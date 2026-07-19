// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Story graph webview app: rete.js renders and lays out the graph (auto-arrange = elk.js),
// React renders toolbar/detail chrome. The extension side (storyGraphPanel.ts) owns all LSP
// traffic; this file only exchanges postMessage envelopes with it.
//
// Editing is mode-based. View/Simulate are read-only. In Edit mode NOTHING is written to disk until
// Save: every mutation is staged (STAGED_KINDS / staging.ts) and the whole queue is flushed to
// aet/applyStoryCommandBatch on Save (or dry-run through aet/validateStoryCommandBatch on Validate).
// Property edits are reflected instantly by an optimistic dto patch; structural gestures
// (create/delete/rename/edges) instead ask the server for a preview graph built from the composed
// working copy (aet/previewStoryGraph - the server does the graph build, the client never
// re-implements it). Staged edits are re-applied after each graph rebuild (reapplyStagedCommands) so
// a reconcile never reverts them.

import {
    CSSProperties, DragEvent, PointerEvent as ReactPointerEvent,
    useCallback, useEffect, useReducer, useRef, useState,
} from 'react';
import { optimisticEdit, PREVIEW_KINDS, STAGED_KINDS } from './staging';
import { createPortal } from 'react-dom';
import { createRoot } from 'react-dom/client';
import { ClassicPreset, GetSchemes, NodeEditor } from 'rete';
import { AreaExtensions, AreaPlugin } from 'rete-area-plugin';
import { AutoArrangePlugin, Presets as ArrangePresets } from 'rete-auto-arrange-plugin';
import { ConnectionPlugin, Presets as ConnectionPresets } from 'rete-connection-plugin';
import { Drag, Presets, ReactArea2D, ReactPlugin, RenderEmit } from 'rete-react-plugin';
import styled, { createGlobalStyle } from 'styled-components';

declare function acquireVsCodeApi(): { postMessage(message: unknown): void };
const vscode = acquireVsCodeApi();

// ── Protocol DTOs (camelCased server records; keep in sync with StoryProtocol.cs) ───────────────

interface StoryGraphNodeDto {
    id: string; kind: string; label: string; threadUri?: string | null; line?: number | null;
    eventType?: string | null; rewardType?: string | null; branch?: string | null;
    lifecycle?: string | null; reachable: boolean;
    eventParams?: { position: number; value: string }[] | null;
    rewardParams?: { position: number; value: string }[] | null;
    perpetual?: boolean; storyDialog?: string | null; storyChapter?: number | null;
}
interface StoryGraphEdgeDto { fromId: string; toId: string; kind: string; label?: string | null; }
interface StoryParamSchemaDto {
    position: number; valueType: string; referenceType?: string | null;
    enumName?: string | null; optional: boolean; description?: string | null;
    enumValues?: string[] | null;
}
interface ParamOption { value: string; detail?: string | null; }
interface StoryDiagnosticDto {
    nodeId?: string | null; side?: string | null; position?: number | null;
    severity: string; message: string; uri: string; line: number; column: number;
}
interface StoryLayoutEntry { file: string; eventName: string; x: number; y: number; }
interface GraphFilters { nameFilter: string; branch: string; lifecycle: string; reachableFrom: string; }
interface SimFlag { name: string; value: number; }
interface SimNodeState { nodeId: string; lifecycle: string; }
interface SimIntervention {
    kind: string; nodeId: string; eventName: string; eventType?: string | null; options: string[];
}
interface SimState {
    running: boolean; clock: number; flags: SimFlag[]; nodes: SimNodeState[];
    interventions: SimIntervention[]; luaNotifications: string[]; log: string[];
}

function sendSim(method: string, args?: Record<string, unknown>): void {
    vscode.postMessage({ type: 'sim', method, args });
}

/**
 * Request broker for param completion candidates: each fetch posts a requestId to the extension
 * (which round-trips aet/getStoryParamOptions) and resolves when the matching 'paramOptions'
 * message returns. The timeout guarantees the suggestion dropdown never hangs on a lost reply.
 */
let optionRequestCounter = 0;
const pendingOptionRequests = new Map<number, (options: ParamOption[]) => void>();

function fetchParamOptions(
    side: 'event' | 'reward', typeName: string, position: number, prefix: string,
): Promise<ParamOption[]> {
    const requestId = ++optionRequestCounter;
    return new Promise(resolve => {
        pendingOptionRequests.set(requestId, resolve);
        vscode.postMessage({ type: 'paramOptions', requestId, side, typeName, position, prefix });
        window.setTimeout(() => {
            if (pendingOptionRequests.delete(requestId)) { resolve([]); }
        }, 3000);
    });
}

const EMPTY_FILTERS: GraphFilters = { nameFilter: '', branch: '', lifecycle: '', reachableFrom: '' };

/** Event/reward type names flagged `untested` in the schema - set once, read during render. */
const untestedTypes = new Set<string>();

/** Event/reward type name → its param schema - set once from the 'schema' message; read by node bodies. */
const eventTypeParams = new Map<string, StoryParamSchemaDto[]>();
const rewardTypeParams = new Map<string, StoryParamSchemaDto[]>();

/** One editable param row on an inline node body. `missing` = mandatory and still unset. */
interface ParamRowSpec { position: number; value: string; missing: boolean; }

/**
 * Rows to render for one param kind on a node body: EVERY schema-declared param (the type
 * dictates the fields - optional ones render as empty "(optional)" slots), plus any value present
 * in the XML beyond what the schema declares (legacy/unknown slots still need to be visible and
 * editable). Shared between rendering and node-height estimation so the two never disagree about
 * how tall the node actually is.
 */
function paramRowSpecs(
    existing: { position: number; value: string }[] | null | undefined,
    schema: StoryParamSchemaDto[],
): ParamRowSpec[] {
    const byPosition = new Map((existing ?? []).map(p => [p.position, p.value]));
    const rows: ParamRowSpec[] = [];
    for (const p of schema) {
        rows.push({
            position: p.position,
            value: byPosition.get(p.position) ?? '',
            missing: !p.optional && !byPosition.has(p.position),
        });
        byPosition.delete(p.position);
    }
    for (const [position, value] of byPosition) {
        rows.push({ position, value, missing: false });
    }
    rows.sort((a, b) => a.position - b.position);
    return rows;
}

const EVENT_NODE_WIDTH = 280;
const EVENT_ROW_H = 22;
const EVENT_HEADER_H = 30;
// Slack added to the measured row height: the header's margin-bottom plus each section head's
// margin-top plus the body's own top/bottom padding aren't counted row-by-row, so without enough
// here the last field sits right on (and just barely clips against) the bottom border.
const EVENT_BODY_PAD = 26;

/** Above this node count, Event nodes start with Trigger/Reward collapsed (perf on big campaigns). */
const LARGE_GRAPH_NODE_COUNT = 60;

type NodeSection = 'general' | 'trigger' | 'reward';

/**
 * Per-node collapse state of the General/Trigger/Reward sections - module scope (not React
 * state) because `estimateEventNodeHeight` must read it when the node is (re)measured, and the
 * node views render through rete's portal pipeline where App state isn't reachable. Never
 * pruned: ids are stable per campaign and the value is three booleans.
 */
const collapsedSections = new Map<string, Record<NodeSection, boolean>>();

function isSectionCollapsed(nodeId: string, section: NodeSection): boolean {
    return collapsedSections.get(nodeId)?.[section] ?? false;
}

function toggleSection(nodeId: string, section: NodeSection): void {
    const state = collapsedSections.get(nodeId) ?? { general: false, trigger: false, reward: false };
    state[section] = !state[section];
    collapsedSections.set(nodeId, state);
    editorHandleRef?.refreshNode(nodeId); // re-measure + re-render just this node
}

/**
 * Diagnostics per node id, from the server's aet/getStoryDiagnostics (via the extension's
 * 'diagnostics' message) - module scope for the same portal-rendering reason as the maps above.
 */
const nodeDiagnostics = new Map<string, StoryDiagnosticDto[]>();

/**
 * `{threadUri}|{name}` keys for events just created via drag/drop whose node should open its
 * rename box the moment it mounts - so naming a new event is one continuous gesture (drop → type),
 * as it was in the old create dialog. Consumed once, on first mount of the matching node.
 */
const pendingAutoRename = new Set<string>();
const autoRenameKey = (threadUri: string | null | undefined, name: string): string =>
    `${threadUri ?? ''}|${name}`;

/**
 * In-progress rename drafts, keyed by node id - module scope (like `collapsedSections`) so an
 * open rename and its typed text SURVIVE a re-mount of the node's React view. A graph refresh
 * (storyGraphChanged) rebuilds node views through rete's portal; a controlled input's local state
 * is lost on re-mount, which reset the field to the old name and dropped the rename entirely.
 * Presence of a key = that node is being renamed; the value is the current draft text.
 */
const renameDrafts = new Map<string, string>();

/**
 * A short row label from a schema param description ("Attacker faction." → "Attacker faction"),
 * or null when the schema has nothing usable - the caller falls back to "Param N".
 */
function shortParamLabel(schema: StoryParamSchemaDto | undefined): string | null {
    const description = schema?.description?.trim();
    if (!description) { return null; }
    const label = description.split(/[(,;.]/)[0].trim();
    return label.length ? label : null;
}

/**
 * A checkbox label from a boolean param's description: the checkbox already encodes the 0/1
 * mechanics, so strip the "1 = " prefix and take just the semantic phrase —
 * "1 = loop the movie; 0 = …" → "Loop the movie". Null when the description has another shape.
 */
function booleanParamLabel(description: string | null | undefined): string | null {
    if (!description) { return null; }
    const match = /^\s*[01]\s*=\s*([^;.(]+)/.exec(description);
    const text = match?.[1].trim();
    return text ? text[0].toUpperCase() + text.slice(1) : null;
}

/**
 * VS Code's themed categorical chart palette - these track the active colour theme (and invert with
 * light/dark), so branch colours belong to the theme rather than being hard-coded hues. Branches
 * beyond the palette length reuse a colour; that's fine for the handful of branches a thread has.
 */
const BRANCH_CHART_VARS = [
    '--vscode-charts-blue',
    '--vscode-charts-green',
    '--vscode-charts-orange',
    '--vscode-charts-purple',
    '--vscode-charts-red',
    '--vscode-charts-yellow',
];

/** Stable themed chart colour per branch name - shared by the node glow and the edge glow. */
function branchColor(branch: string): string {
    let hash = 0;
    for (let i = 0; i < branch.length; i++) { hash = (hash * 31 + branch.charCodeAt(i)) | 0; }
    return `var(${BRANCH_CHART_VARS[Math.abs(hash) % BRANCH_CHART_VARS.length]})`;
}

/**
 * The sankey node glow: a faint branch-coloured background tint plus a soft outer halo, so a
 * branch's member nodes read as belonging to the same coloured strand as its edges. `withBorder`
 * also tints the border (virtual junctions have no lifecycle border to preserve; event nodes keep
 * theirs). One modest box-shadow per branched node - far cheaper than the per-edge SVG filter it
 * replaces. `color-mix` derives the translucent tints from the theme's chart colour.
 */
function branchGlowStyle(branch: string | null, withBorder: boolean): CSSProperties | undefined {
    if (!branch) { return undefined; }
    const c = branchColor(branch);
    const tint = `color-mix(in srgb, ${c} 14%, transparent)`;
    const style: CSSProperties = {
        background: `linear-gradient(${tint}, ${tint}), var(--vscode-editorWidget-background)`,
        boxShadow: `0 0 8px 1px color-mix(in srgb, ${c} 45%, transparent)`,
    };
    if (withBorder) { style.borderColor = `color-mix(in srgb, ${c} 75%, transparent)`; }
    return style;
}

/** Row count → pixel height for an Event node body - kept in lockstep with what EventNodeView renders. */
function estimateEventNodeHeight(dto: StoryGraphNodeDto): number {
    // Expanded General = branch/perpetual/dialog; expanded Trigger/Reward = type chip row + params.
    const generalRows = isSectionCollapsed(dto.id, 'general') ? 0 : 3;
    const eventRows = isSectionCollapsed(dto.id, 'trigger') ? 0
        : 1 + paramRowSpecs(dto.eventParams, eventTypeParams.get(dto.eventType ?? '') ?? []).length;
    const rewardRows = isSectionCollapsed(dto.id, 'reward') ? 0
        : 1 + paramRowSpecs(dto.rewardParams, rewardTypeParams.get(dto.rewardType ?? '') ?? []).length;
    // three section heads + their expanded contents.
    const rows = 3 + generalRows + eventRows + rewardRows;
    return EVENT_HEADER_H + EVENT_BODY_PAD + rows * EVENT_ROW_H;
}

/**
 * Event types that need the manifest-file form (`TacticalCreateBar`) instead of the generic
 * create flow: their manifest param is declared `optional: true` in the schema even though a
 * tactical trigger without one is useless, so generic mandatory-param enforcement can't catch a
 * missing file - this is a UX rule stricter than the schema, kept as an explicit exception.
 * `LINK_TACTICAL` (the reward) needs no such exception: every one of its params is schema-mandatory,
 * so it's just an ordinary reward in the palette's Rewards list.
 */
const TACTICAL_EVENT_TYPES = new Set(['STORY_LAND_TACTICAL', 'STORY_SPACE_TACTICAL']);

/** What a palette drag carries; interpreted on drop to open the matching create form. */
interface PaletteDrag {
    category: 'trigger' | 'reward' | 'tactical' | 'blank' | 'andJunction' | 'orJunction';
    type: string | null;
}
/** A pending create form: the dragged preset plus (if dropped on the canvas) its landing spot. */
interface CreateRequest extends PaletteDrag { position: { x: number; y: number } | null; }
const PALETTE_DRAG_MIME = 'application/x-story-palette';

function baseName(uri: string | null | undefined): string {
    if (!uri) { return ''; }
    const idx = uri.lastIndexOf('/');
    return idx < 0 ? uri : uri.slice(idx + 1);
}

/**
 * Routes a mutation. In Edit mode a staged kind is applied optimistically to the local graph and
 * queued (flushed to the server only on Save - see `stageCommand`); everything else posts straight
 * to the extension (which owns confirmation dialogs and error toasts). View/Simulate never mutate.
 */
function sendCommand(payload: Record<string, unknown>, confirm?: string, refreshDetail?: string): void {
    if (currentMode === 'edit' && STAGED_KINDS.has(payload.kind as string)) {
        if (confirm) {
            // Destructive gesture: the extension owns the modal and the persisted "don't ask again"
            // preference, and replies with `confirmStageResult` telling us whether to stage.
            vscode.postMessage({ type: 'confirmStage', payload, confirm });
        } else {
            stageCommand(payload);
        }
        return;
    }
    vscode.postMessage({ type: 'command', payload, confirm, refreshDetail });
}

// ── Rete setup ───────────────────────────────────────────────────────────────────────────────────

const flowSocket = new ClassicPreset.Socket('flow');

class StoryNode extends ClassicPreset.Node {
    width = 0;
    height = 0;
    dto: StoryGraphNodeDto;
    /** Branch name this node belongs to, for the sankey glow - null when unbranched. */
    branchGlow: string | null = null;

    constructor(dto: StoryGraphNodeDto, hasInputs: boolean, hasOutputs: boolean) {
        super(dto.label);
        // ClassicPreset.Node otherwise self-assigns a random UID here, unrelated to the server's
        // dto id - patch()'s whole reconciliation (editor.getNode(dto.id), connection endpoint
        // matching, incoming.has(node.id)) depends on rete's node identity being the dto id.
        this.id = dto.id;
        this.dto = dto;
        // Event nodes always expose sockets so prereq edges can be drawn to/from them.
        // multipleConnections must be explicit: ClassicPreset.Input defaults it to false (Output
        // defaults to true), so rete-connection-plugin's syncConnections() silently evicts any
        // existing prereq edge into this socket the moment a second one is dropped on it - the
        // AND/OR junction never gets a chance to materialise; the prior source just vanishes.
        if (hasInputs || dto.kind === 'Event') {
            this.addInput('in', new ClassicPreset.Input(flowSocket, undefined, true));
        }
        if (hasOutputs || dto.kind === 'Event') { this.addOutput('out', new ClassicPreset.Output(flowSocket)); }
        this.applyDto(dto);
    }

    /** Refreshes label/size/state from a newer server dto - the in-place update path. */
    applyDto(dto: StoryGraphNodeDto): void {
        this.dto = dto;
        this.label = dto.label;
        // Events carry their own branch; junctions are stamped separately (owner-inherited).
        if (dto.kind === 'Event') { this.branchGlow = dto.branch ?? null; }
        if (dto.kind === 'AndJunction' || dto.kind === 'OrJunction'
            || dto.kind === 'StagingAnd' || dto.kind === 'StagingOr') {
            this.width = 48; this.height = 48;
        } else if (dto.kind === 'Event') {
            this.width = EVENT_NODE_WIDTH;
            this.height = estimateEventNodeHeight(dto);
        } else {
            this.width = Math.max(140, Math.min(dto.label.length, 30) * 6.5 + 32);
            this.height = 40;
        }
    }
}

class StoryConnection extends ClassicPreset.Connection<StoryNode, StoryNode> {
    /** <param name="branch">Branch this prereq edge feeds - drives the sankey-style glow.</param> */
    constructor(
        source: StoryNode, target: StoryNode, public readonly kind: string,
        public readonly branch: string | null = null,
    ) {
        super(source, 'out', target, 'in');
    }
}

type Schemes = GetSchemes<StoryNode, StoryConnection>;
type AreaExtra = ReactArea2D<Schemes>;

interface EditorHandle {
    /**
     * Applies a server graph. `full` clears and auto-arranges (first load, filter changes);
     * otherwise the graph is patched in place - the viewport and node positions stay put.
     */
    setGraph(nodes: StoryGraphNodeDto[], edges: StoryGraphEdgeDto[], layout: StoryLayoutEntry[],
        full: boolean): Promise<void>;
    /** Overrides node lifecycles from the simulation (null restores the static analysis view). */
    applyLifecycles(byNodeId: ReadonlyMap<string, string> | null): void;
    fit(): void;
    /**
     * Recomputes the automatic layout for the current graph, discarding manual positions - the
     * new positions are persisted, so the arrangement survives re-renders and reopening.
     */
    autoArrange(): Promise<void>;
    /** Converts a browser client point (e.g. a drop event) to graph coordinates. */
    toGraphPosition(clientX: number, clientY: number): { x: number; y: number };
    /**
     * Remembers where a not-yet-created event should land once the server confirms it - used by
     * palette drag-and-drop, where the drop position is known well before the event exists.
     */
    presetPosition(threadUri: string, eventName: string, position: { x: number; y: number }): void;
    /**
     * Carries a renamed node's current position to its new name, so the renamed event (which gets a
     * new node id derived from its name) reappears exactly where it was instead of being re-placed
     * beside a neighbour. No-op if the old node isn't currently laid out.
     */
    carryPosition(oldNodeId: string, threadUri: string | null | undefined, newName: string): void;
    /**
     * Resolves an Event node's identity and current types from its id - used by the
     * drag-a-type-onto-a-node gesture, which only has a `data-node-id` DOM attribute to go on,
     * not a rete node reference. Null for an unknown id or a non-Event (virtual) node.
     */
    getEventNode(nodeId: string): {
        threadUri: string | null; eventName: string;
        eventType: string | null; rewardType: string | null;
    } | null;
    /** Brings one node into view and flashes it - the problems list's jump-to. */
    centerNode(nodeId: string): void;
    /**
     * The thread file a new event dropped at `position` should belong to: the nearest existing
     * event node's thread, else the first thread. Null only when the campaign has no thread.
     */
    nearestEventThread(position: { x: number; y: number }, threads: string[]): string | null;
    /** Every current event node label - used to pick a unique default name for a new event. */
    eventLabels(): string[];
    /** The current viewport centre in graph coordinates - where a toolbar-created node lands. */
    viewportCentre(): { x: number; y: number };
    /** Node rects + the current viewport rect, all in graph coordinates, for the dock minimap. */
    getMinimap(): {
        nodes: { x: number; y: number; w: number; h: number }[];
        viewport: { x: number; y: number; w: number; h: number };
    };
    /** Pans the viewport so (graphX, graphY) sits at the centre - the minimap's click-to-navigate. */
    panTo(graphX: number, graphY: number): void;
    /** The area transform (k = zoom, x/y = pan), so overlays can place themselves over the nodes. */
    /**
     * The in-holder element the swimlane overlay portals into. Being inside rete's transformed
     * content holder is what keeps the lanes pinned to the nodes during pan/zoom.
     */
    getSwimlaneLayer(): HTMLElement;
    /** Bounding boxes (graph coords) of Event nodes grouped by thread or chapter - the swimlanes. */
    getGroupBounds(by: 'thread' | 'chapter'): {
        key: string; title: string; x: number; y: number; w: number; h: number;
    }[];
    /**
     * Drops a local-only staging AND/OR-junction at the given position - no server round trip.
     * The user wires Event outputs into its input to accumulate prereq sources, then drags its
     * output onto a target Event to commit them: AND = all sources as one new AND-line, OR = one
     * new prereq line per source (see the `connectioncreate` pipe).
     */
    createStagingJunction(position: { x: number; y: number }, kind: 'and' | 'or'): void;
    /** Discards an unattached staging junction (its own "×" button) - never sent to the server. */
    discardStagingJunction(nodeId: string): void;
    /**
     * Re-renders every node's body - needed on top of `applyLifecycles`' targeted updates because
     * switching Edit/View/Simulate mode changes an Event node's own layout (the blank "add a new
     * param" row appears/disappears, inputs enable/disable), not just its lifecycle border.
     */
    refreshMode(): void;
    /** Re-measures and re-renders one node - e.g. after its section collapse state changed. */
    refreshNode(nodeId: string): void;
    /**
     * Optimistically updates one Event node's dto and re-renders it - the Edit-mode staging path,
     * so a property change shows instantly without a server round trip. No-op for unknown ids.
     */
    patchEventNode(nodeId: string, update: (dto: StoryGraphNodeDto) => StoryGraphNodeDto): void;
    /**
     * Repaints only the given nodes (no re-measure - diagnostics don't change height). Used by the
     * diagnostics push so a validation refresh touches just the nodes whose markers changed,
     * instead of re-rendering every node like `refreshMode`.
     */
    repaintNodes(nodeIds: Iterable<string>): void;
    destroy(): void;
}

/** Set by the React app before the editor exists; invoked from an Event node's "reachable from
 * here" button - a node body has no direct line to App's `setFilter`, so it goes through this
 * bridge, the same pattern `onGraphDesynced` already uses. */
let onReachableFromRequested: (nodeId: string) => void = () => { /* replaced by App */ };

/** A gesture locally changed the graph without a server command - re-fetch to reconcile. */
let onGraphDesynced: () => void = () => { /* replaced by App */ };

/** Subscribers (the minimap) notified when the viewport pans/zooms or a node moves. */
const areaChangeSubs = new Set<() => void>();
function subscribeAreaChange(cb: () => void): () => void {
    areaChangeSubs.add(cb);
    return () => { areaChangeSubs.delete(cb); };
}
let areaChangeScheduled = false;
function scheduleAreaChanged(): void {
    if (areaChangeScheduled) { return; }
    areaChangeScheduled = true;
    requestAnimationFrame(() => {
        areaChangeScheduled = false;
        for (const cb of areaChangeSubs) { cb(); }
    });
}

/**
 * Subscribers notified when node *geometry* changes (a node moved, or the graph was rebuilt) —
 * deliberately NOT on pan/zoom. The swimlane overlay lives inside rete's transformed content
 * holder, so panning moves it for free; recomputing its bounds per pan frame would walk every node
 * for nothing.
 */
const geometryChangeSubs = new Set<() => void>();
function subscribeGeometryChange(cb: () => void): () => void {
    geometryChangeSubs.add(cb);
    return () => { geometryChangeSubs.delete(cb); };
}
let geometryChangeScheduled = false;
function scheduleGeometryChanged(): void {
    if (geometryChangeScheduled) { return; }
    geometryChangeScheduled = true;
    requestAnimationFrame(() => {
        geometryChangeScheduled = false;
        for (const cb of geometryChangeSubs) { cb(); }
    });
}

/** Set by App once the editor exists - VirtualNodeView's staging-junction discard button needs
 * to call straight into the editor (`discardStagingJunction`), not through a command round trip. */
let editorHandleRef: EditorHandle | null = null;

type EditorMode = 'view' | 'edit' | 'simulate';

/**
 * The App keeps this in sync with its `mode` state so the rete pipes below (created once,
 * outside React) can gate gestures on it without recreating the editor on every mode change.
 */
let currentMode: EditorMode = 'view';

// ── Edit-mode staging ──────────────────────────────────────────────────────────────────────────
//
// In Edit mode, gestures don't round-trip per change (the old behaviour - sluggish even on a
// checkbox). Each staged command mutates the local graph immediately and is queued here; the whole
// queue is flushed to aet/applyStoryCommandBatch on Save, or dry-run through
// aet/validateStoryCommandBatch on Validate. The command payloads are exactly the server's command
// envelopes, so batching them needs no client-side model logic.

/** The queued command envelopes, in gesture order - flushed to the server only on Save. */
const pendingCommands: Record<string, unknown>[] = [];

/** App subscribes so the toolbar's Save/Validate buttons reflect the pending count. */
let onPendingChanged: () => void = () => { /* replaced by App */ };

/** Set by App: asks the extension for a preview graph over the current pending queue. */
let requestPreview: () => void = () => { /* replaced by App */ };

// Chain of staged renames (oldName → latest name). A gesture reads the node's dto.label, which lags
// a staged rename until the preview lands - so an edit made in that window would carry a name the
// batch no longer knows once its rename runs. Resolving through this map keeps every staged command
// pointed at the event's latest name, so the batch composes in order without "event not found".
const stagedRenames = new Map<string, string>();

/** Whether there are unsaved staged changes - drives the dirty-exit prompt and Save button. */
function hasPendingChanges(): boolean {
    return pendingCommands.length > 0;
}

function clearPendingCommands(): void {
    pendingCommands.length = 0;
    stagedRenames.clear();
    onPendingChanged();
}

/** Follows the staged-rename chain to the event's latest name. */
function resolveStagedName(name: string): string {
    let current = name;
    for (let i = 0; i < 64; i++) { // bounded against a pathological cycle
        const next = stagedRenames.get(current.toLowerCase());
        if (next === undefined || next === current) { break; }
        current = next;
    }
    return current;
}

/** Applies a staged command to the local graph, queues it, and previews structural changes. */
function stageCommand(payload: Record<string, unknown>): void {
    // Retarget to the event's latest staged name (dto.label may still show a pre-rename name).
    if (typeof payload.eventName === 'string') {
        const resolved = resolveStagedName(payload.eventName);
        if (resolved !== payload.eventName) { payload = { ...payload, eventName: resolved }; }
    }
    if (payload.kind === 'renameEvent' && typeof payload.newName === 'string') {
        stagedRenames.set((payload.eventName as string).toLowerCase(), payload.newName);
    }

    applyOptimistic(payload);
    pendingCommands.push(payload);
    onPendingChanged();

    // Structural gestures have no cheap local representation - let the server rebuild the graph from
    // the composed working copy (no disk write) and re-render from that.
    if (PREVIEW_KINDS.has(payload.kind as string)) { schedulePreview(); }
}

// Coalesce rapid structural gestures (e.g. a junction commit fires addPrereqGroup) into one preview.
let previewTimer: ReturnType<typeof setTimeout> | null = null;
function schedulePreview(): void {
    if (previewTimer !== null) { clearTimeout(previewTimer); }
    previewTimer = setTimeout(() => { previewTimer = null; requestPreview(); }, 40);
}

/**
 * Re-applies every staged command on top of a freshly (re)built graph. Staged commands aren't
 * committed, so any reconcile (a filter change, a preview, or a post-save push) would otherwise
 * revert the optimistic property edits. Replaying them is idempotent - the staged view survives
 * every rebuild until Save flushes the queue (and clears it, so a post-save rebuild shows committed
 * truth). Structural kinds are already baked into a preview graph, so their replay is a no-op.
 */
function reapplyStagedCommands(): void {
    for (const payload of pendingCommands) { applyOptimistic(payload); }
}

/** Reflects a staged command in the local graph so Edit mode feels instant. */
function applyOptimistic(payload: Record<string, unknown>): void {
    const handle = editorHandleRef;
    if (!handle) { return; }
    const edit = optimisticEdit(payload);
    if (edit) { handle.patchEventNode(edit.nodeId, edit.apply); }
}

/** AND-junction node ids embed their owner event and group index: `{eventNodeId}#g{index}`. */
const andJunctionId = /^(.*)#g(\d+)$/;

/**
 * elk auto-arrange options - Event nodes are full-blueprint-style forms, several times taller
 * than plain boxes; the default spacing crowded them together enough to overlap.
 */
const ARRANGE_OPTIONS = {
    'elk.direction': 'RIGHT',
    'elk.spacing.nodeNode': '60',
    'elk.layered.spacing.nodeNodeBetweenLayers': '90',
};

async function createEditor(container: HTMLElement): Promise<EditorHandle> {
    const editor = new NodeEditor<Schemes>();
    const area = new AreaPlugin<Schemes, AreaExtra>(container);
    const render = new ReactPlugin<Schemes, AreaExtra>({ createRoot });
    const arrange = new AutoArrangePlugin<Schemes>();
    const connection = new ConnectionPlugin<Schemes, AreaExtra>();

    render.addPreset(Presets.classic.setup({
        customize: {
            node: () => StoryNodeView,
            connection: () => StoryConnectionView,
            socket: () => StorySocketView,
        },
    }));
    arrange.addPreset(ArrangePresets.classic.setup());
    connection.addPreset(ConnectionPresets.classic.setup());

    editor.use(area);
    area.use(render);
    area.use(arrange);
    area.use(connection);

    AreaExtensions.selectableNodes(area, AreaExtensions.selector(), {
        accumulating: AreaExtensions.accumulateOnCtrl(),
    });
    AreaExtensions.simpleNodesOrder(area);

    // Promote the transformed content to its own GPU layer so pan/zoom composites instead of
    // repainting the whole node tree each frame - a cheap, large win on big campaigns.
    (area.area.content.holder as HTMLElement).style.willChange = 'transform';

    // Swimlanes live INSIDE the transformed content holder, so rete's own pan/zoom transform moves
    // them in the same compositor frame as the nodes. The earlier sibling-overlay version recomputed
    // screen positions in JS one rAF later, which is exactly what made the lanes visibly trail the
    // graph while dragging. Depth is handled by `.swimlane-layer`'s z-index, not by this position —
    // rete re-orders the holder's children as the graph changes.
    const swimlaneLayer = document.createElement('div');
    swimlaneLayer.className = 'swimlane-layer';
    (area.area.content.holder as HTMLElement).prepend(swimlaneLayer);

    // Sankey-style branch glow: a node's own branch (events) or its owner event's (AND/OR
    // junctions inherit it, so the coloured strand stays unbroken across them). Resolved from the
    // dto set at build/patch time - see `stampBranchGlow` - and stamped onto `StoryNode.branchGlow`
    // so the node/connection views paint it without any editor lookup on the hot render path.
    const junctionOwnerId = (id: string): string | null =>
        andJunctionId.exec(id)?.[1] ?? (id.endsWith('#or') ? id.slice(0, -'#or'.length) : null);

    const branchOfNodeId = (id: string, branchByEvent: Map<string, string>): string | null => {
        if (branchByEvent.has(id)) { return branchByEvent.get(id)!; }
        const owner = junctionOwnerId(id);
        return owner ? branchByEvent.get(owner) ?? null : null;
    };

    /** The branch a prereq edge belongs to (its target's, source as fallback) - the glow colour key. */
    const edgeBranchFrom = (
        fromId: string, toId: string, kind: string, branchByEvent: Map<string, string>
    ): string | null => {
        if (kind !== 'Prereq') { return null; }
        return branchOfNodeId(toId, branchByEvent) ?? branchOfNodeId(fromId, branchByEvent);
    };

    /** Event id → branch, for a whole incoming graph - one build, reused for every node and edge. */
    const branchIndex = (nodes: StoryGraphNodeDto[]): Map<string, string> => {
        const map = new Map<string, string>();
        for (const dto of nodes) { if (dto.kind === 'Event' && dto.branch) { map.set(dto.id, dto.branch); } }
        return map;
    };

    /** Stamps `branchGlow` on every live node (junctions included) from the incoming branch index. */
    const stampBranchGlow = (branchByEvent: Map<string, string>): void => {
        for (const node of editor.getNodes()) {
            const next = branchOfNodeId(node.id, branchByEvent);
            if (next !== node.branchGlow) {
                node.branchGlow = next;
                void area.update('node', node.id);
            }
        }
    };

    let applyingServerGraph = false;
    // Serializes setGraph calls: buildFull/patch mutate shared rete state step by step across many
    // awaits, so two overlapping calls (e.g. a rapid pair of server pushes) interleave their
    // mutations and corrupt the model - nodes misjudged as new, connections dropped, etc.
    let graphQueue: Promise<unknown> = Promise.resolve();

    /** Numbers `local:and:N` staging-junction ids - unique for this session, never sent to the server. */
    let stagingCounter = 0;

    /**
     * rete's `editor.removeNode` only splices the node out of the node list - connections attached
     * to it stay in the editor and render as edges pointing at nothing (and `patch()` deliberately
     * never reconciles `local:` connections away). Every staging-node removal must go through here.
     */
    const removeNodeWithConnections = async (id: string): Promise<void> => {
        for (const connection of [...editor.getConnections()]) {
            if (connection.source === id || connection.target === id) {
                await editor.removeConnection(connection.id);
            }
        }
        await editor.removeNode(id);
    };

    /**
     * Re-applies a node's dto and pushes the recomputed size into the area. The size MUST go
     * through `area.resize`: auto-arrange stamped every node element with an INLINE width/height
     * style (AreaPlugin's node view does that on resize), and inline styles beat the styled
     * component's `$w`/`$h` classes - `applyDto` alone updates the node object while the DOM
     * keeps the stale size. `area.resize` also re-anchors the node's connections.
     */
    const remeasure = async (node: StoryNode, dto?: StoryGraphNodeDto): Promise<void> => {
        node.applyDto(dto ?? node.dto);
        await area.resize(node.id, node.width, node.height);
        await area.update('node', node.id);
    };

    // Where a server-synthesized AND/OR junction should land, keyed by its owner Event node id —
    // set when a staging junction is committed (the junction should appear where the user had
    // placed the staging node, not at placeNewNode's centroid), consumed by patch(). Kind-matched:
    // one commit can materialise BOTH junction kinds at once (a multi-token group added to an
    // event with one existing prereq line creates its AND junction and the OR junction) and only
    // the kind the user actually placed should inherit the staging spot.
    const pendingJunctionPositions = new Map<string, { kind: string; x: number; y: number }>();

    /** The event a junction belongs to (AND junctions embed their owner in the id). */
    const junctionOwner = (junction: StoryNode): { owner: StoryNode; groupIndex: number } | null => {
        const match = andJunctionId.exec(junction.id);
        const owner = match ? editor.getNode(match[1]) : undefined;
        return owner?.dto.kind === 'Event' && owner.dto.threadUri
            ? { owner, groupIndex: Number(match![2]) }
            : null;
    };

    /** The Event labels currently wired into a staging AND-junction's input, in connection order. */
    const stagingSources = (junctionId: string): string[] =>
        editor.getConnections()
            .filter(c => c.target === junctionId)
            .map(c => editor.getNode(c.source)?.dto.label)
            .filter((label): label is string => Boolean(label));

    // Edge gestures. Creating: event→event becomes a new OR-line prereq, event→AND-junction joins
    // that AND-line, event→staging-junction (a local-only AND/OR node the user dropped from the
    // palette) just wires up locally with no server command yet, staging-junction→event is the
    // commit gesture (AND: every accumulated source becomes one new AND-line via addPrereqGroup;
    // OR: one new prereq line per source via addPrereqAlternatives - atomically either way, then
    // the staging node is discarded and the real server-synthesized junction arrives with the
    // next graph push). Every case except the two
    // local-only staging ones blocks local materialisation - the real edge arrives with the
    // server's re-render. Removing (the connection plugin lets you pick an existing connection off
    // a socket): prereq edges become removePrereq commands; a wire into a still-unattached staging
    // node is removed locally with no server round trip (nothing was ever sent for it); anything
    // else (control/flag/tactical edges are derived data, junction plumbing is structural) is not
    // removable - the local removal is allowed to play out and a re-fetch restores it.
    editor.addPipe(context => {
        if (context.type === 'connectioncreate' && !applyingServerGraph) {
            if (currentMode !== 'edit') { return undefined; }
            const source = editor.getNode(context.data.source);
            const target = editor.getNode(context.data.target);
            if (source?.dto.kind === 'Event'
                && (target?.dto.kind === 'StagingAnd' || target?.dto.kind === 'StagingOr')) {
                return context; // local bookkeeping only - nothing to send yet
            }
            if ((source?.dto.kind === 'StagingAnd' || source?.dto.kind === 'StagingOr')
                && target?.dto.kind === 'Event' && target.dto.threadUri) {
                const tokens = stagingSources(source.id);
                if (tokens.length) {
                    sendCommand({
                        // AND: all tokens on one new prereq line; OR: one new line per token.
                        kind: source.dto.kind === 'StagingAnd' ? 'addPrereqGroup' : 'addPrereqAlternatives',
                        threadUri: target.dto.threadUri,
                        eventName: target.dto.label,
                        tokens,
                    });
                    // The server-synthesized junction should appear where the staging node stood.
                    const position = area.nodeViews.get(source.id)?.position;
                    if (position) {
                        pendingJunctionPositions.set(target.id, {
                            kind: source.dto.kind === 'StagingAnd' ? 'AndJunction' : 'OrJunction',
                            x: position.x, y: position.y,
                        });
                    }
                    // consumed - the real junction arrives via push
                    void removeNodeWithConnections(source.id);
                }
                return undefined;
            }
            if (source?.dto.kind === 'Event' && target?.dto.kind === 'Event' && target.dto.threadUri) {
                sendCommand({
                    kind: 'addPrereq',
                    threadUri: target.dto.threadUri,
                    eventName: target.dto.label,
                    token: source.dto.label,
                });
            } else if (source?.dto.kind === 'Event' && target?.dto.kind === 'AndJunction') {
                const junction = junctionOwner(target);
                if (junction) {
                    sendCommand({
                        kind: 'addPrereq',
                        threadUri: junction.owner.dto.threadUri,
                        eventName: junction.owner.dto.label,
                        groupIndex: junction.groupIndex,
                        token: source.dto.label,
                    });
                }
            }
            return undefined; // never materialise gesture connections locally
        }
        if (context.type === 'connectionremove' && !applyingServerGraph) {
            if (currentMode !== 'edit') { return undefined; } // freeze: block the local removal too
            const removed = context.data as StoryConnection;
            const source = editor.getNode(removed.source);
            const target = editor.getNode(removed.target);
            if (target?.dto.kind === 'StagingAnd' || target?.dto.kind === 'StagingOr') {
                return context; // discarding one accumulated source - nothing was ever sent for it
            }
            const isPrereq = (removed.kind ?? 'Prereq') === 'Prereq';
            if (isPrereq && source?.dto.kind === 'Event'
                && target?.dto.kind === 'Event' && target.dto.threadUri) {
                sendCommand({
                    kind: 'removePrereq',
                    threadUri: target.dto.threadUri,
                    eventName: target.dto.label,
                    token: source.dto.label,
                });
            } else if (isPrereq && source?.dto.kind === 'Event' && target?.dto.kind === 'AndJunction') {
                const junction = junctionOwner(target);
                if (junction) {
                    sendCommand({
                        kind: 'removePrereq',
                        threadUri: junction.owner.dto.threadUri,
                        eventName: junction.owner.dto.label,
                        groupIndex: junction.groupIndex,
                        token: source.dto.label,
                    });
                } else {
                    onGraphDesynced();
                }
            } else {
                onGraphDesynced();
            }
            return context; // allow the local removal; the server round-trip reconciles
        }
        return context;
    });

    const saveAllPositions = (): void => {
        const entries: StoryLayoutEntry[] = [];
        for (const node of editor.getNodes()) {
            if (node.dto.kind !== 'Event') { continue; }
            const view = area.nodeViews.get(node.id);
            if (!view) { continue; }
            entries.push({
                file: baseName(node.dto.threadUri),
                eventName: node.dto.label,
                x: view.position.x,
                y: view.position.y,
            });
        }
        if (entries.length) { vscode.postMessage({ type: 'saveLayout', entries }); }
    };

    area.addPipe(context => {
        if (context.type === 'nodetranslate' && currentMode !== 'edit' && !applyingServerGraph) {
            return undefined; // freeze user-driven dragging outside Edit mode; auto-layout and
                               // stored-position restores (during setGraph) still go through
        }
        if (context.type === 'nodedragged' && currentMode === 'edit') {
            saveAllPositions();
        }
        // Keep the dock minimap in sync with pan/zoom and node moves (rAF-throttled).
        if (context.type === 'translated' || context.type === 'zoomed'
            || context.type === 'nodetranslated') {
            scheduleAreaChanged();
        }
        // Swimlane bounds only depend on where the nodes are, not on the viewport.
        if (context.type === 'nodetranslated') {
            scheduleGeometryChanged();
        }
        return context;
    });

    // Original (static analysis) lifecycles, so ending a simulation restores the pre-sim view.
    const staticLifecycles = new Map<string, string | null | undefined>();

    const layoutKey = (dto: StoryGraphNodeDto): string =>
        `${baseName(dto.threadUri)} ${dto.label}`.toLowerCase();

    const connectionKey = (fromId: string, toId: string, kind: string): string =>
        `${fromId}>${toId}|${kind}`;

    // Palette drag-and-drop: the drop position is known before the event exists server-side, so
    // it's stashed here (keyed the same way as `layout`) and consumed the moment the new node
    // shows up in a patch.
    const pendingDropPositions = new Map<string, { x: number; y: number }>();

    const average = (positions: { x: number; y: number }[]): { x: number; y: number } => ({
        x: positions.reduce((sum, p) => sum + p.x, 0) / positions.length,
        y: positions.reduce((sum, p) => sum + p.y, 0) / positions.length,
    });

    /**
     * A sensible spot for a node the server introduced mid-session. Junctions (and any node with
     * several relevant edges, e.g. an AND-junction with many prereq sources) are placed at the
     * midpoint between the average position of their already-rendered sources and targets, rather
     * than beside whichever single neighbour happened to be first in the edge list - otherwise the
     * junction lands next to one arbitrary parent instead of between the events it actually joins.
     */
    const placeNewNode = (dto: StoryGraphNodeDto, edges: StoryGraphEdgeDto[]): { x: number; y: number } => {
        const sources: { x: number; y: number }[] = [];
        const targets: { x: number; y: number }[] = [];
        for (const edge of edges) {
            if (edge.toId === dto.id) {
                const view = area.nodeViews.get(edge.fromId);
                if (view) { sources.push(view.position); }
            } else if (edge.fromId === dto.id) {
                const view = area.nodeViews.get(edge.toId);
                if (view) { targets.push(view.position); }
            }
        }
        if (sources.length && targets.length) {
            const s = average(sources);
            const t = average(targets);
            return { x: (s.x + t.x) / 2, y: (s.y + t.y) / 2 };
        }
        if (sources.length) {
            const s = average(sources);
            return { x: s.x + 220, y: s.y };
        }
        if (targets.length) {
            const t = average(targets);
            return { x: t.x - 220, y: t.y };
        }
        const { k, x, y } = area.area.transform;
        const rect = container.getBoundingClientRect();
        return { x: (rect.width / 2 - x) / k, y: (rect.height / 2 - y) / k };
    };

    const buildFull = async (
        nodes: StoryGraphNodeDto[], edges: StoryGraphEdgeDto[], layout: StoryLayoutEntry[]
    ): Promise<void> => {
        await editor.clear();
        pendingJunctionPositions.clear();
        const byId = new Map<string, StoryNode>();
        const branches = branchIndex(nodes);
        const hasIn = new Set(edges.map(e => e.toId));
        const hasOut = new Set(edges.map(e => e.fromId));
        for (const dto of nodes) {
            const node = new StoryNode(dto, hasIn.has(dto.id), hasOut.has(dto.id));
            node.branchGlow = branchOfNodeId(dto.id, branches);
            byId.set(dto.id, node);
            await editor.addNode(node);
        }
        for (const edge of edges) {
            const source = byId.get(edge.fromId);
            const target = byId.get(edge.toId);
            if (source && target) {
                await editor.addConnection(new StoryConnection(source, target, edge.kind,
                    edgeBranchFrom(edge.fromId, edge.toId, edge.kind, branches)));
            }
        }
        await arrange.layout({ options: ARRANGE_OPTIONS });

        // Stored positions win over auto-layout for the events that have them.
        const stored = new Map(layout.map(e => [`${e.file} ${e.eventName}`.toLowerCase(), e]));
        for (const node of editor.getNodes()) {
            if (node.dto.kind !== 'Event') { continue; }
            const entry = stored.get(layoutKey(node.dto));
            if (entry) { await area.translate(node.id, { x: entry.x, y: entry.y }); }
        }

        void AreaExtensions.zoomAt(area, editor.getNodes());
    };

    /** Reconciles the live graph against the server's - no re-layout, viewport untouched. */
    const patch = async (
        nodes: StoryGraphNodeDto[], edges: StoryGraphEdgeDto[], layout: StoryLayoutEntry[]
    ): Promise<void> => {
        const incoming = new Map(nodes.map(d => [d.id, d]));
        const branches = branchIndex(nodes);
        const hasIn = new Set(edges.map(e => e.toId));
        const hasOut = new Set(edges.map(e => e.fromId));
        const incomingConnections = new Set(edges.map(e => connectionKey(e.fromId, e.toId, e.kind)));

        // Local-only staging junctions (dropped from the palette, not yet wired to a target event)
        // are never known to the server - reconciliation must leave them and their wires alone
        // rather than treating them as stale.
        const isLocal = (id: string): boolean => id.startsWith('local:');

        // 1. Stale connections go first (their endpoints may be about to disappear).
        for (const connection of [...editor.getConnections()]) {
            if (isLocal(connection.source) || isLocal(connection.target)) { continue; }
            if (!incomingConnections.has(connectionKey(connection.source, connection.target, connection.kind))) {
                await editor.removeConnection(connection.id);
            }
        }

        // 2. Stale nodes.
        for (const node of [...editor.getNodes()]) {
            if (isLocal(node.id)) { continue; }
            if (!incoming.has(node.id)) { await editor.removeNode(node.id); }
        }

        // 3. Existing nodes update in place; nodes whose socket shape changed are rebuilt at
        //    their current position; genuinely new nodes appear beside a neighbour.
        const stored = new Map(layout.map(e => [`${e.file} ${e.eventName}`.toLowerCase(), e]));
        let placedPending = false;
        for (const dto of nodes) {
            const needsIn = hasIn.has(dto.id) || dto.kind === 'Event';
            const needsOut = hasOut.has(dto.id) || dto.kind === 'Event';
            const existing = editor.getNode(dto.id);
            if (existing) {
                if (existing.hasInput('in') !== needsIn || existing.hasOutput('out') !== needsOut) {
                    const position = area.nodeViews.get(dto.id)?.position;
                    await editor.removeNode(dto.id);
                    const rebuilt = new StoryNode(dto, needsIn, needsOut);
                    await editor.addNode(rebuilt);
                    if (position) { await area.translate(rebuilt.id, position); }
                } else {
                    await remeasure(existing, dto);
                }
            } else {
                const node = new StoryNode(dto, needsIn, needsOut);
                node.branchGlow = branchOfNodeId(dto.id, branches);
                await editor.addNode(node);
                const key = dto.kind === 'Event' ? layoutKey(dto) : null;
                const pending = key ? pendingDropPositions.get(key) : undefined;
                if (pending && key) { pendingDropPositions.delete(key); placedPending = true; }
                // A junction born from a staging-node commit lands where the staging node stood.
                const owner = dto.kind === 'AndJunction' ? andJunctionId.exec(dto.id)?.[1]
                    : dto.kind === 'OrJunction' && dto.id.endsWith('#or') ? dto.id.slice(0, -'#or'.length)
                    : undefined;
                const pendingEntry = owner ? pendingJunctionPositions.get(owner) : undefined;
                const junctionPending = pendingEntry?.kind === dto.kind
                    ? { x: pendingEntry.x, y: pendingEntry.y }
                    : undefined;
                if (junctionPending && owner) { pendingJunctionPositions.delete(owner); }
                const entry = key ? stored.get(key) : undefined;
                await area.translate(node.id, pending ?? junctionPending ?? (entry
                    ? { x: entry.x, y: entry.y }
                    : placeNewNode(dto, edges.filter(e => e.fromId === dto.id || e.toId === dto.id))));
            }
        }
        if (placedPending) { saveAllPositions(); }

        // 4. New connections.
        const present = new Set(editor.getConnections()
            .map(c => connectionKey(c.source, c.target, c.kind)));
        for (const edge of edges) {
            if (present.has(connectionKey(edge.fromId, edge.toId, edge.kind))) { continue; }
            const source = editor.getNode(edge.fromId);
            const target = editor.getNode(edge.toId);
            if (source && target) {
                await editor.addConnection(new StoryConnection(source, target, edge.kind,
                    edgeBranchFrom(edge.fromId, edge.toId, edge.kind, branches)));
            }
        }

        // Junctions inherit their owner event's branch; an event's branch may have just changed,
        // so re-stamp every junction glow (events already synced via applyDto).
        stampBranchGlow(branches);
    };

    return {
        setGraph(
            nodes: StoryGraphNodeDto[], edges: StoryGraphEdgeDto[], layout: StoryLayoutEntry[],
            full: boolean
        ): Promise<void> {
            const run = async (): Promise<void> => {
                staticLifecycles.clear();
                for (const dto of nodes) { staticLifecycles.set(dto.id, dto.lifecycle); }
                applyingServerGraph = true;
                try {
                    if (full || editor.getNodes().length === 0) {
                        await buildFull(nodes, edges, layout);
                    } else {
                        await patch(nodes, edges, layout);
                    }
                } finally {
                    applyingServerGraph = false;
                    // Node *removals* emit no 'nodetranslated', so the pipe alone would leave a
                    // lane for a thread whose last event just vanished.
                    scheduleGeometryChanged();
                }
            };
            // Chain onto the queue regardless of whether the previous run succeeded or threw, so
            // one failed application doesn't wedge every graph update after it.
            const result = graphQueue.then(run, run);
            graphQueue = result.catch(() => undefined);
            return result;
        },
        applyLifecycles(byNodeId: ReadonlyMap<string, string> | null): void {
            for (const node of editor.getNodes()) {
                if (node.dto.kind !== 'Event') { continue; }
                const next = byNodeId
                    ? byNodeId.get(node.id) ?? node.dto.lifecycle
                    : staticLifecycles.get(node.id);
                if (next !== node.dto.lifecycle) {
                    node.dto.lifecycle = next ?? null;
                    void area.update('node', node.id);
                }
            }
        },
        fit(): void {
            void AreaExtensions.zoomAt(area, editor.getNodes());
        },
        autoArrange(): Promise<void> {
            const run = async (): Promise<void> => {
                // nodetranslate is frozen outside Edit mode for USER gestures - the arrange
                // plugin's translations must pass, same as during setGraph.
                applyingServerGraph = true;
                try {
                    await arrange.layout({ options: ARRANGE_OPTIONS });
                } finally {
                    applyingServerGraph = false;
                }
                void AreaExtensions.zoomAt(area, editor.getNodes());
                saveAllPositions(); // the recomputed layout replaces the saved one
            };
            // Same serialization as setGraph - arranging mid-patch would interleave mutations.
            const result = graphQueue.then(run, run);
            graphQueue = result.catch(() => undefined);
            return result;
        },
        toGraphPosition(clientX: number, clientY: number): { x: number; y: number } {
            const { k, x, y } = area.area.transform;
            const rect = container.getBoundingClientRect();
            return { x: (clientX - rect.left - x) / k, y: (clientY - rect.top - y) / k };
        },
        presetPosition(threadUri: string, eventName: string, position: { x: number; y: number }): void {
            pendingDropPositions.set(`${baseName(threadUri)} ${eventName}`.toLowerCase(), position);
        },
        carryPosition(oldNodeId: string, threadUri: string | null | undefined, newName: string): void {
            const view = area.nodeViews.get(oldNodeId);
            if (!view) { return; }
            pendingDropPositions.set(`${baseName(threadUri)} ${newName}`.toLowerCase(),
                { x: view.position.x, y: view.position.y });
        },
        getEventNode(nodeId: string): {
            threadUri: string | null; eventName: string;
            eventType: string | null; rewardType: string | null;
        } | null {
            const node = editor.getNode(nodeId);
            if (!node || node.dto.kind !== 'Event') { return null; }
            return {
                threadUri: node.dto.threadUri ?? null,
                eventName: node.dto.label,
                eventType: node.dto.eventType ?? null,
                rewardType: node.dto.rewardType ?? null,
            };
        },
        centerNode(nodeId: string): void {
            const node = editor.getNode(nodeId);
            if (!node) { return; }
            void AreaExtensions.zoomAt(area, [node]);
            // Flash the node so it's findable in a large graph. Toggle a class on the node's DOM
            // wrapper (a forced reflow restarts the animation if the same node is re-jumped); the
            // keyframes' box-shadow transiently overrides the branch-glow inline box-shadow.
            const element = area.nodeViews.get(nodeId)?.element as HTMLElement | undefined;
            if (element) {
                element.classList.remove('story-flash');
                void element.offsetWidth;
                element.classList.add('story-flash');
                window.setTimeout(() => element.classList.remove('story-flash'), 1600);
            }
        },
        nearestEventThread(position: { x: number; y: number }, threads: string[]): string | null {
            let best: string | null = null;
            let bestDist = Infinity;
            for (const node of editor.getNodes()) {
                if (node.dto.kind !== 'Event' || !node.dto.threadUri) { continue; }
                const view = area.nodeViews.get(node.id);
                if (!view) { continue; }
                const dx = view.position.x + node.width / 2 - position.x;
                const dy = view.position.y + node.height / 2 - position.y;
                const dist = dx * dx + dy * dy;
                if (dist < bestDist) { bestDist = dist; best = node.dto.threadUri; }
            }
            return best ?? threads[0] ?? null;
        },
        eventLabels(): string[] {
            return editor.getNodes().filter(n => n.dto.kind === 'Event').map(n => n.dto.label);
        },
        viewportCentre(): { x: number; y: number } {
            const { k, x, y } = area.area.transform;
            const rect = container.getBoundingClientRect();
            return { x: (rect.width / 2 - x) / k, y: (rect.height / 2 - y) / k };
        },
        getMinimap() {
            const nodes = editor.getNodes().map(n => {
                const view = area.nodeViews.get(n.id);
                return { x: view?.position.x ?? 0, y: view?.position.y ?? 0, w: n.width, h: n.height };
            });
            const { k, x, y } = area.area.transform;
            const rect = container.getBoundingClientRect();
            // Visible canvas mapped back into graph coordinates (screen (0,0) → graph (-x/k, -y/k)).
            const viewport = { x: -x / k, y: -y / k, w: rect.width / k, h: rect.height / k };
            return { nodes, viewport };
        },
        panTo(graphX: number, graphY: number): void {
            const { k } = area.area.transform;
            const rect = container.getBoundingClientRect();
            void area.area.translate(rect.width / 2 - graphX * k, rect.height / 2 - graphY * k);
        },
        getSwimlaneLayer(): HTMLElement {
            return swimlaneLayer;
        },
        getGroupBounds(by: 'thread' | 'chapter') {
            const groups = new Map<string,
                { title: string; minX: number; minY: number; maxX: number; maxY: number }>();
            for (const node of editor.getNodes()) {
                if (node.dto.kind !== 'Event') { continue; }
                let key: string;
                let title: string;
                if (by === 'thread') {
                    if (!node.dto.threadUri) { continue; }
                    key = node.dto.threadUri;
                    title = baseName(node.dto.threadUri);
                } else {
                    if (typeof node.dto.storyChapter !== 'number') { continue; }
                    key = String(node.dto.storyChapter);
                    title = `Chapter ${node.dto.storyChapter}`;
                }
                const view = area.nodeViews.get(node.id);
                if (!view) { continue; }
                const x0 = view.position.x, y0 = view.position.y;
                const x1 = x0 + node.width, y1 = y0 + node.height;
                const g = groups.get(key);
                if (g) {
                    g.minX = Math.min(g.minX, x0); g.minY = Math.min(g.minY, y0);
                    g.maxX = Math.max(g.maxX, x1); g.maxY = Math.max(g.maxY, y1);
                } else {
                    groups.set(key, { title, minX: x0, minY: y0, maxX: x1, maxY: y1 });
                }
            }
            const pad = 24;
            return [...groups.entries()].map(([key, g]) => ({
                key, title: g.title,
                x: g.minX - pad, y: g.minY - pad,
                w: g.maxX - g.minX + pad * 2, h: g.maxY - g.minY + pad * 2,
            }));
        },
        createStagingJunction(position: { x: number; y: number }, kind: 'and' | 'or'): void {
            stagingCounter += 1;
            const dto: StoryGraphNodeDto = {
                id: `local:${kind}:${stagingCounter}`,
                kind: kind === 'and' ? 'StagingAnd' : 'StagingOr',
                label: kind === 'and' ? 'AND' : 'OR',
                threadUri: null, line: null, eventType: null, rewardType: null, branch: null,
                lifecycle: null, reachable: true,
            };
            const node = new StoryNode(dto, true, true);
            void editor.addNode(node).then(() => area.translate(node.id, position));
        },
        discardStagingJunction(nodeId: string): void {
            void removeNodeWithConnections(nodeId);
        },
        refreshMode(): void {
            for (const node of editor.getNodes()) {
                if (node.dto.kind !== 'Event') { continue; }
                void remeasure(node); // recomputes height (readOnly changes row count)
            }
        },
        refreshNode(nodeId: string): void {
            const node = editor.getNode(nodeId);
            if (!node) { return; }
            void remeasure(node);
        },
        patchEventNode(nodeId: string, update: (dto: StoryGraphNodeDto) => StoryGraphNodeDto): void {
            const node = editor.getNode(nodeId);
            if (!node || node.dto.kind !== 'Event') { return; }
            node.applyDto(update(node.dto));
            void remeasure(node);
        },
        repaintNodes(nodeIds: Iterable<string>): void {
            for (const id of nodeIds) {
                if (editor.getNode(id)) { void area.update('node', id); }
            }
        },
        destroy(): void {
            area.destroy();
        },
    };
}

// ── Node / connection / socket components ────────────────────────────────────────────────────────

/** Native-tooltip text for virtual nodes - nothing on them is editable, so a hover suffices. */
const VIRTUAL_DESCRIPTIONS: Record<string, string> = {
    AndJunction: 'AND junction - every input on this prereq line must fire.',
    OrJunction: 'OR junction - any one prereq line arms the event.',
    Portal: 'Portal - stands in for a cross-file target event.',
    TacticalPlot: 'Tactical plot manifest attached to this campaign.',
    StagingAnd: 'Not yet attached - wire event outputs into this, then drag its output onto the '
        + "event that should require all of them together. Nothing is saved until then.",
    StagingOr: 'Not yet attached - wire event outputs into this, then drag its output onto the '
        + "event that any one of them should arm. Nothing is saved until then.",
};

/** AND/OR/Portal/TacticalPlot - nothing on them is editable, so they keep the old compact shape. */
const NodeBox = styled.div<{ selected?: boolean; $w: number; $h: number }>`
    position: relative;
    width: ${p => p.$w}px;
    height: ${p => p.$h}px;
    background: var(--vscode-editorWidget-background, var(--vscode-editor-background));
    color: var(--vscode-editor-foreground);
    border: 2px solid var(--vscode-disabledForeground, #888);
    border-radius: 6px;
    box-sizing: border-box;
    display: flex;
    flex-direction: column;
    justify-content: center;
    padding: 2px 10px;
    cursor: pointer;
    ${p => p.selected ? 'outline: 2px solid var(--vscode-focusBorder); outline-offset: 2px;' : ''}

    .title {
        font-size: 12px;
        font-family: var(--vscode-font-family);
        text-align: center;
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
    }

    &.unreachable { opacity: 0.4; }

    &.k-AndJunction, &.k-OrJunction, &.k-StagingAnd, &.k-StagingOr {
        padding: 0;
        align-items: center;
        justify-content: center;
    }
    &.k-AndJunction, &.k-StagingAnd { border-radius: 50%; }
    /* The diamond is an inner rotated square, NOT a transform on the node box itself: rete reads
       socket positions from offsetLeft/offsetTop, which ignore CSS transforms, so a rotated box
       leaves the sockets visually at the diamond's upper-left/lower-right edges while the edges
       anchor elsewhere. The box stays unrotated (sockets sit at its true left/right middle = the
       diamond's corners); only the decorative inner square rotates. */
    &.k-OrJunction, &.k-StagingOr {
        border: none;
        background: transparent;
    }
    .diamond {
        position: absolute;
        inset: 15%;
        transform: rotate(45deg);
        border: 2px solid var(--vscode-disabledForeground, #888);
        border-radius: 4px;
        background: var(--vscode-editorWidget-background, var(--vscode-editor-background));
    }
    &.k-OrJunction .title, &.k-StagingOr .title { position: relative; z-index: 1; }
    &.k-Portal, &.k-TacticalPlot {
        border-style: dashed;
        border-radius: 12px;
    }
    /* Dashed = "not yet attached", same visual language as Portal/TacticalPlot's "not fully
       resolved" - nothing about a staging junction is saved until its output reaches an event. */
    &.k-StagingAnd { border-style: dashed; }
    &.k-StagingOr .diamond { border-style: dashed; }
    .discard {
        position: absolute;
        top: -8px;
        right: -8px;
        width: 14px;
        height: 14px;
        line-height: 12px;
        text-align: center;
        border-radius: 50%;
        background: var(--vscode-editorWidget-background, var(--vscode-editor-background));
        border: 1px solid var(--vscode-disabledForeground, #888);
        color: var(--vscode-descriptionForeground);
        cursor: pointer;
        font-size: 10px;
        padding: 0;
    }
    .discard:hover { color: var(--vscode-errorForeground, #f44); }
    .jump {
        position: absolute;
        bottom: -8px;
        right: -8px;
        width: 14px;
        height: 14px;
        line-height: 12px;
        text-align: center;
        border-radius: 50%;
        background: var(--vscode-editorWidget-background, var(--vscode-editor-background));
        border: 1px solid var(--vscode-disabledForeground, #888);
        color: var(--vscode-descriptionForeground);
        cursor: pointer;
        font-size: 10px;
        padding: 0;
    }
    .jump:hover { color: var(--vscode-focusBorder); }

    /* top uses calc(50% - 7px), not transform: translateY(-50%) - rete positions connection
       endpoints from offsetTop/offsetLeft (rete-render-utils' getElementCenter), which does not
       reflect CSS transforms, so a translateY-centered socket draws edges anchored below it. */
    .input-socket  { position: absolute; left: -7px;  top: calc(50% - 7px); }
    .output-socket { position: absolute; right: -7px; top: calc(50% - 7px); }
`;

const { RefSocket } = Presets.classic;

function VirtualNodeView(props: { data: StoryNode; emit: RenderEmit<Schemes> }): JSX.Element {
    const dto = props.data.dto;
    const input = props.data.inputs['in'];
    const output = props.data.outputs['out'];
    const classes = [
        'k-' + dto.kind,
        dto.reachable ? '' : 'unreachable',
    ].filter(c => c).join(' ');
    const title = dto.kind === 'AndJunction' ? 'AND'
        : dto.kind === 'OrJunction' || dto.kind === 'StagingOr' ? 'OR'
        : dto.label;
    // Glow only the solid AND junctions; the OR box is transparent (the diamond carries its
    // shape), so a background tint there would show as an odd square - its edges glow instead.
    const glow = dto.kind === 'AndJunction'
        ? branchGlowStyle(props.data.branchGlow, true) : undefined;

    return (
        <NodeBox
            className={classes}
            selected={props.data.selected}
            $w={props.data.width}
            $h={props.data.height}
            style={glow}
            title={VIRTUAL_DESCRIPTIONS[dto.kind] ?? dto.kind}
            data-testid="node"
            data-node-id={dto.id}
        >
            {dto.kind === 'OrJunction' || dto.kind === 'StagingOr' ? <div className="diamond" /> : null}
            <div className="title">{title}</div>
            {(dto.kind === 'StagingAnd' || dto.kind === 'StagingOr') && currentMode === 'edit' ? (
                <Drag.NoDrag>
                    <button
                        className="discard" title="Discard - nothing was saved"
                        onClick={() => editorHandleRef?.discardStagingJunction(dto.id)}
                    >×</button>
                </Drag.NoDrag>
            ) : null}
            {dto.kind === 'TacticalPlot' && output ? (
                <Drag.NoDrag>
                    <button
                        className="jump" title="Jump to this battle's own story"
                        onClick={() => onReachableFromRequested(dto.id)}
                    >→</button>
                </Drag.NoDrag>
            ) : null}
            {input ? (
                <RefSocket
                    name="input-socket" side="input" socketKey="in"
                    nodeId={props.data.id} emit={props.emit} payload={input.socket}
                />
            ) : null}
            {output ? (
                <RefSocket
                    name="output-socket" side="output" socketKey="out"
                    nodeId={props.data.id} emit={props.emit} payload={output.socket}
                />
            ) : null}
        </NodeBox>
    );
}

function StoryNodeView(props: { data: StoryNode; emit: RenderEmit<Schemes> }): JSX.Element {
    return props.data.dto.kind === 'Event'
        ? <EventNodeView data={props.data} emit={props.emit} />
        : <VirtualNodeView data={props.data} emit={props.emit} />;
}

/**
 * Blueprint-style node body: every trigger/reward field lives here now, editable in place - no
 * more side panel. Every interactive element is wrapped in `Drag.NoDrag` (rete-react-plugin's own
 * mechanism, used internally by its context-menu search input): rete's `NodeView` attaches a plain
 * pointerdown listener to the whole node element with no target check, so without this an input
 * click/drag-to-select-text would be swallowed as a "move the node" gesture instead.
 */
const EventBody = styled.div<{ selected?: boolean; $w: number; $h: number }>`
    position: relative;
    width: ${p => p.$w}px;
    height: ${p => p.$h}px;
    background: var(--vscode-editorWidget-background, var(--vscode-editor-background));
    color: var(--vscode-editor-foreground);
    border: 2px solid var(--vscode-disabledForeground, #888);
    border-radius: 6px;
    box-sizing: border-box;
    display: flex;
    flex-direction: column;
    padding: 4px 8px 6px;
    font-size: 11px;
    font-family: var(--vscode-font-family);
    cursor: default;
    ${p => p.selected ? 'outline: 2px solid var(--vscode-focusBorder); outline-offset: 2px;' : ''}

    &.lc-Waiting  { border-color: var(--vscode-charts-blue,   #3794ff); }
    &.lc-Armed    { border-color: var(--vscode-charts-green,  #89d185); }
    &.lc-Fired    { border-color: var(--vscode-charts-purple, #b180d7); }
    &.lc-Disabled { border-color: var(--vscode-charts-red,    #f14c4c); }
    &.unreachable { opacity: 0.5; }
    &.untested    { border-style: dashed; }

    .header {
        display: flex;
        align-items: center;
        gap: 3px;
        height: ${EVENT_HEADER_H}px;
        flex-shrink: 0;
        border-bottom: 1px solid var(--vscode-panel-border);
        margin-bottom: 2px;
    }
    /* Same Drag.NoDrag wrapper-span problem as .row > span: the wrappers are plain inline spans,
       so a long title never shrinks and pushes the icon buttons out of the node. The first span
       wraps the title (flexes and shrinks); the rest wrap icon buttons (keep natural size). */
    .header > span {
        flex-shrink: 0;
        display: flex;
        min-width: 0;
    }
    .header > span:first-of-type { flex: 1; }
    .header .title, .header input.title-edit {
        flex: 1;
        min-width: 0;
        font-weight: bold;
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
    }
    /* The title is the node's drag handle - grab it to move the node (rename is the ✎ button). */
    .header .title { cursor: move; }
    .header input.title-edit {
        background: var(--vscode-input-background);
        color: var(--vscode-input-foreground);
        border: 1px solid var(--vscode-focusBorder);
        font-size: 11px;
        font-family: inherit;
        padding: 0 2px;
    }
    .header button {
        flex-shrink: 0;
        background: transparent;
        border: none;
        color: var(--vscode-descriptionForeground);
        cursor: pointer;
        padding: 0 2px;
        font-size: 11px;
        line-height: 1.6;
    }
    .header button:hover { color: var(--vscode-editor-foreground); }
    .header button.danger:hover { color: var(--vscode-errorForeground, #f44); }

    .row {
        display: flex;
        align-items: center;
        gap: 4px;
        height: ${EVENT_ROW_H}px;
        flex-shrink: 0;
    }
    .row label {
        width: 72px;
        flex-shrink: 0;
        color: var(--vscode-descriptionForeground);
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
    }
    .row.section-head {
        border-top: 1px solid var(--vscode-panel-border);
        margin-top: 2px;
    }
    .section-toggle {
        flex: 1;
        min-width: 0;
        align-self: center;
        cursor: pointer;
        font-weight: bold;
        font-size: 10px;
        letter-spacing: 0.5px;
        text-transform: uppercase;
        color: var(--vscode-descriptionForeground);
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
    }
    .section-toggle:hover { color: var(--vscode-editor-foreground); }
    /* Drag.NoDrag's own wrapper is an unstyleable <span> - flex it via the child combinator so the
       control it wraps still fills the row like every other field. */
    .row > span {
        flex: 1;
        min-width: 0;
        display: flex;
    }
    .row select, .row input[type=text] {
        flex: 1;
        min-width: 0;
        font-size: 11px;
        font-family: inherit;
        background: var(--vscode-input-background);
        color: var(--vscode-input-foreground);
        border: 1px solid var(--vscode-input-border, transparent);
        padding: 0 3px;
    }
    .row input[type=checkbox] { margin: 0; }
    .row input.missing, .row select.missing { border-color: var(--vscode-errorForeground, #f44); }
    .row input.diag-error, .row select.diag-error {
        border-color: var(--vscode-errorForeground, #f44);
        outline: 1px solid var(--vscode-errorForeground, #f44);
    }
    .row input.diag-warning, .row select.diag-warning {
        border-color: var(--vscode-charts-yellow, #cca700);
        outline: 1px solid var(--vscode-charts-yellow, #cca700);
    }
    .row input:disabled, .row select:disabled { opacity: 0.7; }
    /* NoDrag wrappers around a row's buttons must not flex like the input wrappers. */
    .row > span:has(> button) { flex: 0 0 auto; }

    .row > span > .type-chip, .row > .type-empty, .row > span > .type-empty { flex: 1; min-width: 0; }
    .row button.chip-remove:hover { color: var(--vscode-errorForeground, #f44); }
    .header .diag-badge { flex-shrink: 0; cursor: help; }
    /* Boolean rows lead with the checkbox; the label text takes the rest of the row. */
    .row > span:has(> input[type=checkbox]) { flex: 0 0 auto; }
    .row .bool-label {
        flex: 1;
        min-width: 0;
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
    }
    .row button.goto {
        flex-shrink: 0;
        background: transparent;
        border: none;
        color: var(--vscode-descriptionForeground);
        cursor: pointer;
        padding: 0 2px;
        font-size: 11px;
    }
    .row button.goto:hover { color: var(--vscode-focusBorder); }

    /* same socket-offset rule as NodeBox - see the comment there for why calc(), not transform. */
    .input-socket  { position: absolute; left: -7px;  top: calc(50% - 7px); }
    .output-socket { position: absolute; right: -7px; top: calc(50% - 7px); }
`;

/**
 * A text input that commits on blur, matching the rest of this file's edit UX (`ParamRows` before
 * it). Resyncs from `value` on prop change UNLESS the field is currently focused - Event nodes are
 * now always mounted (no more open-a-node/close-a-node lifecycle to reset stale local state), so an
 * unconditional reset-on-remount isn't available, but an unconditional resync-on-every-prop-change
 * would clobber in-progress typing every time an unrelated graph refresh lands.
 */
function BlurCommitInput(props: {
    value: string; disabled: boolean; onCommit: (v: string) => void;
    placeholder?: string; className?: string;
}): JSX.Element {
    const [value, setValue] = useState(props.value);
    const focused = useRef(false);
    useEffect(() => { if (!focused.current) { setValue(props.value); } }, [props.value]);
    return (
        <input
            type="text" className={props.className} value={value} disabled={props.disabled}
            placeholder={props.placeholder}
            onFocus={() => { focused.current = true; }}
            onChange={e => setValue(e.target.value)}
            onBlur={() => {
                focused.current = false;
                if (value !== props.value) { props.onCommit(value); }
            }}
        />
    );
}

/**
 * A reference-typed value input: commits on blur like `BlurCommitInput`, plus a debounced
 * suggestion dropdown fed by the server (aet/getStoryParamOptions via the extension). Picking a
 * suggestion commits immediately - `onMouseDown` + `preventDefault` so the input never blurs
 * mid-pick (a blur would commit the half-typed prefix first). `lastSent` guards the follow-up
 * blur from re-committing the same value while the server round trip is still in flight.
 */
function RefValueInput(props: {
    value: string; disabled: boolean; onCommit: (v: string) => void;
    fetchOptions: (prefix: string) => Promise<ParamOption[]>;
    onInput?: (v: string) => void;
    placeholder?: string; className?: string;
}): JSX.Element {
    const [value, setValue] = useState(props.value);
    const [options, setOptions] = useState<ParamOption[]>([]);
    const [open, setOpen] = useState(false);
    const focused = useRef(false);
    const fetchSeq = useRef(0);
    const lastSent = useRef<string | null>(null);
    const debounce = useRef<number | undefined>(undefined);
    useEffect(() => {
        lastSent.current = null;
        if (!focused.current) { setValue(props.value); }
    }, [props.value]);
    useEffect(() => () => window.clearTimeout(debounce.current), []);

    const query = (prefix: string): void => {
        const seq = ++fetchSeq.current;
        window.clearTimeout(debounce.current);
        debounce.current = window.setTimeout(() => {
            void props.fetchOptions(prefix).then(fetched => {
                // Stale replies (an older prefix) and replies landing after focus left are dropped.
                if (seq !== fetchSeq.current || !focused.current) { return; }
                setOptions(fetched);
                setOpen(fetched.length > 0);
            });
        }, 150);
    };

    const commit = (v: string): void => {
        if (v !== props.value && v !== lastSent.current) {
            lastSent.current = v;
            props.onCommit(v);
        }
    };

    return (
        <div className="suggest">
            <input
                type="text" className={props.className} value={value} disabled={props.disabled}
                placeholder={props.placeholder}
                onFocus={() => { focused.current = true; query(value); }}
                onChange={e => {
                    setValue(e.target.value);
                    props.onInput?.(e.target.value);
                    query(e.target.value);
                }}
                onBlur={() => {
                    focused.current = false;
                    setOpen(false);
                    commit(value);
                }}
                onKeyDown={e => {
                    if (e.key === 'Escape') { setOpen(false); }
                    if (e.key === 'Enter') { setOpen(false); commit(value); }
                }}
            />
            {open && !props.disabled ? (
                <div className="suggest-list">
                    {options.map(option => (
                        <div
                            key={option.value} className="suggest-item"
                            title={option.detail ?? undefined}
                            onMouseDown={e => {
                                e.preventDefault(); // keep the input focused - no blur-commit race
                                setValue(option.value);
                                props.onInput?.(option.value);
                                setOpen(false);
                                commit(option.value);
                            }}
                        >{option.value}</div>
                    ))}
                </div>
            ) : null}
        </div>
    );
}

/**
 * Event/reward param rows for one node: exactly the schema-declared fields of the current type
 * (optional ones as "(optional)" slots, mandatory unset ones red-outlined), plus any off-schema
 * value present in the XML as a raw "Param N" row. Row count must match
 * `estimateEventNodeHeight`'s (`paramRowSpecs` is the shared source of truth for both).
 * Booleans render as checkboxes, enums as dropdowns from the schema's inline values,
 * reference-typed rows get server-backed suggestions and a ↗ go-to button. Rows carrying a
 * server diagnostic get a severity outline and the message in their tooltip.
 */
function EventParamRows(props: {
    kind: 'event' | 'reward';
    nodeId: string;
    typeName: string | null;
    threadUri: string | null | undefined;
    eventName: string;
    params: { position: number; value: string }[] | null | undefined;
    schema: StoryParamSchemaDto[];
    readOnly: boolean;
}): JSX.Element {
    const rows = paramRowSpecs(props.params, props.schema);
    const label = props.kind === 'event' ? 'Param' : 'Reward';
    const schemaByPosition = new Map(props.schema.map(s => [s.position, s]));
    const diagnostics = nodeDiagnostics.get(props.nodeId) ?? [];
    // "Planet", "Attacker faction", … from the schema description; "Param N" when it has none.
    const rowLabel = (position: number): string =>
        shortParamLabel(schemaByPosition.get(position)) ?? `${label} ${position + 1}`;

    const commit = (position: number, value: string): void => {
        sendCommand({
            kind: 'setParams', threadUri: props.threadUri, eventName: props.eventName,
            paramKind: props.kind, params: [{ position, value: value || null }],
        });
    };

    const typeName = props.typeName;

    return (
        <>
            {rows.map(row => {
                const schemaParam = schemaByPosition.get(row.position);
                const enumValues = schemaParam?.enumValues ?? null;
                const referenceType = schemaParam?.referenceType ?? null;
                const isBoolean = schemaParam?.valueType === 'Boolean';
                const optionalUnset = !!schemaParam?.optional && !row.value;
                const diagnostic = diagnostics.find(d =>
                    d.side === props.kind && d.position === row.position);
                const controlClass = [
                    row.missing ? 'missing' : '',
                    diagnostic ? `diag-${diagnostic.severity === 'error' ? 'error' : 'warning'}` : '',
                ].filter(c => c).join(' ');
                const title = `${label} ${row.position + 1}`
                    + (schemaParam?.description ? ` - ${schemaParam.description}` : '')
                    + (row.missing ? ' (required)' : optionalUnset ? ' (optional)' : '')
                    + (diagnostic ? `\n⚠ ${diagnostic.message}` : '');
                // List params hold several tokens; go-to targets the first one.
                const firstToken = row.value.split(/[\s,]+/).filter(t => t)[0] ?? '';
                if (isBoolean) {
                    // Checkbox-first layout with the FULL cleaned description as its label - the
                    // checkbox already encodes the 0/1 mechanics, so the "1 = …" prefix goes and
                    // the label is no longer squeezed into the 72px label column.
                    return (
                        <div className="row" key={row.position}>
                            <Drag.NoDrag>
                                <input
                                    type="checkbox" className={controlClass} title={title}
                                    checked={row.value === '1' || /^(yes|true)$/i.test(row.value)}
                                    disabled={props.readOnly}
                                    onChange={e => commit(row.position, e.target.checked ? '1' : '0')}
                                />
                            </Drag.NoDrag>
                            <span className="bool-label" title={title}>
                                {booleanParamLabel(schemaParam?.description) ?? rowLabel(row.position)}
                            </span>
                        </div>
                    );
                }
                return (
                    <div className="row" key={row.position}>
                        <label title={title}>
                            {rowLabel(row.position)}
                        </label>
                        <Drag.NoDrag>
                            {enumValues?.length ? (
                                <select
                                    className={controlClass} title={title}
                                    value={row.value} disabled={props.readOnly}
                                    onChange={e => commit(row.position, e.target.value)}
                                >
                                    <option value="">{row.missing ? '(required)' : '(unset)'}</option>
                                    {[...new Set([row.value, ...enumValues])].filter(v => v)
                                        .map(v => <option key={v} value={v}>{v}</option>)}
                                </select>
                            ) : referenceType && typeName ? (
                                <RefValueInput
                                    className={controlClass}
                                    value={row.value} disabled={props.readOnly}
                                    placeholder={row.missing ? 'required' : optionalUnset ? '(optional)' : undefined}
                                    onCommit={v => commit(row.position, v)}
                                    fetchOptions={prefix =>
                                        fetchParamOptions(props.kind, typeName, row.position, prefix)}
                                />
                            ) : (
                                <BlurCommitInput
                                    className={controlClass}
                                    value={row.value} disabled={props.readOnly}
                                    placeholder={row.missing ? 'required' : optionalUnset ? '(optional)' : undefined}
                                    onCommit={v => commit(row.position, v)}
                                />
                            )}
                        </Drag.NoDrag>
                        {referenceType && firstToken ? (
                            <Drag.NoDrag>
                                <button
                                    className="goto"
                                    title={`Go to the definition of '${firstToken}' (${referenceType})`}
                                    onClick={() => vscode.postMessage({
                                        type: 'resolveRef', value: firstToken, referenceType,
                                    })}
                                >↗</button>
                            </Drag.NoDrag>
                        ) : null}
                    </div>
                );
            })}
        </>
    );
}

/**
 * Collapsible heading separating an Event node's Trigger and Reward sections. Collapse state is
 * per node id, module scope (see `collapsedSections`); a collapsed section shows its type name as
 * an inline summary so the node stays readable at a glance.
 */
function SectionHead(props: {
    nodeId: string; section: NodeSection; summary: string | null;
}): JSX.Element {
    const collapsed = isSectionCollapsed(props.nodeId, props.section);
    const label = props.section === 'general' ? 'General'
        : props.section === 'trigger' ? 'Trigger' : 'Reward';
    return (
        <div className="row section-head">
            <Drag.NoDrag>
                <span
                    className="section-toggle"
                    title={collapsed ? `Expand the ${label.toLowerCase()} section` : `Collapse the ${label.toLowerCase()} section`}
                    onClick={() => toggleSection(props.nodeId, props.section)}
                >
                    {collapsed ? '▸' : '▾'} {label}
                    {collapsed && props.summary ? ` - ${props.summary}` : ''}
                </span>
            </Drag.NoDrag>
        </div>
    );
}

/**
 * The Type row of a Trigger/Reward section. Types are immutable: the chip shows the attached
 * type, its ✕ clears the type AND its params atomically (server `clearEventType`/`clearRewardType`);
 * an empty slot is filled by dropping a type from the palette onto the node - there is no dropdown,
 * so stale params can never survive a type change.
 */
function TypeRow(props: {
    kind: 'trigger' | 'reward';
    typeName: string | null;
    threadUri: string | null | undefined;
    eventName: string;
    readOnly: boolean;
}): JSX.Element {
    const clearKind = props.kind === 'trigger' ? 'clearEventType' : 'clearRewardType';
    return (
        <div className="row">
            <label>Type</label>
            {props.typeName ? (
                <>
                    <Drag.NoDrag>
                        <span
                            className="type-chip"
                            title={`${props.typeName} - remove it (✕) to attach a different type`}
                        >{props.typeName}</span>
                    </Drag.NoDrag>
                    {props.readOnly ? null : (
                        <Drag.NoDrag>
                            <button
                                className="goto chip-remove"
                                title={`Remove this ${props.kind} and its parameters`}
                                onClick={() => sendCommand(
                                    { kind: clearKind, threadUri: props.threadUri, eventName: props.eventName },
                                    `Remove ${props.kind} '${props.typeName}' and its parameters from '${props.eventName}'?`)}
                            >✕</button>
                        </Drag.NoDrag>
                    )}
                </>
            ) : (
                <span className="type-empty">
                    {props.readOnly ? '(none)' : `drop a ${props.kind === 'trigger' ? 'trigger' : 'reward'} type here`}
                </span>
            )}
        </div>
    );
}

function EventNodeView(props: { data: StoryNode; emit: RenderEmit<Schemes> }): JSX.Element {
    const dto = props.data.dto;
    const input = props.data.inputs['in'];
    const output = props.data.outputs['out'];
    const readOnly = currentMode !== 'edit';
    const untested = untestedTypes.has(dto.eventType ?? '') || untestedTypes.has(dto.rewardType ?? '');
    const classes = [
        'lc-' + (dto.lifecycle ?? 'Inactive'),
        dto.reachable ? '' : 'unreachable',
        untested ? 'untested' : '',
    ].filter(c => c).join(' ');

    // Editing is SEEDED from the module-scoped draft so a re-mount (graph refresh rebuilding this
    // node's view) restores the open rename box. The input itself is UNCONTROLLED and reads/writes
    // the durable `renameDrafts` - a controlled `value` can be silently reverted by a re-render,
    // and the typed text must survive a re-mount too. `inputRef` also drives explicit focus.
    const [editingTitle, setEditingTitle] = useState(() => renameDrafts.has(dto.id));
    const inputRef = useRef<HTMLInputElement>(null);

    const openRename = (): void => {
        renameDrafts.set(dto.id, dto.label);
        setEditingTitle(true);
    };
    const cancelRename = (): void => {
        renameDrafts.delete(dto.id);
        setEditingTitle(false);
    };
    const commitTitle = (): void => {
        // Read the live DOM value (the source of truth for the uncontrolled input), falling back
        // to the durable draft - never to a possibly-stale React state.
        const next = (inputRef.current?.value ?? renameDrafts.get(dto.id) ?? dto.label).trim();
        renameDrafts.delete(dto.id);
        setEditingTitle(false);
        if (next && next !== dto.label) {
            // Renaming re-keys the node id (it's derived from the name), so carry its current spot
            // to the new name - otherwise the renamed node re-materialises beside a neighbour.
            editorHandleRef?.carryPosition(dto.id, dto.threadUri, next);
            sendCommand({ kind: 'renameEvent', eventName: dto.label, newName: next });
        }
    };

    // Focus the input explicitly when the box opens: autoFocus is unreliable here because
    // rete-react-plugin's Drag.NoDrag intercepts pointerdown and re-dispatches a synthetic copy,
    // which does NOT carry the browser's default focus action - so a click never focused the field
    // and keystrokes went nowhere (the rename silently reset to the old name on commit).
    useEffect(() => {
        if (editingTitle && !readOnly) {
            const el = inputRef.current;
            if (el) { el.focus(); el.select(); }
        }
    }, [editingTitle, readOnly]);

    // A just-created event opens its rename box on first mount (see `pendingAutoRename`), so the
    // drop-then-name gesture is continuous. Runs once; the key is consumed so a later re-mount
    // doesn't reopen it.
    useEffect(() => {
        const key = autoRenameKey(dto.threadUri, dto.label);
        if (pendingAutoRename.has(key) && currentMode === 'edit') {
            pendingAutoRename.delete(key);
            openRename();
        }
    }, []);

    const eventSchema = eventTypeParams.get(dto.eventType ?? '') ?? [];
    const rewardSchema = rewardTypeParams.get(dto.rewardType ?? '') ?? [];

    return (
        <EventBody
            className={classes}
            selected={props.data.selected}
            $w={props.data.width}
            $h={props.data.height}
            style={branchGlowStyle(props.data.branchGlow, false)}
            data-testid="node"
            data-node-id={dto.id}
            data-branch={props.data.branchGlow ?? undefined}
        >
            <div className="header">
                {editingTitle && !readOnly ? (
                    <>
                        <Drag.NoDrag>
                            <input
                                ref={inputRef}
                                className="title-edit" type="text"
                                defaultValue={renameDrafts.get(dto.id) ?? dto.label}
                                onChange={e => renameDrafts.set(dto.id, e.target.value)}
                                onKeyDown={e => {
                                    if (e.key === 'Enter') { commitTitle(); }
                                    if (e.key === 'Escape') { cancelRename(); }
                                }}
                            />
                        </Drag.NoDrag>
                        {/* Explicit commit/cancel - an unambiguous way to apply the rename besides
                            Enter. onMouseDown+preventDefault keeps the input focused so the click
                            doesn't blur-then-fight the button; there's no onBlur commit (a blur that
                            landed anywhere would otherwise fire an unintended rename). */}
                        <Drag.NoDrag>
                            <button
                                className="rename-ok" title="Apply rename (Enter)"
                                onMouseDown={e => { e.preventDefault(); commitTitle(); }}
                            >✓</button>
                        </Drag.NoDrag>
                        <Drag.NoDrag>
                            <button
                                title="Cancel (Esc)"
                                onMouseDown={e => { e.preventDefault(); cancelRename(); }}
                            >✗</button>
                        </Drag.NoDrag>
                    </>
                ) : (
                    // NOT NoDrag-wrapped: the title is the node's drag handle (grab it to move the
                    // node). Renaming is the explicit ✎ button below, so a drag never lands in the
                    // rename box by accident. (A plain onClick here wouldn't fire anyway - rete's
                    // simpleNodesOrder reparents the node on pointerdown and the browser drops the
                    // click; that's why rename is a dedicated NoDrag button.)
                    <span className="title" title={`${dto.label} - drag to move`}>{dto.label}</span>
                )}
                {readOnly || editingTitle ? null : (
                    <Drag.NoDrag>
                        <button title="Rename this event" onClick={openRename}>✎</button>
                    </Drag.NoDrag>
                )}
                {(nodeDiagnostics.get(dto.id)?.length ?? 0) > 0 ? (
                    <span
                        className={'diag-badge ' + (nodeDiagnostics.get(dto.id)!.some(d => d.severity === 'error')
                            ? 'diag-error' : 'diag-warning')}
                        title={nodeDiagnostics.get(dto.id)!.map(d => d.message).join('\n')}
                    >⚠{nodeDiagnostics.get(dto.id)!.length}</span>
                ) : null}
                <Drag.NoDrag>
                    <button
                        title="Open in XML"
                        onClick={() => vscode.postMessage({ type: 'openXml', threadUri: dto.threadUri, line: dto.line ?? 0 })}
                    >↗</button>
                </Drag.NoDrag>
                <Drag.NoDrag>
                    <button
                        title="Show only what's reachable from here"
                        onClick={() => onReachableFromRequested(dto.id)}
                    >⭑</button>
                </Drag.NoDrag>
                {readOnly ? null : (
                    <Drag.NoDrag>
                        <button
                            className="danger" title="Delete this event"
                            onClick={() => sendCommand(
                                { kind: 'deleteEvent', threadUri: dto.threadUri, eventName: dto.label },
                                `Delete story event '${dto.label}'?`)}
                        >🗑</button>
                    </Drag.NoDrag>
                )}
            </div>

            <SectionHead nodeId={dto.id} section="general" summary={dto.branch ?? null} />
            {isSectionCollapsed(dto.id, 'general') ? null : (
                <>
                    <div className="row">
                        <label>Branch</label>
                        <Drag.NoDrag>
                            <BlurCommitInput
                                value={dto.branch ?? ''} disabled={readOnly}
                                onCommit={v => sendCommand({
                                    kind: 'setBranch', threadUri: dto.threadUri, eventName: dto.label, value: v || null,
                                })}
                            />
                        </Drag.NoDrag>
                    </div>
                    <div className="row">
                        <label>Perpetual</label>
                        <Drag.NoDrag>
                            <input
                                type="checkbox" checked={dto.perpetual ?? false} disabled={readOnly}
                                onChange={e => sendCommand({
                                    kind: 'setPerpetual', threadUri: dto.threadUri, eventName: dto.label,
                                    flag: e.target.checked,
                                })}
                            />
                        </Drag.NoDrag>
                    </div>
                    <div className="row">
                        <label>Dialog</label>
                        <Drag.NoDrag>
                            <BlurCommitInput
                                value={dto.storyDialog ?? ''} disabled={readOnly}
                                onCommit={v => sendCommand({
                                    kind: 'setDialog', threadUri: dto.threadUri, eventName: dto.label, value: v || null,
                                })}
                            />
                        </Drag.NoDrag>
                    </div>
                </>
            )}

            <SectionHead nodeId={dto.id} section="trigger" summary={dto.eventType ?? null} />
            {isSectionCollapsed(dto.id, 'trigger') ? null : (
                <>
                    <TypeRow
                        kind="trigger" typeName={dto.eventType ?? null}
                        threadUri={dto.threadUri} eventName={dto.label} readOnly={readOnly}
                    />
                    <EventParamRows
                        kind="event" nodeId={dto.id} typeName={dto.eventType ?? null}
                        threadUri={dto.threadUri} eventName={dto.label}
                        params={dto.eventParams} schema={eventSchema} readOnly={readOnly}
                    />
                </>
            )}

            <SectionHead nodeId={dto.id} section="reward" summary={dto.rewardType ?? null} />
            {isSectionCollapsed(dto.id, 'reward') ? null : (
                <>
                    <TypeRow
                        kind="reward" typeName={dto.rewardType ?? null}
                        threadUri={dto.threadUri} eventName={dto.label} readOnly={readOnly}
                    />
                    <EventParamRows
                        kind="reward" nodeId={dto.id} typeName={dto.rewardType ?? null}
                        threadUri={dto.threadUri} eventName={dto.label}
                        params={dto.rewardParams} schema={rewardSchema} readOnly={readOnly}
                    />
                </>
            )}

            {input ? (
                <RefSocket
                    name="input-socket" side="input" socketKey="in"
                    nodeId={props.data.id} emit={props.emit} payload={input.socket}
                />
            ) : null}
            {output ? (
                <RefSocket
                    name="output-socket" side="output" socketKey="out"
                    nodeId={props.data.id} emit={props.emit} payload={output.socket}
                />
            ) : null}
        </EventBody>
    );
}

const ConnSvg = styled.svg`
    overflow: visible !important;
    position: absolute;
    pointer-events: none;
    width: 9999px;
    height: 9999px;

    path {
        fill: none;
        stroke-width: 2px;
        stroke: var(--vscode-charts-foreground, #999);
        marker-end: url(#story-arrow);
    }
    &.k-Control path  { stroke: var(--vscode-charts-orange, #d18616); }
    &.k-Tactical path,
    &.k-TacticalEntry path { stroke: var(--vscode-charts-yellow, #cca700); stroke-dasharray: 8 4; }
    &.k-Flag path     { stroke: var(--vscode-charts-blue, #3794ff);   stroke-dasharray: 2 4; }
    /* Sankey glow underlay: a fat translucent stroke UNDER the crisp edge. A plain wide path is
       far cheaper than an SVG filter (drop-shadow was the main pan/zoom perf sink on big
       campaigns) and still reads as a coloured halo. No arrowhead on the underlay. */
    path.glow-underlay {
        stroke-width: 7px;
        marker-end: none;
        stroke-linecap: round;
    }
`;

function StoryConnectionView(props: { data: StoryConnection }): JSX.Element | null {
    const { path } = Presets.classic.useConnection();
    if (!path) { return null; }
    // Sankey-style branch glow: prereq edges feeding a branch carry its hue, so a branch's flow
    // reads as one coloured strand even where it crosses other paths.
    // `?? null` matters: the connection plugin's transient drag pseudo-connection is a plain
    // ClassicPreset.Connection with no `branch` field (undefined) - coalesce it so the guard below
    // doesn't call branchColor(undefined).
    const branch = props.data.branch ?? null;
    const c = branch !== null ? branchColor(branch) : null;
    return (
        <ConnSvg className={'k-' + (props.data.kind ?? '')} data-testid="connection">
            {c !== null ? (
                <path className="glow-underlay" d={path}
                    style={{ stroke: `color-mix(in srgb, ${c} 40%, transparent)` }} />
            ) : null}
            <path
                d={path}
                style={c !== null ? { stroke: c, strokeWidth: 2.5 } : undefined}
            />
        </ConnSvg>
    );
}

const SocketDot = styled.div`
    width: 14px;
    height: 14px;
    border-radius: 50%;
    background: var(--vscode-charts-foreground, #999);
    opacity: 0.55;
    cursor: crosshair;

    &:hover { opacity: 1; }
`;

function StorySocketView(): JSX.Element {
    return <SocketDot data-testid="socket" />;
}

// ── App chrome ───────────────────────────────────────────────────────────────────────────────────

const GlobalStyle = createGlobalStyle`
    * { box-sizing: border-box; margin: 0; padding: 0; }
    html, body, #root {
        height: 100%;
        overflow: hidden;
        font-family: var(--vscode-font-family);
        font-size: var(--vscode-font-size);
        color: var(--vscode-editor-foreground);
        background: var(--vscode-editor-background);
        /* This is a canvas app, not a document: nearly every pointer gesture is a drag (pan, node
           move, socket wiring), and a drag that starts on a label or ends over the dock would
           otherwise leave a text selection behind. A live selection then hijacks subsequent
           drags - the browser extends the selection instead of letting the gesture through, so
           panning appears to stop working. Selection is re-enabled below only where typing or
           copying is the point. */
        user-select: none;
        -webkit-user-select: none;
    }
    /* Text entry needs a caret and selection to be usable at all. */
    input, textarea {
        user-select: text;
        -webkit-user-select: text;
    }
    /* Diagnostic messages and sim log lines are worth copying out, and neither panel has a drag
       gesture of its own, so a selection there can't strand one. */
    .problem-msg, .sim-log-line {
        user-select: text;
        -webkit-user-select: text;
    }

    /* Jump-to-node flash: a bright pulsing ring so a diagnostic's culprit node is unmistakable in
       a large graph. The animated box-shadow transiently overrides a node's branch-glow shadow. */
    @keyframes story-flash {
        0%, 100% { box-shadow: 0 0 0 0 rgba(0, 0, 0, 0); }
        20%, 60% {
            box-shadow: 0 0 0 4px var(--vscode-focusBorder, #3794ff),
                        0 0 18px 6px var(--vscode-focusBorder, #3794ff);
        }
    }
    .story-flash {
        animation: story-flash 0.8s ease-in-out 2;
        border-radius: 6px;
        z-index: 5;
    }

    /* Server-backed suggestion dropdown (RefValueInput) - global because it renders both inside
       Event node bodies and in the toolbar's create forms. */
    .suggest {
        position: relative;
        flex: 1;
        min-width: 0;
        display: flex;
    }
    .suggest input { width: 100%; min-width: 0; }
    .suggest-list {
        position: absolute;
        top: 100%;
        left: 0;
        right: 0;
        max-height: 160px;
        overflow-y: auto;
        z-index: 30;
        background: var(--vscode-editorWidget-background, var(--vscode-editor-background));
        border: 1px solid var(--vscode-focusBorder);
        font-size: 11px;
    }
    .suggest-item {
        padding: 2px 6px;
        cursor: pointer;
        white-space: nowrap;
        overflow: hidden;
        text-overflow: ellipsis;
    }
    .suggest-item:hover { background: var(--vscode-list-hoverBackground, rgba(128, 128, 128, 0.2)); }

    /* Diagnostic severity accents - node header badges and the problems list. */
    .diag-badge {
        font-size: 10px;
        font-weight: bold;
        padding: 0 2px;
    }
    .diag-badge.diag-error { color: var(--vscode-errorForeground, #f44); }
    .diag-badge.diag-warning { color: var(--vscode-charts-yellow, #cca700); }

    /* Immutable-type chips - used in node bodies and the toolbar's create form. */
    .type-chip {
        padding: 1px 6px;
        border: 1px solid var(--vscode-panel-border);
        border-radius: 3px;
        background: var(--vscode-badge-background, rgba(128, 128, 128, 0.2));
        /* Pair the text with the badge background - without this the chip inherited the dark
           editor foreground and read as near-black on the theme's (often blue) badge colour. */
        color: var(--vscode-badge-foreground, var(--vscode-editor-foreground));
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
    }
    .type-empty {
        padding: 1px 6px;
        border: 1px dashed var(--vscode-panel-border);
        border-radius: 3px;
        color: var(--vscode-descriptionForeground);
        font-style: italic;
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
    }
`;

const Shell = styled.div`
    height: 100%;
    display: flex;
    flex-direction: column;

    .toolbar {
        padding: 4px 8px;
        display: flex;
        gap: 6px;
        align-items: center;
        background: var(--vscode-sideBar-background);
        border-bottom: 1px solid var(--vscode-panel-border);
        flex-shrink: 0;
    }
    select, input[type=text] {
        background: var(--vscode-input-background);
        color: var(--vscode-input-foreground);
        border: 1px solid var(--vscode-input-border, transparent);
        padding: 2px 6px;
        font-size: var(--vscode-font-size);
        font-family: var(--vscode-font-family);
        outline: none;
        min-width: 0;
    }
    input[type=text] { flex: 1; }
    input[type=text]:focus, select:focus { border-color: var(--vscode-focusBorder); }
    button {
        background: var(--vscode-button-secondaryBackground, var(--vscode-button-background));
        color: var(--vscode-button-secondaryForeground, var(--vscode-button-foreground));
        border: none;
        padding: 3px 8px;
        cursor: pointer;
        font-size: var(--vscode-font-size);
        font-family: var(--vscode-font-family);
        white-space: nowrap;
        flex-shrink: 0;
    }
    button:hover { background: var(--vscode-button-secondaryHoverBackground, var(--vscode-button-hoverBackground)); }
    button.primary {
        background: var(--vscode-button-background);
        color: var(--vscode-button-foreground);
    }
    button.danger { color: var(--vscode-errorForeground, #f44); }

    .mode-switch {
        display: flex;
        flex-shrink: 0;
        border: 1px solid var(--vscode-panel-border);
        border-radius: 3px;
        overflow: hidden;
    }
    .mode-switch button {
        border-radius: 0;
    }
    .mode-switch button + button {
        border-left: 1px solid var(--vscode-panel-border);
    }

    .body { flex: 1; display: flex; overflow: hidden; min-height: 0; }
    .canvas-area { flex: 1; position: relative; overflow: hidden; }
    .canvas { position: absolute; inset: 0; z-index: 1; }

    /* Zero-size anchor at the graph origin inside rete's transformed content holder: its absolutely
       positioned children are therefore laid out in graph coordinates and inherit pan/zoom.
       z-index:-1 (not DOM order) keeps it beneath the nodes AND the connections - rete's content
       manager re-orders holder children as nodes/connections come and go, and it inserts
       connections ahead of a merely-prepended element, which left the lanes painting over the
       edges. The holder's will-change:transform makes it a stacking context, so the negative
       index stays contained here. */
    .swimlane-layer { position: absolute; top: 0; left: 0; width: 0; height: 0; pointer-events: none; z-index: -1; }
    .swimlane { position: absolute; border: 1.5px solid; border-radius: 10px; box-sizing: border-box; }
    .swimlane-chapter { border-style: dashed; }
    .swimlane-title {
        position: absolute; top: 3px; left: 10px;
        font-size: 11px; font-weight: bold; opacity: 0.85; white-space: nowrap;
    }
    .swimlane-chapter .swimlane-title { left: auto; right: 10px; }
    .status {
        position: absolute;
        inset: 0;
        display: flex;
        align-items: center;
        justify-content: center;
        padding: 16px;
        color: var(--vscode-disabledForeground);
        background: var(--vscode-editor-background);
    }

    /* ── Right dock ─────────────────────────────────────────────────────── */
    .right-dock {
        position: relative;
        flex-shrink: 0;
        display: flex;
        flex-direction: column;
        min-height: 0;
        border-left: 1px solid var(--vscode-panel-border);
        background: var(--vscode-sideBar-background);
    }
    /* The dial is always dead-centre; Save/Validate float over the sides so they never shift it. */
    .dock-header {
        position: relative;
        display: flex;
        align-items: center;
        justify-content: center;
        padding: 6px 8px;
        min-height: 78px;
        border-bottom: 1px solid var(--vscode-panel-border);
    }
    .dock-header .header-left { position: absolute; left: 8px; }
    .dock-header .header-right { position: absolute; right: 8px; }
    .dock-content { flex: 1; min-height: 0; overflow-y: auto; padding: 8px; }
    .dock-hint { font-size: 12px; color: var(--vscode-descriptionForeground); padding: 8px 4px; }
    .dock-overview {
        flex-shrink: 0;
        border-top: 1px solid var(--vscode-panel-border);
        padding: 6px;
        display: flex;
        flex-direction: column;
        gap: 6px;
    }
    .dock-overview > input[type=text] { width: 100%; }
    /* Tools column sprawls from the vertical centre, minimap to its right with breathing room. */
    /* Tools on the left set the left gap; mirror it on the right, minimap flexes to fill between. */
    .overview-mid { display: flex; align-items: center; gap: 10px; padding-right: 10px; }
    .overview-tools { display: flex; flex-direction: column; gap: 4px; flex-shrink: 0; }
    .filters-below { display: flex; flex-direction: column; gap: 4px; }
    .filters-below select { width: 100%; }

    /* Soft, icon-forward buttons: almost no chrome until hovered, the glyph does the talking. */
    .icon-btn {
        display: inline-flex;
        align-items: center;
        gap: 3px;
        min-width: 26px;
        justify-content: center;
        padding: 5px 7px;
        background: transparent;
        border: none;
        border-radius: 8px;
        color: var(--vscode-foreground);
        opacity: 0.72;
        font-size: 14px;
        line-height: 1;
    }
    .icon-btn:hover {
        background: var(--vscode-toolbar-hoverBackground, rgba(128, 128, 128, 0.15));
        opacity: 1;
    }
    .icon-btn:disabled { opacity: 0.35; }
    .icon-btn.active { background: var(--vscode-button-background); color: var(--vscode-button-foreground); opacity: 1; }
    .icon-btn.sev-unvalidated { color: var(--vscode-foreground); opacity: 1; }
    .icon-btn.sev-ok { color: var(--vscode-charts-green, #89d185); opacity: 1; }
    .icon-btn.sev-warning { color: var(--vscode-charts-yellow, #cca700); opacity: 1; }
    .icon-btn.sev-error { color: var(--vscode-errorForeground, #f14c4c); opacity: 1; }
    /* Header controls read out at roughly VS Code activity-bar icon scale. */
    .dock-header .icon-btn { font-size: 18px; padding: 6px 9px; }
    .dock-header .icon-btn .codicon { font-size: 18px; }
    /* Validate is an always-present soft pill, tinted with a hue of its own state colour. */
    .validate-btn { border-radius: 14px; font-weight: 600; }
    .validate-btn.sev-unvalidated { background: color-mix(in srgb, var(--vscode-foreground) 15%, transparent); }
    .validate-btn.sev-ok { background: color-mix(in srgb, var(--vscode-charts-green, #89d185) 14%, transparent); }
    .validate-btn.sev-warning { background: color-mix(in srgb, var(--vscode-charts-yellow, #cca700) 16%, transparent); }
    .validate-btn.sev-error { background: color-mix(in srgb, var(--vscode-errorForeground, #f14c4c) 16%, transparent); }
    .validate-btn:hover { filter: brightness(1.2); }

    /* Codicons inherit their button's colour (never coloured individually) and scale per context. */
    .codicon { font-size: 15px; vertical-align: middle; }
    .overview-tools .codicon { font-size: 16px; }
    .rotary-center .codicon { font-size: 22px; }
    .rotary-pos .codicon { font-size: 13px; }
    .sim-head .codicon { font-size: 13px; vertical-align: -1px; }

    /* Rotary mode switch - large clickable readout (cycles modes) with the three modes on an arc above. */
    .rotary { position: relative; width: 100px; height: 72px; flex-shrink: 0; }
    .rotary-center {
        position: absolute;
        left: 50%; top: 68%;
        transform: translate(-50%, -50%);
        width: 40px; height: 40px;
        padding: 0;
        display: flex; align-items: center; justify-content: center;
        font-size: 22px;
        border-radius: 50%;
        background: var(--vscode-button-background);
        color: var(--vscode-button-foreground);
        border: 2px solid var(--vscode-focusBorder);
        z-index: 1;
    }
    .rotary-center:hover { background: var(--vscode-button-hoverBackground, var(--vscode-button-background)); }
    .rotary-pos {
        position: absolute;
        left: 50%; top: 68%;
        width: 22px; height: 22px;
        padding: 0;
        border-radius: 50%;
        font-size: 13px;
        line-height: 1;
        background: var(--vscode-button-secondaryBackground, var(--vscode-button-background));
        opacity: 0.5;
    }
    .rotary-pos:hover { opacity: 0.9; }
    .rotary-pos.active { opacity: 1; outline: 2px solid var(--vscode-focusBorder); }

    .resize-handle-n {
        position: absolute;
        top: -3px;
        left: 0;
        height: 6px;
        width: 100%;
        cursor: ns-resize;
        z-index: 2;
    }
    .resize-handle-w {
        position: absolute;
        top: 0;
        left: -3px;
        width: 6px;
        height: 100%;
        cursor: ew-resize;
        z-index: 2;
    }
    .resize-handle-w:hover, .resize-handle-w:active,
    .resize-handle-n:hover, .resize-handle-n:active {
        background: var(--vscode-sash-hoverBorder, var(--vscode-focusBorder));
    }

    /* ── Palette (dock content, Edit mode) ─────────────────────────────── */
    .palette-scroll { min-width: 0; }
    .palette-scroll input[type=text] { width: 100%; margin-bottom: 10px; }
    .palette-group { margin-bottom: 14px; }
    .palette-new {
        padding-bottom: 12px;
        border-bottom: 1px solid var(--vscode-panel-border, rgba(128, 128, 128, 0.35));
    }
    .palette-head {
        font-size: 11px;
        font-weight: bold;
        text-transform: uppercase;
        color: var(--vscode-descriptionForeground);
        margin-bottom: 7px;
    }
    .palette-head.toggle { display: flex; align-items: center; gap: 4px; cursor: pointer; user-select: none; }
    .palette-head.toggle:hover { color: var(--vscode-editor-foreground); }
    .palette-head .palette-count { margin-left: auto; font-weight: normal; opacity: 0.6; }
    /* Colour family: just a gap between groups - no box (the tile tint is the grouping). */
    .tile-family { margin-bottom: 7px; }
    /* auto-rows keeps every tile the same (fixed-minimum) height, whatever its label wrapping. */
    .tile-grid { display: grid; grid-template-columns: repeat(3, 1fr); grid-auto-rows: minmax(46px, auto); gap: 4px; }
    .palette-tile {
        display: flex;
        flex-direction: column;
        align-items: center;
        justify-content: center;
        gap: 3px;
        padding: 4px;
        border: 1px solid;
        border-radius: 4px;
        cursor: grab;
        overflow: hidden;
    }
    .palette-tile:hover { outline: 1px solid var(--vscode-focusBorder); }
    .palette-tile .tile-glyph { font-size: 16px; line-height: 1; }
    .palette-tile .tile-label {
        font-size: 10px;
        line-height: 1.15;
        text-align: center;
        width: 100%;
        /* Never truncate a type name - wrap it instead. */
        white-space: normal;
        overflow-wrap: anywhere;
        word-break: break-word;
    }
    .palette-empty { font-size: 11px; color: var(--vscode-descriptionForeground); }

    /* ── Minimap (dock overview) ───────────────────────────────────────── */
    .minimap-wrap { flex: 1; min-width: 0; display: flex; }
    .minimap {
        display: block;
        border: 1px solid var(--vscode-panel-border);
        border-radius: 3px;
        background: var(--vscode-editor-background);
        cursor: crosshair;
    }
    .minimap.minimap-empty {
        flex: 1; height: 118px;
        display: flex; align-items: center; justify-content: center;
        font-size: 11px; color: var(--vscode-descriptionForeground);
    }
    .minimap .mm-node { fill: var(--vscode-descriptionForeground); opacity: 0.55; }
    .minimap .mm-view {
        fill: var(--vscode-focusBorder);
        fill-opacity: 0.12;
        stroke: var(--vscode-focusBorder);
        stroke-width: 1;
    }

    /* ── Simulation controls (dock content, Simulation mode) ───────────── */
    .sim-controls { display: flex; flex-direction: column; gap: 10px; font-size: 12px; }
    .sim-section { min-width: 0; }
    .sim-head { font-weight: bold; margin-bottom: 3px; }

    /* ── Bottom panels (full width) ────────────────────────────────────── */
    .bottom-panels { flex-shrink: 0; display: flex; flex-direction: column; }
    /* Header row on a dismissable bottom panel (Problems / Simulation log). */
    .panel-bar {
        position: sticky;
        top: 0;
        display: flex;
        align-items: center;
        justify-content: space-between;
        padding: 1px 4px 2px;
        background: var(--vscode-sideBar-background);
    }
    .panel-title { font-weight: bold; font-size: 11px; color: var(--vscode-descriptionForeground); }
    .panel-close {
        background: transparent;
        border: none;
        color: var(--vscode-descriptionForeground);
        cursor: pointer;
        padding: 0 4px;
        flex-shrink: 0;
    }
    .panel-close:hover { background: transparent; color: var(--vscode-editor-foreground); }
    .sim-log-panel {
        position: relative;
        overflow-y: auto;
        padding: 4px 8px;
        background: var(--vscode-sideBar-background);
        border-top: 1px solid var(--vscode-panel-border);
        font-size: 11px;
        color: var(--vscode-descriptionForeground);
    }
    .sim-row { display: flex; gap: 4px; align-items: center; margin: 2px 0; }
    .sim-row input[type=text] { width: 90px; flex: none; }
    .sim-name { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; max-width: 160px; }
    .sim-kind {
        font-size: 10px;
        padding: 0 4px;
        border-radius: 3px;
        border: 1px solid var(--vscode-panel-border);
        color: var(--vscode-descriptionForeground);
    }
    .sim-kind.k-lua      { border-color: var(--vscode-charts-blue, #3794ff); }
    .sim-kind.k-tactical { border-color: var(--vscode-charts-yellow, #cca700); }

    .problems {
        position: relative;
        overflow-y: auto;
        border-top: 1px solid var(--vscode-panel-border);
        background: var(--vscode-sideBar-background);
        flex-shrink: 0;
        font-size: 12px;
        padding: 2px 4px;
    }
    .problem-row {
        display: flex;
        gap: 6px;
        align-items: center;
        padding: 1px 4px;
    }
    .problem-row.clickable { cursor: pointer; }
    .problem-row:hover { background: var(--vscode-list-hoverBackground, rgba(128, 128, 128, 0.15)); }
    .problem-node {
        flex-shrink: 0;
        max-width: 180px;
        color: var(--vscode-descriptionForeground);
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
    }
    .problem-msg {
        flex: 1;
        min-width: 0;
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
    }
    .problem-row button {
        background: transparent;
        border: none;
        color: var(--vscode-descriptionForeground);
        cursor: pointer;
        padding: 0 3px;
        flex-shrink: 0;
    }
    .problem-row button:hover { background: transparent; color: var(--vscode-editor-foreground); }

    .legend {
        padding: 2px 8px;
        display: flex;
        gap: 12px;
        font-size: 11px;
        color: var(--vscode-descriptionForeground);
        background: var(--vscode-sideBar-background);
        border-top: 1px solid var(--vscode-panel-border);
        flex-shrink: 0;
        flex-wrap: wrap;
    }
    .legend .swatch {
        display: inline-block;
        width: 10px; height: 10px;
        border-radius: 2px;
        border: 2px solid;
        vertical-align: -1px;
        margin-right: 3px;
    }
`;

const LIFECYCLES = ['Inactive', 'Waiting', 'Armed', 'Fired', 'Disabled'];

function App(): JSX.Element {
    const containerRef = useRef<HTMLDivElement>(null);
    const editorRef = useRef<EditorHandle | null>(null);
    const filtersRef = useRef<GraphFilters>({ ...EMPTY_FILTERS });
    // User-driven fetches (filter changes) re-layout; edit-driven refreshes patch in place.
    const fullRenderRef = useRef(true);
    const pendingGraphRef = useRef<{
        nodes: StoryGraphNodeDto[]; edges: StoryGraphEdgeDto[]; layout: StoryLayoutEntry[]; full: boolean;
    } | null>(null);

    const [filters, setFiltersState] = useState<GraphFilters>({ ...EMPTY_FILTERS });
    const [branches, setBranches] = useState<string[]>([]);
    const [threads, setThreads] = useState<string[]>([]);
    const [eventTypes, setEventTypes] = useState<string[]>([]);
    const [rewardTypes, setRewardTypes] = useState<string[]>([]);
    const [status, setStatus] = useState<string | null>('Loading story graph…');
    // True while a full rebuild's auto-arrange is in flight, so the canvas stays covered instead
    // of flashing the pre-layout node stack (every node starts at the same spot) before it settles.
    const [layouting, setLayouting] = useState(false);
    const [createRequest, setCreateRequest] = useState<CreateRequest | null>(null);
    const [simState, setSimState] = useState<SimState | null>(null);
    const simRef = useRef<SimState | null>(null);
    const [mode, setMode] = useState<EditorMode>('view');
    // Which modes the flags permit. Both default off, matching the extension's own fallbacks, so a
    // panel that somehow never receives the message stays read-only rather than offering modes whose
    // every request the server would reject. View is implied - the panel wouldn't open without it.
    const [availableModes, setAvailableModes] = useState<{ edit: boolean; simulate: boolean }>(
        { edit: false, simulate: false });
    const [problems, setProblems] = useState<StoryDiagnosticDto[]>([]);
    const [showProblems, setShowProblems] = useState(false);
    const [showSimLog, setShowSimLog] = useState(true);
    // Whether the current (possibly staged) state has been validated since it last changed.
    const [validated, setValidated] = useState(false);
    // Swimlane overlays, toggled independently (persisted per-workspace via WorkspaceSettings).
    const [showThreadLanes, setShowThreadLanes] = useState(false);
    const [showChapterLanes, setShowChapterLanes] = useState(false);
    // Count of unsaved staged edits - drives the Save button's enabled/dirty state.
    const [pendingCount, setPendingCount] = useState(0);
    const [saving, setSaving] = useState(false);

    useEffect(() => { currentMode = mode; editorRef.current?.refreshMode(); }, [mode]);

    useEffect(() => {
        onPendingChanged = () => {
            setPendingCount(pendingCommands.length);
            setValidated(false); // the staged set changed → the last validation is stale
            // Mirror the queue to the extension so it can offer to save if the tab is closed while
            // dirty (a disposed webview can't prompt - the panel owns that).
            vscode.postMessage({ type: 'pendingSync', commands: [...pendingCommands] });
        };
        return () => { onPendingChanged = () => { /* detached on unmount */ }; };
    }, []);

    const saveEdits = useCallback(() => {
        if (!hasPendingChanges()) { return; }
        setSaving(true);
        vscode.postMessage({ type: 'saveBatch', commands: [...pendingCommands] });
    }, []);

    const validateEdits = useCallback(() => {
        vscode.postMessage({ type: 'validateBatch', commands: [...pendingCommands] });
    }, []);

    // When a Save is triggered by leaving Edit with unsaved changes, the mode switch waits for the
    // save to land (View/Simulate must run against committed text) - this holds the target mode.
    const pendingModeAfterSave = useRef<EditorMode | null>(null);

    const doSwitchMode = useCallback((next: EditorMode) => {
        setMode(next);
        if (next === 'simulate') {
            setShowSimLog(true); // re-show the log each time simulation is entered
            if (!simRef.current?.running) { sendSim('start'); }
        } else if (simRef.current?.running) {
            sendSim('stop');
        }
        if (next !== 'edit') { setCreateRequest(null); }
    }, []);

    const switchMode = useCallback((next: EditorMode) => {
        // A disabled mode is not offered by the switch, but guard here too - this is the single
        // funnel every mode change goes through, including the centre-button cycle.
        if ((next === 'edit' && !availableModes.edit)
            || (next === 'simulate' && !availableModes.simulate)) { return; }
        // Leaving Edit with unsaved staged changes → ask (the panel shows the modal and replies with
        // 'dirtyExitChoice'); the switch happens then. Simulate/View therefore run on committed text.
        if (mode === 'edit' && next !== 'edit' && hasPendingChanges()) {
            vscode.postMessage({ type: 'confirmDirtyExit', next });
            return;
        }
        doSwitchMode(next);
    }, [mode, doSwitchMode, availableModes]);

    const onCanvasDragOver = useCallback((e: DragEvent): void => {
        if (mode !== 'edit') { return; }
        e.preventDefault();
        e.dataTransfer.dropEffect = 'copy';
    }, [mode]);

    /**
     * Creates a new event node directly on the canvas (no toolbar form): a unique default name in
     * the nearest thread, at `position`, optionally pre-typed. Everything about it - the name,
     * Branch/Perpetual/Dialog, and trigger/reward types (dropped on) - is then editable in the node
     * body. The name is auto so the user can just drop and rename in place.
     */
    const createEventAt = useCallback((
        position: { x: number; y: number }, eventType: string | null,
    ): void => {
        const handle = editorRef.current;
        if (!handle) { return; }
        const thread = handle.nearestEventThread(position, threads);
        if (!thread) {
            setStatus('This campaign has no thread file to add events to - create a thread first.');
            return;
        }
        const taken = new Set(handle.eventLabels());
        let name = 'New_Event';
        for (let i = 2; taken.has(name); i++) { name = `New_Event_${i}`; }
        handle.presetPosition(thread, name, position);
        // Open the new node's rename box as soon as it materialises - drop then type the name.
        pendingAutoRename.add(autoRenameKey(thread, name));
        sendCommand({ kind: 'createEvent', threadUri: thread, newName: name, eventType: eventType || null });
    }, [threads]);

    const onCanvasDrop = useCallback((e: DragEvent): void => {
        if (mode !== 'edit') { return; }
        const raw = e.dataTransfer.getData(PALETTE_DRAG_MIME);
        if (!raw) { return; }
        e.preventDefault();
        const drag = JSON.parse(raw) as PaletteDrag;
        // Types attach by dropping onto a node (data-node-id lookup, not geometry) - and only
        // into an EMPTY slot: types are immutable, an occupied slot must be cleared (✕) first.
        const targetId = e.target instanceof HTMLElement
            ? e.target.closest('[data-node-id]')?.getAttribute('data-node-id') ?? null
            : null;
        const target = targetId ? editorRef.current?.getEventNode(targetId) : null;
        if (drag.category === 'reward') {
            // Dropped on blank canvas or a virtual node it's a no-op, not a create form.
            if (target && drag.type && !target.rewardType) {
                sendCommand({
                    kind: 'setRewardType', threadUri: target.threadUri, eventName: target.eventName,
                    value: drag.type,
                });
            }
            return;
        }
        if (drag.category === 'trigger' && target) {
            // Tactical triggers need their manifest-file form - no drop-on-node shortcut for them.
            if (drag.type && !target.eventType && !TACTICAL_EVENT_TYPES.has(drag.type)) {
                sendCommand({
                    kind: 'setEventType', threadUri: target.threadUri, eventName: target.eventName,
                    value: drag.type,
                });
            }
            return;
        }
        if (drag.category === 'andJunction' || drag.category === 'orJunction') {
            // Local-only - no server command, no create form. Wiring it up is itself the gesture.
            const position = editorRef.current?.toGraphPosition(e.clientX, e.clientY) ?? null;
            if (position) {
                editorRef.current?.createStagingJunction(
                    position, drag.category === 'orJunction' ? 'or' : 'and');
            }
            return;
        }
        const position = editorRef.current?.toGraphPosition(e.clientX, e.clientY) ?? null;
        if (!position) { return; }
        // Land/space tactical triggers still need the dedicated manifest-file form (their plot
        // file is mandatory and can't be filled in-node later); everything else drops as an
        // editable node straight onto the canvas - no toolbar form.
        if (drag.type && TACTICAL_EVENT_TYPES.has(drag.type)) {
            setCreateRequest({ ...drag, category: 'tactical', position });
            return;
        }
        createEventAt(position, drag.category === 'trigger' ? drag.type : null);
    }, [mode, createEventAt]);

    const applySimOverlay = useCallback((state: SimState | null) => {
        simRef.current = state;
        setSimState(state);
        const handle = editorRef.current;
        if (!handle) { return; }
        handle.applyLifecycles(state?.running
            ? new Map(state.nodes.map(n => [n.nodeId, n.lifecycle]))
            : null);
    }, []);

    const fetchGraph = useCallback((next: GraphFilters) => {
        filtersRef.current = next;
        setFiltersState(next);
        fullRenderRef.current = true;
        vscode.postMessage({ type: 'fetch', filters: next });
    }, []);

    /** Runs setGraph, keeping the canvas covered for the duration of a full rebuild's auto-arrange. */
    const runSetGraph = useCallback((handle: EditorHandle, g: {
        nodes: StoryGraphNodeDto[]; edges: StoryGraphEdgeDto[]; layout: StoryLayoutEntry[]; full: boolean;
    }) => {
        if (g.full) { setLayouting(true); }
        // Re-apply staged edits once the (re)built graph settles, so a reconcile never reverts them.
        const done = handle.setGraph(g.nodes, g.edges, g.layout, g.full)
            .then(() => reapplyStagedCommands());
        if (g.full) { void done.finally(() => setLayouting(false)); }
    }, []);

    const applyGraph = useCallback((
        nodes: StoryGraphNodeDto[], edges: StoryGraphEdgeDto[], layout: StoryLayoutEntry[], full: boolean
    ) => {
        setBranches(() => {
            const found = [...new Set(nodes.map(n => n.branch).filter((b): b is string => !!b))].sort();
            const active = filtersRef.current.branch;
            return active && !found.includes(active) ? [...found, active].sort() : found;
        });
        setThreads([...new Set(nodes.map(n => n.threadUri).filter((u): u is string => !!u))].sort());
        if (!nodes.length) {
            setStatus('No events match the current filters.');
            return;
        }
        setStatus(null);
        const handle = editorRef.current;
        if (handle) {
            pendingGraphRef.current = null;
            runSetGraph(handle, { nodes, edges, layout, full });
        } else {
            pendingGraphRef.current = { nodes, edges, layout, full };
        }
    }, [runSetGraph]);

    useEffect(() => {
        onGraphDesynced = () => {
            // Re-fetch with the current filters; the incremental patch restores the view.
            vscode.postMessage({ type: 'fetch', filters: filtersRef.current });
        };
        onReachableFromRequested = id => setFilter({ reachableFrom: id });
        requestPreview = () => vscode.postMessage({
            type: 'previewGraph', commands: [...pendingCommands], filters: filtersRef.current,
        });

        const onMessage = (event: MessageEvent): void => {
            const msg = event.data as { type: string; [key: string]: unknown };
            switch (msg.type) {
                case 'schema': {
                    untestedTypes.clear();
                    for (const name of (msg.untestedEventTypes as string[] | undefined) ?? []) { untestedTypes.add(name); }
                    for (const name of (msg.untestedRewardTypes as string[] | undefined) ?? []) { untestedTypes.add(name); }
                    setEventTypes((msg.eventTypes as string[] | undefined) ?? []);
                    setRewardTypes((msg.rewardTypes as string[] | undefined) ?? []);
                    // EventNodeView is rendered by rete's own portal pipeline, not as an App
                    // child, so it can't receive these as React props - mirrored into module
                    // scope (eventTypeParams/rewardTypeParams/untestedTypes) for that reason.
                    eventTypeParams.clear();
                    for (const [name, params] of Object.entries(
                        (msg.eventTypeParams as Record<string, StoryParamSchemaDto[]> | undefined) ?? {})) {
                        eventTypeParams.set(name, params);
                    }
                    rewardTypeParams.clear();
                    for (const [name, params] of Object.entries(
                        (msg.rewardTypeParams as Record<string, StoryParamSchemaDto[]> | undefined) ?? {})) {
                        rewardTypeParams.set(name, params);
                    }
                    editorRef.current?.refreshMode(); // param schema changed → row counts may have too
                    break;
                }
                case 'graph': {
                    // A preview (staged structural change) always patches in place - never re-layout,
                    // so the viewport and node positions stay put - and must not consume a pending
                    // full render queued by a real fetch/filter change.
                    const full = msg.preview ? false : fullRenderRef.current;
                    if (!msg.preview) { fullRenderRef.current = false; }
                    const graphNodes = (msg.nodes as StoryGraphNodeDto[] | undefined) ?? [];
                    // Big campaigns render hundreds of full form nodes - collapse the Trigger and
                    // Reward sections by default past a threshold so first paint (and every later
                    // measure) touches far less DOM. Only seeds nodes with no explicit choice yet,
                    // so a user's expand/collapse persists across refreshes. Must run BEFORE the
                    // graph is applied - buildFull reads collapse state when it measures heights.
                    if (graphNodes.length > LARGE_GRAPH_NODE_COUNT) {
                        for (const dto of graphNodes) {
                            if (dto.kind === 'Event' && !collapsedSections.has(dto.id)) {
                                collapsedSections.set(dto.id, { general: false, trigger: true, reward: true });
                            }
                        }
                    }
                    applyGraph(
                        graphNodes,
                        (msg.edges as StoryGraphEdgeDto[] | undefined) ?? [],
                        (msg.layout as StoryLayoutEntry[] | undefined) ?? [],
                        full);
                    // A running simulation keeps painting its lifecycles over fresh renders.
                    if (simRef.current?.running) { applySimOverlay(simRef.current); }
                    break;
                }
                case 'simState':
                    applySimOverlay((msg.state as SimState | null) ?? null);
                    break;
                case 'simChanged':
                    sendSim('getState');
                    break;
                case 'paramOptions': {
                    const resolve = pendingOptionRequests.get(msg.requestId as number);
                    if (resolve) {
                        pendingOptionRequests.delete(msg.requestId as number);
                        resolve((msg.options as ParamOption[] | undefined) ?? []);
                    }
                    break;
                }
                case 'diagnostics': {
                    const diags = (msg.diagnostics as StoryDiagnosticDto[] | undefined) ?? [];
                    // Repaint only the nodes whose marker set actually changed (union of before
                    // and after), not every node - a whole-graph refreshMode() here was a real
                    // hitch on large campaigns.
                    const affected = new Set<string>(nodeDiagnostics.keys());
                    nodeDiagnostics.clear();
                    for (const d of diags) {
                        if (!d.nodeId) { continue; }
                        const list = nodeDiagnostics.get(d.nodeId) ?? [];
                        list.push(d);
                        nodeDiagnostics.set(d.nodeId, list);
                        affected.add(d.nodeId);
                    }
                    setProblems(diags);
                    setValidated(true); // a validation run just completed
                    // Validate is the only source of diagnostics now - auto-open the bottom panel
                    // when there's something to show (clean run just greens the Validate button).
                    setShowProblems(diags.length > 0);
                    editorRef.current?.repaintNodes(affected);
                    break;
                }
                case 'invalidate':
                    vscode.postMessage({ type: 'fetch', filters: filtersRef.current });
                    break;
                case 'workspaceSettings':
                    setShowThreadLanes(msg.showThreadLanes === true);
                    setShowChapterLanes(msg.showChapterLanes === true);
                    break;
                case 'availableModes':
                    setAvailableModes({ edit: msg.edit === true, simulate: msg.simulate === true });
                    break;
                case 'confirmStageResult':
                    if (msg.proceed) { stageCommand(msg.payload as Record<string, unknown>); }
                    break;
                case 'saveResult':
                    setSaving(false);
                    // Success clears the queue; the server's storyGraphChanged then reconciles the
                    // graph to committed truth. Failure keeps the queue - the panel surfaced the
                    // error and named the offending change, so the user can fix and re-save.
                    if (msg.success) {
                        clearPendingCommands();
                        // A successful save returns to View mode (a dirty-exit save targets whatever
                        // mode the user was switching to - View or Simulate).
                        doSwitchMode(pendingModeAfterSave.current ?? 'view');
                        pendingModeAfterSave.current = null;
                    } else {
                        pendingModeAfterSave.current = null; // save failed → stay in Edit to fix it
                    }
                    break;
                case 'dirtyExitChoice': {
                    const next = msg.next as EditorMode;
                    if (msg.choice === 'save') {
                        pendingModeAfterSave.current = next; // switch once the save lands
                        saveEdits();
                    } else if (msg.choice === 'discard') {
                        clearPendingCommands();
                        // Staged edits were local-only - re-fetch to drop them and show committed state.
                        vscode.postMessage({ type: 'fetch', filters: filtersRef.current });
                        doSwitchMode(next);
                    }
                    // 'cancel' → stay in Edit with the queue intact
                    break;
                }
                case 'error':
                    setStatus('⚠ ' + String(msg.message));
                    break;
            }
        };
        window.addEventListener('message', onMessage);
        vscode.postMessage({ type: 'ready' });
        return () => window.removeEventListener('message', onMessage);
    }, [applyGraph, applySimOverlay, doSwitchMode, saveEdits]);

    useEffect(() => {
        const container = containerRef.current;
        if (!container) { return; }
        let disposed = false;
        let handle: EditorHandle | null = null;
        void createEditor(container).then(created => {
            if (disposed) { created.destroy(); return; }
            handle = created;
            editorRef.current = created;
            editorHandleRef = created;
            const pending = pendingGraphRef.current;
            if (pending) {
                pendingGraphRef.current = null;
                runSetGraph(created, pending);
            }
        });
        return () => {
            disposed = true;
            editorRef.current = null;
            editorHandleRef = null;
            handle?.destroy();
        };
    }, [runSetGraph]);

    const setFilter = (patch: Partial<GraphFilters>): void => fetchGraph({ ...filtersRef.current, ...patch });
    const clearFilters = (): void => fetchGraph({ ...EMPTY_FILTERS });
    const toggleLane = (which: 'thread' | 'chapter'): void => {
        const nextThread = which === 'thread' ? !showThreadLanes : showThreadLanes;
        const nextChapter = which === 'chapter' ? !showChapterLanes : showChapterLanes;
        setShowThreadLanes(nextThread);
        setShowChapterLanes(nextChapter);
        vscode.postMessage({ type: 'setLanePref', showThreadLanes: nextThread, showChapterLanes: nextChapter });
    };
    const anyFilter = !!(filters.nameFilter || filters.branch || filters.lifecycle || filters.reachableFrom);

    // Validate button reads out validation health (codicon name; coloured via sev-* class):
    // unvalidated (stale/never run) → error → warning → clean.
    const severity = !validated ? 'unvalidated'
        : problems.some(p => p.severity === 'error') ? 'error'
            : problems.some(p => p.severity === 'warning') ? 'warning' : 'ok';
    const severityIcon = severity === 'unvalidated' ? 'question'
        : severity === 'error' ? 'error'
            : severity === 'warning' ? 'warning' : 'check';

    const { size: dockWidth, handleProps: dockResize } = useEdgeResize(
        paletteWidthMemo, 210, 520, 'w', v => { paletteWidthMemo = v; });

    return (
        <Shell>
            <GlobalStyle />
            <svg width="0" height="0" style={{ position: 'absolute' }} aria-hidden="true">
                <defs>
                    <marker id="story-arrow" viewBox="0 0 10 10" refX="9" refY="5"
                        markerWidth="6" markerHeight="6" orient="auto-start-reverse">
                        <path d="M 0 0 L 10 5 L 0 10 z" fill="var(--vscode-charts-foreground, #999)" />
                    </marker>
                </defs>
            </svg>
            {createRequest && createRequest.category === 'tactical' && mode === 'edit' ? (
                <TacticalCreateBar
                    key={`tactical:${createRequest.type ?? ''}`}
                    threads={threads}
                    initialType={createRequest.type}
                    onCreate={(threadUri, newName, value, file) => {
                        if (createRequest.position) {
                            editorRef.current?.presetPosition(threadUri, newName, createRequest.position);
                        }
                        sendCommand({ kind: 'createTacticalAttachment', threadUri, newName, value, file });
                        setCreateRequest(null);
                    }}
                    onClose={() => setCreateRequest(null)}
                />
            ) : null}
            <div className="body">
                <div className="canvas-area">
                    {/* These render into rete's content holder via a portal, not here. */}
                    {showThreadLanes ? <SwimlaneOverlay getHandle={() => editorRef.current} by="thread" /> : null}
                    {showChapterLanes ? <SwimlaneOverlay getHandle={() => editorRef.current} by="chapter" /> : null}
                    <div
                        className="canvas" ref={containerRef}
                        onDragOver={onCanvasDragOver} onDrop={onCanvasDrop}
                    />
                    {status || layouting ? <p className="status">{status ?? 'Arranging layout…'}</p> : null}
                </div>
                <div className="right-dock" style={{ width: dockWidth }}>
                    <div className="resize-handle-w" title="Drag to resize" {...dockResize} />
                    <div className="dock-header">
                        {mode === 'edit' ? (
                            <button
                                className={'icon-btn header-left' + (pendingCount > 0 ? ' active' : '')}
                                disabled={pendingCount === 0 || saving}
                                onClick={saveEdits}
                                title="Save - write all staged changes to the XML files"
                            ><span className="codicon codicon-save" />{pendingCount > 0 ? ` ${pendingCount}` : ''}</button>
                        ) : null}
                        <RotaryModeSwitch mode={mode} onSelect={switchMode} available={availableModes} />
                        <button
                            className={'icon-btn validate-btn header-right sev-' + severity}
                            onClick={() => { validateEdits(); }}
                            title="Validate - check the story for problems (opens the panel below)"
                        ><span className={'codicon codicon-' + severityIcon} />{problems.length ? ` ${problems.length}` : ''}</button>
                    </div>
                    <div className="dock-content">
                        {mode === 'edit'
                            ? <NodePalette eventTypes={eventTypes} rewardTypes={rewardTypes} /> : null}
                        {mode === 'simulate' && simState?.running ? <SimControls state={simState} /> : null}
                        {mode === 'simulate' && !simState?.running
                            ? <div className="dock-hint">Starting simulation…</div> : null}
                        {mode === 'view'
                            ? <div className="dock-hint">Read-only. Switch to Edit to change the story,
                                or Simulation to run it forward.</div> : null}
                    </div>
                    <div className="dock-overview">
                        <input
                            type="text" placeholder="Filter event names…" value={filters.nameFilter}
                            onChange={e => setFilter({ nameFilter: e.target.value })}
                        />
                        <div className="overview-mid">
                            <div className="overview-tools">
                                {anyFilter ? (
                                    <button className="icon-btn" onClick={clearFilters} title="Clear all filters">
                                        <span className="codicon codicon-clear-all" />
                                    </button>
                                ) : null}
                                <button
                                    className="icon-btn"
                                    onClick={() => {
                                        const handle = editorRef.current;
                                        if (!handle) { return; }
                                        setLayouting(true);
                                        void handle.autoArrange().finally(() => setLayouting(false));
                                    }}
                                    title="Arrange - recompute the automatic layout"
                                ><span className="codicon codicon-type-hierarchy" /></button>
                                <button className="icon-btn" onClick={() => editorRef.current?.fit()} title="Fit graph to view">
                                    <span className="codicon codicon-screen-full" />
                                </button>
                                <button
                                    className={'icon-btn' + (showThreadLanes ? ' active' : '')}
                                    onClick={() => toggleLane('thread')}
                                    title="Toggle thread lanes"
                                ><span className="codicon codicon-list-tree" /></button>
                                <button
                                    className={'icon-btn' + (showChapterLanes ? ' active' : '')}
                                    onClick={() => toggleLane('chapter')}
                                    title="Toggle chapter lanes"
                                ><span className="codicon codicon-book" /></button>
                            </div>
                            <Minimap getHandle={() => editorRef.current} />
                        </div>
                        <div className="filters-below">
                            <select value={filters.branch} onChange={e => setFilter({ branch: e.target.value })} title="Branch">
                                <option value="">All branches</option>
                                {branches.map(b => <option key={b} value={b}>{b}</option>)}
                            </select>
                            <select value={filters.lifecycle} onChange={e => setFilter({ lifecycle: e.target.value })} title="Lifecycle">
                                <option value="">Any lifecycle</option>
                                {LIFECYCLES.map(l => <option key={l} value={l}>{l}</option>)}
                            </select>
                        </div>
                    </div>
                </div>
            </div>
            <div className="bottom-panels">
                {showProblems && problems.length ? (
                    <ProblemsBar
                        problems={problems}
                        onJump={id => editorRef.current?.centerNode(id)}
                        onClose={() => setShowProblems(false)}
                    />
                ) : null}
                {simState?.running && showSimLog ? (
                    <SimLog state={simState} onClose={() => setShowSimLog(false)} />
                ) : null}
                <div className="legend">
                    <span><span className="swatch" style={{ borderColor: 'var(--vscode-disabledForeground, #888)' }} />Inactive</span>
                    <span><span className="swatch" style={{ borderColor: 'var(--vscode-charts-blue, #3794ff)' }} />Waiting</span>
                    <span><span className="swatch" style={{ borderColor: 'var(--vscode-charts-green, #89d185)' }} />Armed</span>
                    <span><span className="swatch" style={{ borderColor: 'var(--vscode-charts-purple, #b180d7)' }} />Fired</span>
                    <span><span className="swatch" style={{ borderColor: 'var(--vscode-charts-red, #f14c4c)' }} />Disabled</span>
                    <span>◇ OR</span><span>○ AND</span><span>dashed = portal / tactical / untested</span>
                    <span>drag socket→socket = prereq</span>
                </div>
            </div>
        </Shell>
    );
}

/** Session-remembered chrome sizes, so a re-mount (mode switch, sim restart) keeps the choice. */
let paletteWidthMemo = 270;
let simBarHeightMemo = 140;

/**
 * Pointer-capture drag resizing for one panel edge. `axis` maps pointer movement to growth:
 * 'e' = dragging right grows (a left panel's right edge), 'w' = dragging left grows (a right dock's
 * left edge), 'n' = dragging up grows (a bottom bar's top edge). Plain pointer capture on the handle
 * - no window listeners to leak.
 */
function useEdgeResize(
    initial: number, min: number, max: number, axis: 'e' | 'w' | 'n', persist: (v: number) => void,
): { size: number; handleProps: Record<string, unknown> } {
    const [size, setSize] = useState(initial);
    const drag = useRef<{ start: number; base: number } | null>(null);
    const clamp = (v: number): number => Math.max(min, Math.min(max, v));
    const horizontal = axis === 'e' || axis === 'w';
    return {
        size,
        handleProps: {
            onPointerDown: (e: ReactPointerEvent<HTMLDivElement>) => {
                e.preventDefault();
                e.currentTarget.setPointerCapture(e.pointerId);
                drag.current = { start: horizontal ? e.clientX : e.clientY, base: size };
            },
            onPointerMove: (e: ReactPointerEvent<HTMLDivElement>) => {
                if (!drag.current) { return; }
                const delta = axis === 'e' ? e.clientX - drag.current.start
                    : axis === 'w' ? drag.current.start - e.clientX
                        : drag.current.start - e.clientY;
                const next = clamp(drag.current.base + delta);
                setSize(next);
                persist(next);
            },
            onPointerUp: () => { drag.current = null; },
        },
    };
}

let problemsHeightMemo = 150;

/**
 * The campaign's validation problems. A row whose diagnostic lives on a graph node is clickable
 * as a whole (jumps to the node); the ↗ button opens the XML at the diagnostic's line either way.
 * Resizable by dragging its top edge, like the sim bar.
 */
function ProblemsBar(props: {
    problems: StoryDiagnosticDto[];
    onJump: (nodeId: string) => void;
    onClose: () => void;
}): JSX.Element {
    const { size: height, handleProps } = useEdgeResize(
        problemsHeightMemo, 60, 420, 'n', v => { problemsHeightMemo = v; });
    return (
        <div className="problems" style={{ height }}>
            <div className="resize-handle-n" title="Drag to resize" {...handleProps} />
            <div className="panel-bar">
                <span className="panel-title">Problems ({props.problems.length})</span>
                <button className="panel-close" onClick={props.onClose} title="Close"><span className="codicon codicon-close" /></button>
            </div>
            {props.problems.map((problem, i) => (
                <div
                    className={'problem-row' + (problem.nodeId ? ' clickable' : '')}
                    key={i}
                    title={problem.nodeId ? 'Click to show this node in the graph' : undefined}
                    onClick={problem.nodeId ? () => props.onJump(problem.nodeId!) : undefined}
                >
                    <span className={'diag-badge diag-' + (problem.severity === 'error' ? 'error' : 'warning')}>
                        {problem.severity === 'error' ? '⛔' : '⚠'}
                    </span>
                    <span className="problem-node" title={problem.nodeId ?? problem.uri}>
                        {problem.nodeId
                            ? problem.nodeId.slice(problem.nodeId.indexOf('#') + 1)
                            : baseName(problem.uri)}
                    </span>
                    <span className="problem-msg" title={problem.message}>{problem.message}</span>
                    <button
                        onClick={e => {
                            e.stopPropagation(); // the XML button must not also trigger the row's jump
                            vscode.postMessage({ type: 'openXml', threadUri: problem.uri, line: problem.line });
                        }}
                        title="Open in XML"
                    >↗</button>
                </div>
            ))}
        </div>
    );
}

/** The running simulation: clock, flag inspector, intervention queue, and the step log. */
/** The simulation driver controls - clock, flags, and pending interventions - stacked for the dock. */
function SimControls(props: { state: SimState }): JSX.Element {
    const state = props.state;
    const [advanceBy, setAdvanceBy] = useState('10');
    const [flagName, setFlagName] = useState('');

    return (
        <div className="sim-controls">
            <div className="sim-section">
                <div className="sim-head"><span className="codicon codicon-watch" /> Clock - {state.clock.toFixed(0)}s</div>
                <div className="sim-row">
                    <input type="text" value={advanceBy} onChange={e => setAdvanceBy(e.target.value)} title="Seconds" />
                    <button onClick={() => {
                        const seconds = Number(advanceBy);
                        if (seconds > 0) { sendSim('advanceClock', { seconds }); }
                    }} title="Advance the virtual clock">Advance</button>
                </div>
            </div>
            <div className="sim-section">
                <div className="sim-head">Flags</div>
                {state.flags.map(f => (
                    <div className="sim-row" key={f.name}>
                        <span className="sim-name" title={f.name}>{f.name}</span>
                        <button onClick={() => sendSim('setFlag', { flag: f.name, value: f.value !== 0 ? 0 : 1 })}
                            title={`Toggle ${f.name}`}>
                            {f.value !== 0 ? '1 → 0' : '0 → 1'}
                        </button>
                    </div>
                ))}
                <div className="sim-row">
                    <input
                        type="text" placeholder="Set flag…" value={flagName}
                        onChange={e => setFlagName(e.target.value)}
                        onKeyDown={e => {
                            if (e.key === 'Enter' && flagName.trim()) {
                                sendSim('setFlag', { flag: flagName.trim(), value: 1 });
                                setFlagName('');
                            }
                        }}
                    />
                </div>
            </div>
            <div className="sim-section">
                <div className="sim-head">Waiting on</div>
                {state.interventions.length === 0 ? <div className="sim-row">nothing - story exhausted</div> : null}
                {state.interventions.map(i => (
                    <div className="sim-row" key={i.nodeId}>
                        <span className={'sim-kind k-' + i.kind}>{i.kind}</span>
                        <span className="sim-name" title={`${i.eventName} (${i.eventType ?? '?'})`}>{i.eventName}</span>
                        {i.kind === 'lua' && i.options.length
                            ? i.options.map(o => (
                                <button key={o} title={`Story_Event("${o}")`}
                                    onClick={() => sendSim('luaNotify', { id: o })}>{o}</button>
                            ))
                            : <button title="Fire this event's trigger"
                                onClick={() => sendSim('satisfyTrigger', { nodeId: i.nodeId })}>Fire</button>}
                    </div>
                ))}
                {state.luaNotifications.length ? (
                    <div className="sim-row">
                        <select
                            value=""
                            title="Simulate a Lua Story_Event call"
                            onChange={e => { if (e.target.value) { sendSim('luaNotify', { id: e.target.value }); } }}
                        >
                            <option value="">Lua Story_Event…</option>
                            {state.luaNotifications.map(id => <option key={id} value={id}>{id}</option>)}
                        </select>
                    </div>
                ) : null}
            </div>
        </div>
    );
}

/** The simulation step log - full-width bottom panel (VS Code-style), resizable by its top edge. */
function SimLog(props: { state: SimState; onClose: () => void }): JSX.Element {
    const { size: height, handleProps } = useEdgeResize(
        simBarHeightMemo, 60, 320, 'n', v => { simBarHeightMemo = v; });
    return (
        <div className="sim-log-panel" style={{ height }}>
            <div className="resize-handle-n" title="Drag to resize" {...handleProps} />
            <div className="panel-bar">
                <span className="panel-title">Simulation log</span>
                <button className="panel-close" onClick={props.onClose} title="Close"><span className="codicon codicon-close" /></button>
            </div>
            {props.state.log.slice(-100).map((line, i) => (
                <div className="sim-log-line" key={i}>{line}</div>
            ))}
        </div>
    );
}

/**
 * The three editor modes on a "half-horizon" arc (angles in degrees, 0 = 3 o'clock, 90 = down): the
 * modes sit ABOVE the horizontal line running through the lower half of the large centre readout.
 */
const ROTARY_MODES: { id: EditorMode; icon: string; label: string; angle: number }[] = [
    { id: 'view', icon: 'eye', label: 'View', angle: 210 },          // upper-left
    { id: 'edit', icon: 'edit', label: 'Edit', angle: 270 },          // top
    { id: 'simulate', icon: 'play', label: 'Simulation', angle: 330 }, // upper-right
];

/**
 * A rotary-switch-style mode selector: the active mode's icon reads out large in the centre, the
 * available modes arc above it like a half horizon, and clicking one "rotates" to it.
 *
 * Edit and Simulation are feature-flagged (both off by default), and a mode that is off is omitted
 * entirely rather than shown disabled - there is nothing the user could do about it from here, and
 * every request it makes would be refused server-side. Each mode keeps its fixed angle when others
 * are hidden, so View stays where the eye expects it.
 */
function RotaryModeSwitch(props: {
    mode: EditorMode; onSelect: (m: EditorMode) => void;
    available: { edit: boolean; simulate: boolean };
}): JSX.Element {
    const enabled = ROTARY_MODES.filter(
        m => (m.id === 'edit' ? props.available.edit
            : m.id === 'simulate' ? props.available.simulate : true));
    const active = enabled.find(m => m.id === props.mode) ?? enabled[0];
    const radius = 34;
    const order = enabled.map(m => m.id);
    const cycle = (): void => {
        const at = order.indexOf(props.mode);
        props.onSelect(order[(at + 1) % order.length]);
    };
    return (
        <div className="rotary">
            {enabled.map(m => {
                const rad = (m.angle * Math.PI) / 180;
                const x = Math.cos(rad) * radius;
                const y = Math.sin(rad) * radius;
                return (
                    <button
                        key={m.id}
                        className={'rotary-pos' + (m.id === props.mode ? ' active' : '')}
                        style={{ transform: `translate(-50%, -50%) translate(${x}px, ${y}px)` }}
                        title={m.label}
                        onClick={() => props.onSelect(m.id)}
                    ><span className={'codicon codicon-' + m.icon} /></button>
                );
            })}
            <button
                className="rotary-center"
                title={order.length > 1
                    ? `${active.label} - click to switch mode`
                    : `${active.label} - the other modes are disabled in settings`}
                onClick={cycle}
            >
                <span className={'codicon codicon-' + active.icon} />
            </button>
        </div>
    );
}

const MINIMAP_H = 118;

/**
 * A hand-built overview of the whole graph (the rete minimap plugin can only overlay the canvas, not
 * dock here). Draws every node scaled to the graph extent plus the current viewport rectangle;
 * click/drag pans. Width is dynamic - measured from its flex slot - with a fixed height. Stays live
 * via the `onAreaChanged` bridge (pan/zoom/node-move) and re-reads node geometry on every render.
 */
function Minimap(props: { getHandle: () => EditorHandle | null }): JSX.Element {
    const [, force] = useReducer((x: number) => x + 1, 0);
    const wrapRef = useRef<HTMLDivElement>(null);
    const [width, setWidth] = useState(150);
    const dragging = useRef(false);
    useEffect(() => subscribeAreaChange(() => force()), []);
    useEffect(() => {
        const el = wrapRef.current;
        if (!el) { return; }
        const measure = (): void => setWidth(Math.max(60, Math.round(el.clientWidth)));
        measure();
        const observer = new ResizeObserver(measure);
        observer.observe(el);
        return () => observer.disconnect();
    }, []);

    const handle = props.getHandle();
    const data = handle?.getMinimap();

    let inner: JSX.Element;
    if (!data || data.nodes.length === 0) {
        inner = <div className="minimap minimap-empty">no nodes</div>;
    } else {
        // Extent comes from the NODES only. The viewport rect is measured in graph coordinates, so
        // zooming out inflates it without bound (w = canvasWidth / k); letting it drive the extent
        // collapsed `scale` toward zero and rendered every node sub-pixel - the minimap went blank
        // exactly when a large campaign was zoomed out far enough to fit on screen. The viewport is
        // still drawn, just clipped to the node extent.
        let minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity;
        for (const r of data.nodes) {
            minX = Math.min(minX, r.x); minY = Math.min(minY, r.y);
            maxX = Math.max(maxX, r.x + r.w); maxY = Math.max(maxY, r.y + r.h);
        }
        const pad = 60;
        minX -= pad; minY -= pad; maxX += pad; maxY += pad;
        const scale = Math.min(width / (maxX - minX), MINIMAP_H / (maxY - minY));
        // Centre the drawing in the (usually wider) box so it isn't jammed to the top-left.
        const offX = (width - (maxX - minX) * scale) / 2;
        const offY = (MINIMAP_H - (maxY - minY) * scale) / 2;
        const sx = (x: number): number => (x - minX) * scale + offX;
        const sy = (y: number): number => (y - minY) * scale + offY;
        const panFromEvent = (e: ReactPointerEvent<SVGSVGElement>): void => {
            const rect = e.currentTarget.getBoundingClientRect();
            handle?.panTo((e.clientX - rect.left - offX) / scale + minX,
                (e.clientY - rect.top - offY) / scale + minY);
        };
        inner = (
            <svg
                className="minimap" width={width} height={MINIMAP_H}
                onPointerDown={e => { dragging.current = true; e.currentTarget.setPointerCapture(e.pointerId); panFromEvent(e); }}
                onPointerMove={e => { if (dragging.current) { panFromEvent(e); } }}
                onPointerUp={() => { dragging.current = false; }}
            >
                <title>Overview - click or drag to navigate</title>
                {data.nodes.map((n, i) => (
                    <rect key={i} className="mm-node"
                        x={sx(n.x)} y={sy(n.y)} width={n.w * scale} height={n.h * scale} rx={1} />
                ))}
                {/* Clamped to the drawn extent so a zoomed-out viewport (which can be many times
                    the graph's size) still reads as a bordered box hugging the edges, rather than
                    an off-screen rectangle leaving only a flat wash of fill behind. */}
                {(() => {
                    const vx0 = Math.max(data.viewport.x, minX);
                    const vy0 = Math.max(data.viewport.y, minY);
                    const vx1 = Math.min(data.viewport.x + data.viewport.w, maxX);
                    const vy1 = Math.min(data.viewport.y + data.viewport.h, maxY);
                    return (
                        <rect className="mm-view"
                            x={sx(vx0)} y={sy(vy0)}
                            width={Math.max(0, (vx1 - vx0) * scale)}
                            height={Math.max(0, (vy1 - vy0) * scale)} />
                    );
                })()}
            </svg>
        );
    }
    return <div className="minimap-wrap" ref={wrapRef}>{inner}</div>;
}

const LANE_COLORS = ['#3794ff', '#89d185', '#d18616', '#b180d7', '#cca700', '#4ec9b0', '#e2649a'];
/** Stable colour per group key, so a thread/chapter keeps its hue as nodes move. */
function laneColorFor(key: string): string {
    let hash = 0;
    for (let i = 0; i < key.length; i++) { hash = (hash * 31 + key.charCodeAt(i)) >>> 0; }
    return LANE_COLORS[hash % LANE_COLORS.length];
}

/**
 * Tinted grouping rectangles drawn behind the nodes - one per thread or per chapter. Rendered
 * through a portal into rete's transformed content holder, so the boxes are positioned in raw
 * GRAPH coordinates and rete's own pan/zoom transform carries them along with the nodes; there is
 * deliberately no per-frame screen-space math here. Toggled independently; thread lanes are solid,
 * chapter lanes dashed, so the two can overlap legibly.
 */
function SwimlaneOverlay(props: {
    getHandle: () => EditorHandle | null; by: 'thread' | 'chapter';
}): JSX.Element | null {
    const [, force] = useReducer((x: number) => x + 1, 0);
    useEffect(() => subscribeGeometryChange(() => force()), []);
    const handle = props.getHandle();
    if (!handle) { return null; }
    return createPortal(
        <>
            {handle.getGroupBounds(props.by).map(g => {
                const color = laneColorFor(props.by + ':' + g.key);
                return (
                    <div
                        key={g.key}
                        className={'swimlane swimlane-' + props.by}
                        style={{
                            left: g.x, top: g.y, width: g.w, height: g.h,
                            borderColor: color,
                            background: `color-mix(in srgb, ${color} 7%, transparent)`,
                        }}
                    >
                        <span className="swimlane-title" style={{ color }}>{g.title}</span>
                    </div>
                );
            })}
        </>,
        handle.getSwimlaneLayer()
    );
}

/** Wires the palette's Tactical category to `createTacticalAttachment` - no other UI reaches it. */
function TacticalCreateBar(props: {
    threads: string[];
    initialType: string | null;
    onCreate(threadUri: string, newName: string, value: 'land' | 'space', file: string): void;
    onClose(): void;
}): JSX.Element {
    const [name, setName] = useState('New_Tactical_Link');
    const [thread, setThread] = useState(props.threads[0] ?? '');
    const [value, setValue] = useState<'land' | 'space'>(
        props.initialType === 'STORY_SPACE_TACTICAL' ? 'space' : 'land');
    const [file, setFile] = useState('');

    const create = (): void => {
        if (!name.trim() || !thread || !file.trim()) { return; }
        props.onCreate(thread, name.trim(), value, file.trim());
    };

    return (
        <div className="toolbar">
            <span>New tactical link:</span>
            <input type="text" value={name} onChange={e => setName(e.target.value)}
                onKeyDown={e => { if (e.key === 'Enter') { create(); } }} />
            <select value={value} onChange={e => setValue(e.target.value as 'land' | 'space')} title="Battle type">
                <option value="land">Land</option>
                <option value="space">Space</option>
            </select>
            <input
                type="text" placeholder="Tactical plot manifest file…" value={file}
                onChange={e => setFile(e.target.value)}
                onKeyDown={e => { if (e.key === 'Enter') { create(); } }}
            />
            <select value={thread} onChange={e => setThread(e.target.value)} title="Thread file">
                {props.threads.length === 0 ? <option value="">(no thread files)</option> : null}
                {props.threads.map(t => <option key={t} value={t}>{baseName(t)}</option>)}
            </select>
            <button className="primary" onClick={create} disabled={!thread || !file.trim()}>Create</button>
            <button onClick={props.onClose}>Cancel</button>
        </div>
    );
}

/**
 * Drag source for node creation. Order matters: "New event" sits on top (set off by a divider),
 * then the Structure (AND/OR) group, then the long, collapsible Event-types and Rewards lists —
 * so the common actions aren't buried under 100+ type entries. Dropping New event / an Event type
 * on the canvas creates the node directly; dropping a Reward attaches it to the event node under
 * the cursor (see `onCanvasDrop`). Rewards default collapsed - you usually attach them onto a node
 * rather than pick one to create.
 */
/**
 * A CX-inspired colour family for a palette step, keyed heuristically off the type name (we have no
 * per-type art). The point is at-a-glance grouping - flags amber, event-control blue, combat red,
 * media purple, timing yellow - not an exact taxonomy.
 */
function stepColor(category: string, name: string | null): string {
    if (category === 'blank') { return 'var(--vscode-charts-foreground, #bbb)'; }
    if (category === 'andJunction' || category === 'orJunction') { return 'var(--vscode-charts-purple, #b180d7)'; }
    const n = (name ?? '').toUpperCase();
    if (/FLAG/.test(n)) { return 'var(--vscode-charts-orange, #d18616)'; }
    if (/TACTICAL|VICTORY|MISSION|LAND|SPACE|BATTLE|CONQUER|BOMBARD/.test(n)) { return 'var(--vscode-charts-red, #f14c4c)'; }
    if (/DIALOG|SPEECH|NOTIF|MOVIE|SOUND|MUSIC|CAMERA|SUBTITLE|TEXT/.test(n)) { return 'var(--vscode-charts-purple, #b180d7)'; }
    if (/ELAPSED|TIME|TIMER|CLOCK/.test(n)) { return 'var(--vscode-charts-yellow, #cca700)'; }
    if (/TRIGGER|RESET|DISABLE|ACTIVATE|ENABLE|EVENT|PLOT|ELEMENT/.test(n)) { return 'var(--vscode-charts-blue, #3794ff)'; }
    return category === 'reward' ? 'var(--vscode-charts-green, #89d185)' : 'var(--vscode-charts-blue, #3794ff)';
}

function NodePalette(props: { eventTypes: string[]; rewardTypes: string[] }): JSX.Element {
    const [search, setSearch] = useState('');
    // Collapse state per collapsible group; Rewards starts collapsed (it's the long one).
    const [collapsed, setCollapsed] = useState<Record<string, boolean>>({ Rewards: true });
    const q = search.trim().toLowerCase();
    const matches = (name: string): boolean => q === '' || name.toLowerCase().includes(q);

    const eventTypes = props.eventTypes.filter(matches);
    const rewardTypes = props.rewardTypes.filter(matches);
    // The structural actions (New event / AND / OR) always stay - the search only filters the types.
    const showBlank = true;
    const showAndJunction = true;
    const showOrJunction = true;

    const onDragStart = (e: DragEvent<HTMLDivElement>, drag: PaletteDrag): void => {
        e.dataTransfer.setData(PALETTE_DRAG_MIME, JSON.stringify(drag));
        e.dataTransfer.effectAllowed = 'copy';
    };

    // The whole tile is washed in its family colour - no glyph (they were all identical). Structural
    // tiles (New/AND/OR) keep a distinguishing glyph since their shapes actually differ.
    const fadedBg = (color: string): string =>
        `color-mix(in srgb, ${color} 20%, var(--vscode-editorWidget-background))`;

    const tile = (
        key: string, glyph: JSX.Element | null, label: string, drag: PaletteDrag, hint: string
    ): JSX.Element => {
        const color = stepColor(drag.category, drag.type);
        return (
            <div
                key={key} className="palette-tile" draggable
                style={{ background: fadedBg(color), borderColor: color }}
                onDragStart={e => onDragStart(e, drag)}
                title={`${label}\n${hint}`}
            >
                {glyph ? <span className="tile-glyph">{glyph}</span> : null}
                <span className="tile-label">{label}</span>
            </div>
        );
    };

    /**
     * A collapsible type group. Its items are sub-grouped by colour family, each family in its own
     * boxed grid (no family name - the colour is the label). An active search always expands.
     */
    const typeGroup = (
        label: string, category: 'trigger' | 'reward', items: string[], hint: string
    ): JSX.Element | null => {
        if (!items.length) { return null; }
        const isCollapsed = q === '' && collapsed[label];
        const families = new Map<string, string[]>();
        for (const t of items) {
            const c = stepColor(category, t);
            const list = families.get(c);
            if (list) { list.push(t); } else { families.set(c, [t]); }
        }
        return (
            <div className="palette-group">
                <div
                    className="palette-head toggle"
                    onClick={() => setCollapsed(c => ({ ...c, [label]: !c[label] }))}
                    title={isCollapsed ? `Expand ${label}` : `Collapse ${label}`}
                >
                    {isCollapsed ? '▸' : '▾'} {label}
                    <span className="palette-count">{items.length}</span>
                </div>
                {isCollapsed ? null : [...families.entries()].map(([color, names]) => (
                    <div key={color} className="tile-family">
                        <div className="tile-grid">
                            {names.map(t => tile(t, null, t, { category, type: t }, hint))}
                        </div>
                    </div>
                ))}
            </div>
        );
    };

    return (
        <div className="palette-scroll">
            <input
                type="text" placeholder="Search node types…" value={search}
                onChange={e => setSearch(e.target.value)}
            />
            {showBlank || showAndJunction || showOrJunction ? (
                <div className="palette-group palette-new">
                    <div className="tile-grid">
                        {showBlank ? tile('blank', <span className="codicon codicon-add" />, 'New event',
                            { category: 'blank', type: null },
                            'Drag onto the canvas to create a new untyped event, then edit it in place') : null}
                        {showAndJunction ? tile('and', <span className="junction-glyph">◯</span>, 'AND',
                            { category: 'andJunction', type: null },
                            'Drag onto the canvas, wire event outputs into it, then drag its output onto the event that should require all of them together') : null}
                        {showOrJunction ? tile('or', <span className="junction-glyph">◇</span>, 'OR',
                            { category: 'orJunction', type: null },
                            'Drag onto the canvas, wire event outputs into it, then drag its output onto the event that any one of them should arm') : null}
                    </div>
                </div>
            ) : null}
            {typeGroup('Event types', 'trigger', eventTypes, 'Drag onto the canvas to create an event')}
            {typeGroup('Rewards', 'reward', rewardTypes, 'Drag onto an event node to attach this reward to it')}
            {q !== '' && !eventTypes.length && !rewardTypes.length
                ? <p className="palette-empty">No node types match &ldquo;{search}&rdquo;.</p>
                : null}
        </div>
    );
}

const rootElement = document.getElementById('root');
if (rootElement) {
    createRoot(rootElement).render(<App />);
}

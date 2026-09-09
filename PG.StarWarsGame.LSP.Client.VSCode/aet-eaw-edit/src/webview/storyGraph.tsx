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
    useCallback, useEffect, useMemo, useReducer, useRef, useState,
} from 'react';
import {
    dockBodyCss, dockChromeCss, dockHeaderCss, dockOverviewCss, problemsPanelCss, rightDockCss,
    rotarySwitchCss,
} from './shared/dockChrome';
import { RotaryModeSwitch, type RotaryMode } from './shared/RotaryModeSwitch';
import { RightDock } from './shared/RightDock';
import { ProblemsPanel, type ProblemFilterControl } from './shared/ProblemsPanel';
import { filterProblems } from './shared/problemFilter';
import { ARRANGE_OPTIONS } from './storyGraph/arrangeOptions';
import { ClearFiltersButton } from './storyGraph/ClearFiltersButton';
import { FrameNotifier } from './storyGraph/frameNotifier';
import { canReuseStoredLayout } from './storyGraph/layoutReuse';
import { createLabelSizer, LINE_RATIO, wrapLabel } from './storyGraph/lodLabel';
import { needsFullMountForLayout, shouldShowOverview, shouldWindow } from './storyGraph/lodPolicy';
import { Extent, fitZoom } from './storyGraph/viewportFit';
import { booleanParamLabel, shortParamLabel } from './storyGraph/paramLabels';
import { paramRowSpecs } from './storyGraph/paramRows';
import { StagedRenames } from './storyGraph/stagedRenames';
import { optimisticEdit, PREVIEW_KINDS, STAGED_KINDS } from './staging';
import { useEdgeResize } from './useEdgeResize';
import { worstSeverity } from './loc/validateState';
import { createRoot } from 'react-dom/client';
import { ClassicPreset, GetSchemes, NodeEditor } from 'rete';
import { AreaExtensions, AreaPlugin } from 'rete-area-plugin';
import { AutoArrangePlugin, Presets as ArrangePresets } from 'rete-auto-arrange-plugin';
import { ConnectionPlugin, Presets as ConnectionPresets } from 'rete-connection-plugin';
import { Drag, Presets, ReactArea2D, ReactPlugin, RenderEmit } from 'rete-react-plugin';
import styled, { createGlobalStyle } from 'styled-components';

import {
    StoryDiagnosticDto, StoryGraphEdgeDto, StoryGraphNodeDto, StoryLayoutEntryDto,
    StoryParamOptionDto, StoryParamSchemaDto, StorySimStateDto,
} from '../protocol';

import { readPanelSize, writePanelSize } from './shared/panelLayout';
import { initPanelLayout } from './shared/panelLayoutBridge';
import { Button, IconButton } from './shared/Button';
import { DockSection } from './shared/DockSection';
import { colourResolver } from './shared/resolveColour';
import { SeverityTag } from './shared/SeverityTag';
import { tokensRootCss } from './shared/tokens';
import {
    JUNCTION_TOKEN, LANE_PALETTE, LIFECYCLE_TOKENS, UNKNOWN_LIFECYCLE_TOKEN,
    branchToken, laneToken,
} from './storyGraph/palette';

declare function acquireVsCodeApi(): { postMessage(message: unknown): void };
const vscode = acquireVsCodeApi();

// Reads the dock and drawer sizes the host seeded into the page, and reports
// every drag back to it. Must run before anything measures itself.
initPanelLayout(vscode);

// The wire shapes come from ../protocol - one declaration shared with the extension host that
// forwards them. The copy that used to sit here had already drifted from the host'"'"'s: it carried
// storyChapter and the host'"'"'s did not.

/**
 * The complete filter state this view holds.
 *
 * Distinct from the protocol'"'"'s FilterState, whose every field is optional: that one describes
 * what gets sent, and "no branch filter" travels as an absent field. Here every filter always has
 * a value, because every filter always has a control showing it.
 */
interface FilterState {
    nameFilter: string; branch: string; lifecycle: string; reachableFrom: string;
    /** Active_Plot or Suspended_Plot, as the faction manifest registers the thread. */
    plotState: string;
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
const pendingOptionRequests = new Map<number, (options: StoryParamOptionDto[]) => void>();

function fetchParamOptions(
    side: 'event' | 'reward', typeName: string, position: number, prefix: string,
): Promise<StoryParamOptionDto[]> {
    const requestId = ++optionRequestCounter;
    return new Promise(resolve => {
        pendingOptionRequests.set(requestId, resolve);
        vscode.postMessage({ type: 'paramOptions', requestId, side, typeName, position, prefix });
        window.setTimeout(() => {
            if (pendingOptionRequests.delete(requestId)) { resolve([]); }
        }, 3000);
    });
}

const EMPTY_FILTERS: FilterState = {
    nameFilter: '', branch: '', lifecycle: '', reachableFrom: '', plotState: '',
};

/** Event/reward type names flagged `untested` in the schema - set once, read during render. */
const untestedTypes = new Set<string>();

/** Event/reward type name → its param schema - set once from the 'schema' message; read by node bodies. */
const eventTypeParams = new Map<string, StoryParamSchemaDto[]>();
const rewardTypeParams = new Map<string, StoryParamSchemaDto[]>();

const EVENT_NODE_WIDTH = 280;
const EVENT_ROW_H = 22;
const EVENT_HEADER_H = 30;
// Slack added to the measured row height: the header's margin-bottom plus each section head's
// margin-top plus the body's own top/bottom padding aren't counted row-by-row, so without enough
// here the last field sits right on (and just barely clips against) the bottom border.
const EVENT_BODY_PAD = 26;

/** Above this node count, Event nodes start with Trigger/Reward collapsed (perf on big campaigns). */
const LARGE_GRAPH_NODE_COUNT = 60;

/**
 * How many frames to wait for the container to be laid out before giving up on measuring it.
 * ~1s at 60fps: long enough for a webview that is still settling, short enough that a genuinely
 * zero-sized panel does not hang the first render.
 */
const MEASURE_FRAME_BUDGET = 60;

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
 * VS Code's themed categorical chart palette - these track the active colour theme (and invert with
 * light/dark), so branch colours belong to the theme rather than being hard-coded hues. Branches
 * beyond the palette length reuse a colour; that's fine for the handful of branches a thread has.
 */
/** Stable themed chart colour per branch name - shared by the node glow and the edge glow. */
function branchColor(branch: string): string {
    return `var(${branchToken(branch)})`;
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
    setGraph(nodes: StoryGraphNodeDto[], edges: StoryGraphEdgeDto[], layout: StoryLayoutEntryDto[],
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
    /** Whether the graph is in windowed/LOD mode (big graph): the LOD overview is drawn and only the
     * visible window mounts into rete. The overview stays up at all zoom levels so edges (and off-
     * window nodes) remain visible; real nodes overlay the window when zoomed in. */
    isWindowed(): boolean;
    /** Paints the LOD overview (all nodes as coloured rects + all edges as lines) into a screen-space
     * canvas sized to the viewport, using the current pan/zoom transform. No-op clear when not
     * windowed (a small graph is fully mounted, so the real nodes are the view). */
    drawLodTo(canvas: HTMLCanvasElement): void;
    /** Bounding boxes (graph coords) of Event nodes grouped by thread or chapter - the swimlanes. */
    getGroupBounds(by: 'thread' | 'chapter'): {
        key: string; title: string; x: number; y: number; w: number; h: number;
    }[];
    /** Paints the enabled swimlanes (thread solid, chapter dashed) into a screen-space canvas behind
     * the nodes, using the current pan/zoom transform. */
    drawSwimlanesTo(canvas: HTMLCanvasElement, showThread: boolean, showChapter: boolean): void;
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

/** Notified (the minimap) when the viewport pans/zooms or a node moves. */
const areaChanged = new FrameNotifier();
const subscribeAreaChange = (cb: () => void): (() => void) => areaChanged.subscribe(cb);
const scheduleAreaChanged = (): void => areaChanged.schedule();

/**
 * Notified when node *geometry* changes (a node moved, or the graph was rebuilt) - deliberately
 * NOT on pan/zoom. The swimlane overlay lives inside rete's transformed content holder, so panning
 * moves it for free; recomputing its bounds per pan frame would walk every node for nothing.
 *
 * A second notifier rather than a second flag on the first: they fire on different events, and
 * that is the whole distinction being drawn here.
 */
const geometryChanged = new FrameNotifier();
const subscribeGeometryChange = (cb: () => void): (() => void) => geometryChanged.subscribe(cb);
const scheduleGeometryChanged = (): void => geometryChanged.schedule();

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

// See StagedRenames: a gesture reads the node's dto.label, which lags a staged rename until the
// preview lands, so every staged command is retargeted through this to the event's latest name.
const stagedRenames = new StagedRenames();

/** Whether there are unsaved staged changes - drives the dirty-exit prompt and Save button. */
function hasPendingChanges(): boolean {
    return pendingCommands.length > 0;
}

function clearPendingCommands(): void {
    pendingCommands.length = 0;
    stagedRenames.clear();
    onPendingChanged();
}

/** Applies a staged command to the local graph, queues it, and previews structural changes. */
function stageCommand(payload: Record<string, unknown>): void {
    // Retarget to the event's latest staged name (dto.label may still show a pre-rename name).
    if (typeof payload.eventName === 'string') {
        const resolved = stagedRenames.resolve(payload.eventName);
        if (resolved !== payload.eventName) { payload = { ...payload, eventName: resolved }; }
    }
    if (payload.kind === 'renameEvent' && typeof payload.newName === 'string') {
        stagedRenames.record(payload.eventName as string, payload.newName);
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

// Below this zoom level a large graph shows the cheap LOD overview (LodOverview) instead of mounting
// real rete nodes. Small graphs fit-to-screen at a higher zoom and never hit it, so they mount
// normally. Zooming past it mounts the visible window as real nodes. Tuned so real nodes appear
// while they're still ~90px wide on screen (EVENT_NODE_WIDTH 280 × 0.32); raise/lower to switch
// later/earlier.
const K_DETAIL = 0.32;
// LOD stages: below K_LABEL the overview is coloured rects only; from K_LABEL up the event title is
// drawn inside each rect (a cheap mid-detail stage, no rete mount); from K_DETAIL up the visible
// window mounts as real interactive nodes.
const K_LABEL = 0.12;

/** Reference text and size for measuring the LOD label font's average character advance. */
const LABEL_SAMPLE = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz_0123456789';
const LABEL_MEASURE_PX = 100;

/** Inset between a node's rect and its label text, in screen pixels. */
const LABEL_PAD = 4;

// Overview colour for a node: events by lifecycle (matching the node border + legend), junctions
// the colour a fired event takes.
function lodToken(dto: StoryGraphNodeDto): string {
    if (dto.kind !== 'Event') { return JUNCTION_TOKEN; }
    return LIFECYCLE_TOKENS[dto.lifecycle as keyof typeof LIFECYCLE_TOKENS] ?? UNKNOWN_LIFECYCLE_TOKEN;
}

/**
 * Overview colour for a node: its branch colour (matching the real node's glow) if it has a branch,
 * otherwise the lifecycle colour.
 *
 * A TOKEN, not a colour. It is stored on the model and resolved in the draw pass, so a theme switch
 * repaints the overview without the model being rebuilt - which is also why the hex mirror of the
 * branch palette this used to index is gone.
 */
function overviewToken(dto: StoryGraphNodeDto, branch: string | null): string {
    return branch ? branchToken(branch) : lodToken(dto);
}

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

    // GraphModel: authoritative geometry for EVERY node, independent of which nodes rete currently
    // has mounted. Minimap, swimlanes and save-layout read this (not area.nodeViews) so they stay
    // whole once node mounting is windowed. Synced by rebuildModel() after each full build/patch and
    // upserted per node on drag (the 'nodetranslated' pipe below).
    interface ModelNode { dto: StoryGraphNodeDto; x: number; y: number; w: number; h: number; colorToken: string; }
    const graphModel = new Map<string, ModelNode>();

    const rebuildModel = (): void => {
        graphModel.clear();
        for (const node of editor.getNodes()) {
            const view = area.nodeViews.get(node.id);
            graphModel.set(node.id, {
                dto: node.dto, w: node.width, h: node.height,
                x: view?.position.x ?? 0, y: view?.position.y ?? 0,
                colorToken: overviewToken(node.dto, node.branchGlow),
            });
        }
    };

    // Bounding boxes (graph coords) of Event nodes grouped by thread or chapter - the swimlanes.
    // Reads the model, so it's complete even when only part of the graph is mounted.
    const computeGroupBounds = (
        by: 'thread' | 'chapter'
    ): { key: string; title: string; x: number; y: number; w: number; h: number }[] => {
        const groups = new Map<string, { title: string; minX: number; minY: number; maxX: number; maxY: number }>();
        for (const m of graphModel.values()) {
            if (m.dto.kind !== 'Event') { continue; }
            let key: string;
            let title: string;
            if (by === 'thread') {
                if (!m.dto.threadUri) { continue; }
                key = m.dto.threadUri;
                title = baseName(m.dto.threadUri);
            } else {
                if (typeof m.dto.storyChapter !== 'number') { continue; }
                key = String(m.dto.storyChapter);
                title = `Chapter ${m.dto.storyChapter}`;
            }
            const x0 = m.x, y0 = m.y, x1 = m.x + m.w, y1 = m.y + m.h;
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
    };

    const saveAllPositions = (): void => {
        const entries: StoryLayoutEntryDto[] = [];
        for (const m of graphModel.values()) {
            if (m.dto.kind !== 'Event') { continue; }
            entries.push({
                threadUri: m.dto.threadUri ?? '',
                eventName: m.dto.label,
                x: m.x,
                y: m.y,
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
        // Zoom always reconciles - crossing K_DETAIL swaps between the overview and real nodes for
        // every graph, however small. Panning only matters when windowed, where it changes which
        // screenful is mounted; an unwindowed graph already has everything mounted.
        if (context.type === 'zoomed' || (context.type === 'translated' && windowed)) {
            scheduleReconcile();
        }
        // Swimlane bounds only depend on where the nodes are, not on the viewport.
        if (context.type === 'nodetranslated') {
            // Keep the model's position for this node current (drags, and build-time translates).
            const id = (context.data as { id: string }).id;
            const node = editor.getNode(id);
            const view = area.nodeViews.get(id);
            if (node && view) {
                graphModel.set(id, {
                    dto: node.dto, w: node.width, h: node.height,
                    x: view.position.x, y: view.position.y,
                    colorToken: overviewToken(node.dto, node.branchGlow),
                });
            }
            scheduleGeometryChanged();
        }
        return context;
    });

    // Original (static analysis) lifecycles, so ending a simulation restores the pre-sim view.
    const staticLifecycles = new Map<string, string | null | undefined>();

    // Keyed on the thread's URI, not its base name: two threads can share a file name, and the
    // server has to be able to re-derive this from the model - which it cannot do from a name the
    // webview shortened.
    const layoutKey = (dto: StoryGraphNodeDto): string =>
        `${dto.threadUri ?? ''} ${dto.label}`.toLowerCase();

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

    // ── Level-of-detail (LOD) virtualization ──────────────────────────────────────────────────────
    // A large graph is not mounted into rete on open; the cheap LodOverview (drawn from graphModel)
    // stands in until the user zooms in past K_DETAIL, at which point the nodes mount.
    let windowed = false;   // big graph → LOD overview + viewport windowing (only visible nodes mount)
    let lodActive = false;  // currently showing the overview (zoomed out, k < K_DETAIL)
    let lastEdges: StoryGraphEdgeDto[] = [];
    // Which model nodes / edges are currently mounted into rete (the visible window). Only meaningful
    // while `windowed`; the minimap always reads the full graphModel, not this.
    const mountedIds = new Set<string>();
    const mountedConnKeys = new Set<string>();

    // A node's rete size, computed without mounting it (mirrors StoryNode.applyDto).
    const modelSizeFor = (dto: StoryGraphNodeDto): { w: number; h: number } => {
        if (dto.kind === 'AndJunction' || dto.kind === 'OrJunction'
            || dto.kind === 'StagingAnd' || dto.kind === 'StagingOr') { return { w: 48, h: 48 }; }
        if (dto.kind === 'Event') { return { w: EVENT_NODE_WIDTH, h: estimateEventNodeHeight(dto) }; }
        return { w: Math.max(140, Math.min(dto.label.length, 30) * 6.5 + 32), h: 40 };
    };

    // placeNewNode's midpoint logic, but reading positions from graphModel (nodes aren't mounted).
    // Returns null when none of this node's neighbours are placed yet, so the caller can try again in
    // a later pass instead of dumping it at (0,0).
    const modelPlaceNode = (dto: StoryGraphNodeDto, edges: StoryGraphEdgeDto[]): { x: number; y: number } | null => {
        const sources: { x: number; y: number }[] = [];
        const targets: { x: number; y: number }[] = [];
        for (const edge of edges) {
            if (edge.toId === dto.id) { const m = graphModel.get(edge.fromId); if (m) { sources.push({ x: m.x, y: m.y }); } }
            else if (edge.fromId === dto.id) { const m = graphModel.get(edge.toId); if (m) { targets.push({ x: m.x, y: m.y }); } }
        }
        if (sources.length && targets.length) { const s = average(sources); const t = average(targets); return { x: (s.x + t.x) / 2, y: (s.y + t.y) / 2 }; }
        if (sources.length) { const s = average(sources); return { x: s.x + 220, y: s.y }; }
        if (targets.length) { const t = average(targets); return { x: t.x - 220, y: t.y }; }
        return null;
    };

    // Populate graphModel from stored positions without mounting anything. Returns false (→ slow
    // elk path) when any event lacks a saved position (first-ever open of this campaign).
    const buildModelFromLayout = (
        nodes: StoryGraphNodeDto[], edges: StoryGraphEdgeDto[], layout: StoryLayoutEntryDto[]
    ): boolean => {
        const stored = new Map(layout.map(e => [`${e.threadUri} ${e.eventName}`.toLowerCase(), e]));
        const events = nodes.filter(n => n.kind === 'Event');
        if (events.length === 0 || !canReuseStoredLayout(events.map(layoutKey), new Set(stored.keys()))) {
            return false;
        }
        graphModel.clear();
        const branchByEvent = branchIndex(nodes);
        const colorFor = (dto: StoryGraphNodeDto): string => overviewToken(dto, branchOfNodeId(dto.id, branchByEvent));
        for (const dto of events) {
            // An event with no stored entry - newly added since the layout was written - is left
            // for the placement pass below rather than discarding everyone else's positions.
            const e = stored.get(layoutKey(dto));
            if (e === undefined) { continue; }
            const { w, h } = modelSizeFor(dto);
            graphModel.set(dto.id, { dto, x: e.x, y: e.y, w, h, colorToken: colorFor(dto) });
        }
        // Everything still unplaced - junctions, and events added since the layout was saved - sits
        // at the midpoint of its neighbours. Some connect only to OTHER unplaced nodes, so iterate
        // until those chains resolve; anything left over (a cycle or an orphan) lands at the graph
        // centre - never (0,0), which would stretch the minimap's extent and leave an empty region
        // you can accidentally pan to.
        const junctions = nodes.filter(n => !graphModel.has(n.id));
        for (let pass = 0; pass < 6; pass++) {
            let changed = false;
            for (const dto of junctions) {
                if (graphModel.has(dto.id)) { continue; }
                const pos = modelPlaceNode(dto, edges);
                if (pos) { graphModel.set(dto.id, { dto, x: pos.x, y: pos.y, ...modelSizeFor(dto), colorToken: colorFor(dto) }); changed = true; }
            }
            if (!changed) { break; }
        }
        if (junctions.some(dto => !graphModel.has(dto.id))) {
            let cx = 0, cy = 0, n = 0;
            for (const m of graphModel.values()) { cx += m.x + m.w / 2; cy += m.y + m.h / 2; n += 1; }
            cx = n ? cx / n : 0; cy = n ? cy / n : 0;
            for (const dto of junctions) {
                if (!graphModel.has(dto.id)) { graphModel.set(dto.id, { dto, x: cx, y: cy, ...modelSizeFor(dto), colorToken: colorFor(dto) }); }
            }
        }
        return true;
    };

    // Visible graph-coordinate rectangle, expanded by `margin` × its size so nodes just off-screen
    // are already mounted before they scroll in.
    const viewportRect = (margin: number): { minX: number; minY: number; maxX: number; maxY: number } => {
        const { k, x, y } = area.area.transform;
        const w = container.clientWidth, h = container.clientHeight;
        // An unmeasured container (or a zero zoom) would give a zero-area - or NaN - rectangle, and
        // then nothing is "in window" and the graph mounts nothing at all. Cull nothing instead:
        // mounting too much is a performance problem, mounting nothing looks like a broken editor.
        if (!(w > 0) || !(h > 0) || !(k > 0)) {
            return { minX: -Infinity, minY: -Infinity, maxX: Infinity, maxY: Infinity };
        }
        const minX = -x / k, minY = -y / k, maxX = (w - x) / k, maxY = (h - y) / k;
        const mx = (maxX - minX) * margin, my = (maxY - minY) * margin;
        return { minX: minX - mx, minY: minY - my, maxX: maxX + mx, maxY: maxY + my };
    };

    const inWindow = (m: ModelNode, r: { minX: number; minY: number; maxX: number; maxY: number }): boolean =>
        m.x <= r.maxX && m.x + m.w >= r.minX && m.y <= r.maxY && m.y + m.h >= r.minY;

    // Mount a set of model nodes into rete (+ any edge whose BOTH endpoints are now mounted). Runs
    // under applyingServerGraph so the View-mode drag-freeze and the connection-staging pipe are
    // bypassed - this is view culling, not user editing.
    const mountNodes = async (ids: Iterable<string>): Promise<void> => {
        const fresh = [...ids].filter(id => !mountedIds.has(id) && graphModel.has(id));
        if (fresh.length === 0) { return; }
        const wasApplying = applyingServerGraph;
        applyingServerGraph = true;
        try {
            const branches = branchIndex([...graphModel.values()].map(m => m.dto));
            const hasIn = new Set(lastEdges.map(e => e.toId));
            const hasOut = new Set(lastEdges.map(e => e.fromId));
            const created = fresh.map(id => {
                const m = graphModel.get(id)!;
                const node = new StoryNode(m.dto, hasIn.has(id), hasOut.has(id));
                node.branchGlow = branchOfNodeId(id, branches);
                mountedIds.add(id);
                return node;
            });
            await Promise.all(created.map(n => editor.addNode(n)));
            await Promise.all(created.map(n => area.translate(n.id, {
                x: graphModel.get(n.id)!.x, y: graphModel.get(n.id)!.y,
            })));
            // Real rete connections only for the small-graph full mount (windowed=false), where every
            // endpoint is present. In windowed mode the LOD canvas draws ALL edges (socket-anchored
            // beziers) consistently, so there's no per-window connection churn or mixed edge styles.
            if (!windowed) {
                const conns: StoryConnection[] = [];
                for (const edge of lastEdges) {
                    if (!mountedIds.has(edge.fromId) || !mountedIds.has(edge.toId)) { continue; }
                    const key = connectionKey(edge.fromId, edge.toId, edge.kind);
                    if (mountedConnKeys.has(key)) { continue; }
                    const s = editor.getNode(edge.fromId), t = editor.getNode(edge.toId);
                    if (!s || !t) { continue; }
                    mountedConnKeys.add(key);
                    conns.push(new StoryConnection(s, t, edge.kind,
                        edgeBranchFrom(edge.fromId, edge.toId, edge.kind, branches)));
                }
                await Promise.all(conns.map(c => editor.addConnection(c)));
            }
        } finally {
            applyingServerGraph = wasApplying;
        }
    };

    const unmountNodes = async (ids: Iterable<string>): Promise<void> => {
        const present = [...ids].filter(id => mountedIds.has(id));
        if (present.length === 0) { return; }
        const wasApplying = applyingServerGraph;
        applyingServerGraph = true;
        try {
            // Batched, deliberately - the mirror of mountNodes.
            //
            // This used to remove one node at a time, and each `await` let rete re-render before
            // the next. Dropping a whole large campaign to the overview then played out node by
            // node: parts of the graph turned into LOD rectangles while the rest were still real
            // nodes, which is the stutter you see on a first open. Worse, the per-node version
            // rescanned every connection and every edge for each node - O(nodes x edges) on the
            // one path where both are largest.
            const removing = new Set(present);

            const doomed = editor.getConnections()
                .filter(c => removing.has(c.source) || removing.has(c.target));
            await Promise.all(doomed.map(c => editor.removeConnection(c.id)));

            // One pass over the edges rather than one pass per node.
            for (const edge of lastEdges) {
                if (removing.has(edge.fromId) || removing.has(edge.toId)) {
                    mountedConnKeys.delete(connectionKey(edge.fromId, edge.toId, edge.kind));
                }
            }

            await Promise.all(present.map(id => editor.removeNode(id)));
            for (const id of present) { mountedIds.delete(id); }
        } finally {
            applyingServerGraph = wasApplying;
        }
    };

    const mountAllFromModel = async (): Promise<void> => { await mountNodes(graphModel.keys()); };

    // Reconcile the mounted set to the current viewport (windowed mode only): zoomed out past
    // K_DETAIL → unmount everything and show the overview; zoomed in → mount just the visible
    // screenful (+margin) and unmount what scrolled away. Coalesced via scheduleReconcile.
    let reconciling = false;
    let reconcilePending = false;
    /**
     * Brings the mounted set and the overview into line with the current zoom.
     *
     * Runs for EVERY graph, not just windowed ones. The overview is a zoom decision - below
     * K_DETAIL a real node is an unreadable smudge whatever the graph's size - while `windowed`
     * only decides whether the detailed view mounts the visible screenful or all of it.
     */
    const reconcileWindow = async (): Promise<void> => {
        if (reconciling) { reconcilePending = true; return; }
        reconciling = true;
        try {
            do {
                reconcilePending = false;
                if (shouldShowOverview(area.area.transform.k, K_DETAIL)) {
                    // Unmount even for a small graph: the overview canvas sits BEHIND the nodes, so
                    // leaving them mounted would draw both, with shrunken real nodes on top of it.
                    if (mountedIds.size > 0) { await unmountNodes([...mountedIds]); }
                    // Toggle the overview on. It re-renders on geometryChange, so fire that ONLY on the
                    // transition, never per frame - otherwise zooming rebuilds the whole SVG each frame.
                    if (!lodActive) { lodActive = true; scheduleGeometryChanged(); }
                } else {
                    if (lodActive) { lodActive = false; scheduleGeometryChanged(); }
                    const want = new Set<string>();
                    if (windowed) {
                        const r = viewportRect(0.5);
                        for (const [id, m] of graphModel) { if (inWindow(m, r)) { want.add(id); } }
                    } else {
                        for (const id of graphModel.keys()) { want.add(id); }
                    }
                    await unmountNodes([...mountedIds].filter(id => !want.has(id)));
                    await mountNodes([...want].filter(id => !mountedIds.has(id)));
                }
            } while (reconcilePending);
        } finally {
            reconciling = false;
            scheduleAreaChanged(); // redraw the LOD canvas against the updated mounted set
        }
    };

    let reconcileScheduled = false;
    const scheduleReconcile = (): void => {
        if (reconcileScheduled) { return; }
        reconcileScheduled = true;
        requestAnimationFrame(() => { reconcileScheduled = false; void reconcileWindow(); });
    };

    // Fit the viewport to the whole model (mirrors AreaExtensions.zoomAt, which needs mounted nodes).
    /**
     * Centres the model in the viewport at the zoom that fits it.
     *
     * Returns whether it actually fitted. False means the container had no measurable size, and
     * the caller must NOT read `transform.k` to decide anything: an unmeasured container used to
     * yield k = 0, which reads as "enormous graph" and sent the whole thing down the windowed
     * branch, mounting nothing.
     */
    const fitModel = async (): Promise<boolean> => {
        if (graphModel.size === 0) { return false; }
        let minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity;
        for (const m of graphModel.values()) {
            minX = Math.min(minX, m.x); minY = Math.min(minY, m.y);
            maxX = Math.max(maxX, m.x + m.w); maxY = Math.max(maxY, m.y + m.h);
        }
        const cx = (minX + maxX) / 2, cy = (minY + maxY) / 2;

        const view = await measuredView();
        if (view === null) { return false; }

        const k = fitZoom(view, { width: maxX - minX, height: maxY - minY });
        if (k === null) { return false; }

        area.area.transform.x = view.width / 2 - cx * k;
        area.area.transform.y = view.height / 2 - cy * k;
        await area.area.zoom(k, 0, 0);
        return true;
    };

    /**
     * The container's size, waiting a few frames for layout if it has not happened yet.
     *
     * A webview that is still laying out reports 0x0, and the graph can be handed its data before
     * that settles. Bounded rather than open-ended: if the panel really is zero-sized (hidden, or
     * collapsed to nothing) we give up and let the caller fall back, instead of never rendering.
     */
    const measuredView = async (): Promise<Extent | null> => {
        for (let frame = 0; frame < MEASURE_FRAME_BUDGET; frame++) {
            const width = container.clientWidth, height = container.clientHeight;
            if (width > 0 && height > 0) { return { width, height }; }
            await new Promise<void>(resolve => requestAnimationFrame(() => resolve()));
        }
        return null;
    };

    // Rebuild the model from a fresh server graph WITHOUT refitting: existing nodes keep their
    // position, new nodes are placed (stored/drop position, else midpoint of neighbours). Used for a
    // windowed update (preview / push) so editing a big graph never has to mount the whole thing.
    const rebuildModelFromGraph = (
        nodes: StoryGraphNodeDto[], edges: StoryGraphEdgeDto[], layout: StoryLayoutEntryDto[]
    ): void => {
        const stored = new Map(layout.map(e => [`${e.threadUri} ${e.eventName}`.toLowerCase(), e]));
        const oldPos = new Map<string, { x: number; y: number }>();
        for (const [id, m] of graphModel) { oldPos.set(id, { x: m.x, y: m.y }); }
        graphModel.clear();
        const branchByEvent = branchIndex(nodes);
        const colorFor = (dto: StoryGraphNodeDto): string => overviewToken(dto, branchOfNodeId(dto.id, branchByEvent));
        const rest: StoryGraphNodeDto[] = [];
        let placedFromDrop = false;
        for (const dto of nodes) {
            const old = oldPos.get(dto.id);
            const key = dto.kind === 'Event' ? layoutKey(dto) : null;
            const s = key ? stored.get(key) : undefined;
            const drop = key ? pendingDropPositions.get(key) : undefined;
            // A junction born from a staging-node commit lands where the staging node stood.
            const owner = dto.kind === 'AndJunction' ? andJunctionId.exec(dto.id)?.[1]
                : dto.kind === 'OrJunction' && dto.id.endsWith('#or') ? dto.id.slice(0, -'#or'.length)
                : undefined;
            const je = owner ? pendingJunctionPositions.get(owner) : undefined;
            const jpos = je?.kind === dto.kind ? { x: je.x, y: je.y } : undefined;
            const pos = old ?? drop ?? jpos ?? (s ? { x: s.x, y: s.y } : null);
            if (pos) {
                graphModel.set(dto.id, { dto, x: pos.x, y: pos.y, ...modelSizeFor(dto), colorToken: colorFor(dto) });
                if (drop && key) { pendingDropPositions.delete(key); placedFromDrop = true; }
                if (jpos && owner) { pendingJunctionPositions.delete(owner); }
            } else { rest.push(dto); } // new node with no known spot - placed below from its neighbours
        }
        for (let pass = 0; pass < 6; pass++) {
            let changed = false;
            for (const dto of rest) {
                if (graphModel.has(dto.id)) { continue; }
                const pos = modelPlaceNode(dto, edges);
                if (pos) { graphModel.set(dto.id, { dto, x: pos.x, y: pos.y, ...modelSizeFor(dto), colorToken: colorFor(dto) }); changed = true; }
            }
            if (!changed) { break; }
        }
        if (rest.some(dto => !graphModel.has(dto.id))) {
            let cx = 0, cy = 0, n = 0;
            for (const m of graphModel.values()) { cx += m.x + m.w / 2; cy += m.y + m.h / 2; n += 1; }
            cx = n ? cx / n : 0; cy = n ? cy / n : 0;
            for (const dto of rest) {
                if (!graphModel.has(dto.id)) { graphModel.set(dto.id, { dto, x: cx, y: cy, ...modelSizeFor(dto), colorToken: colorFor(dto) }); }
            }
        }
        if (placedFromDrop) { saveAllPositions(); } // persist the drop, like patch() does
    };

    const windowedUpdate = async (
        nodes: StoryGraphNodeDto[], edges: StoryGraphEdgeDto[], layout: StoryLayoutEntryDto[]
    ): Promise<void> => {
        rebuildModelFromGraph(nodes, edges, layout);
        lastEdges = edges;
        await reconcileWindow(); // mounts/unmounts to the viewport; drops nodes no longer in the model
        reapplyStagedCommands(); // keep optimistic Edit-mode edits, exactly as patch() does
        scheduleGeometryChanged();
        scheduleAreaChanged();
    };

    const buildFull = async (
        nodes: StoryGraphNodeDto[], edges: StoryGraphEdgeDto[], layout: StoryLayoutEntryDto[]
    ): Promise<void> => {
        await editor.clear();
        graphModel.clear();
        mountedIds.clear();
        mountedConnKeys.clear();
        pendingJunctionPositions.clear();
        lastEdges = edges;

        if (buildModelFromLayout(nodes, edges, layout)) {
            // Stored layout exists → no elk. Fit it, then let cost decide whether to window and
            // zoom decide whether to draw the overview. A failed fit only means the viewport was
            // never measured; `reconcileWindow` reads the zoom itself, so nothing here depends on
            // a fabricated one.
            await fitModel();
            windowed = shouldWindow(graphModel.size);
            await reconcileWindow();
            if (!windowed && !lodActive) { rebuildModel(); }
            scheduleGeometryChanged();
            scheduleAreaChanged();
            return;
        }

        // First-ever open (no stored layout): mount everything and run elk once, then persist so
        // every later open takes the fast/LOD path above.
        windowed = false;
        lodActive = false;
        const branches = branchIndex(nodes);
        const hasIn = new Set(edges.map(e => e.toId));
        const hasOut = new Set(edges.map(e => e.fromId));
        const byId = new Map<string, StoryNode>();
        const created = nodes.map(dto => {
            const node = new StoryNode(dto, hasIn.has(dto.id), hasOut.has(dto.id));
            node.branchGlow = branchOfNodeId(dto.id, branches);
            byId.set(dto.id, node);
            return node;
        });
        await Promise.all(created.map(n => editor.addNode(n)));
        const conns = edges
            .map(edge => {
                const source = byId.get(edge.fromId);
                const target = byId.get(edge.toId);
                return source && target
                    ? new StoryConnection(source, target, edge.kind,
                        edgeBranchFrom(edge.fromId, edge.toId, edge.kind, branches))
                    : null;
            })
            .filter((c): c is StoryConnection => c !== null);
        await Promise.all(conns.map(c => editor.addConnection(c)));

        // This path adds nodes to the editor directly rather than through mountNodes, so record
        // what is now mounted. Without this the bookkeeping says "nothing is mounted" while the
        // editor holds the entire campaign, and the later teardown finds nothing to remove: the
        // overview is switched on OVER a full set of real nodes, which is both of them drawn at
        // once and the stutter that comes with it.
        for (const node of created) { mountedIds.add(node.id); }
        for (const edge of edges) {
            if (byId.has(edge.fromId) && byId.has(edge.toId)) {
                mountedConnKeys.add(connectionKey(edge.fromId, edge.toId, edge.kind));
            }
        }

        await arrange.layout({ options: ARRANGE_OPTIONS });
        rebuildModel();
        saveAllPositions(); // persist so the next open takes the fast path
        await AreaExtensions.zoomAt(area, editor.getNodes());

        // Elk needs every node mounted to lay them out, so this path necessarily starts unwindowed.
        // Now that positions exist, adopt the same cost decision every later open makes - otherwise
        // a first-ever open of a large campaign stays fully mounted for the whole session, which is
        // exactly when it is slowest and most noticeable.
        windowed = shouldWindow(graphModel.size);
        await reconcileWindow();
    };

    /** Reconciles the live graph against the server's - no re-layout, viewport untouched. */
    const patch = async (
        nodes: StoryGraphNodeDto[], edges: StoryGraphEdgeDto[], layout: StoryLayoutEntryDto[]
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
        const stored = new Map(layout.map(e => [`${e.threadUri} ${e.eventName}`.toLowerCase(), e]));
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
        rebuildModel();
    };

    return {
        setGraph(
            nodes: StoryGraphNodeDto[], edges: StoryGraphEdgeDto[], layout: StoryLayoutEntryDto[],
            full: boolean
        ): Promise<void> {
            const run = async (): Promise<void> => {
                staticLifecycles.clear();
                for (const dto of nodes) { staticLifecycles.set(dto.id, dto.lifecycle); }
                applyingServerGraph = true;
                try {
                    // patch() reconciles the FULL server graph against the mounted nodes - it assumes
                    // everything is mounted, so it must never run while windowed. A windowed update
                    // rebuilds the model and reconciles the visible window instead (no refit).
                    if (full || editor.getNodes().length === 0) {
                        await buildFull(nodes, edges, layout);
                    } else if (windowed) {
                        await windowedUpdate(nodes, edges, layout);
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
            // Fit the whole MODEL whenever the mounted set is not the whole graph - which is any
            // windowed graph, and ALSO any graph at all while zoomed out past K_DETAIL, where
            // reconcileWindow has unmounted everything to show the overview. Keying on `windowed`
            // alone left `zoomAt` fitting an empty node list on a small graph, which fits nothing.
            const mounted = editor.getNodes();

            if (windowed || mounted.length === 0) { void fitModel(); }
            else { void AreaExtensions.zoomAt(area, mounted); }
        },
        autoArrange(): Promise<void> {
            const run = async (): Promise<void> => {
                // nodetranslate is frozen outside Edit mode for USER gestures - the arrange
                // plugin's translations must pass, same as during setGraph.
                applyingServerGraph = true;
                const wasWindowed = windowed;
                // Keyed on the MOUNTED set, never on `windowed` - see needsFullMountForLayout.
                // Zooming out past K_DETAIL unmounts every graph, small ones included, so this was
                // also true of an unwindowed graph showing the overview: elk arranged an empty
                // editor, rebuildModel read that same empty editor, and the model - which is what
                // the overview, the minimap and the lanes all draw - was cleared. The graph
                // vanished until the next full rebuild put it back.
                const needsMount = needsFullMountForLayout(mountedIds.size, graphModel.size);
                try {
                    if (wasWindowed) {
                        // A windowed mount deliberately carries NO rete connections - the LOD canvas
                        // draws the edges instead - and elk lays out a graph, not a bag of boxes.
                        // Dropping the window first is what makes the re-mount add them: mountNodes
                        // skips ids it already holds, so a window that happened to cover the whole
                        // graph would otherwise re-enter with nothing fresh, and elk would flow
                        // sixty unconnected nodes into a grid and persist that as the layout.
                        await unmountNodes([...mountedIds]);
                        mountedConnKeys.clear();
                        windowed = false;
                        await mountAllFromModel();
                    } else if (needsMount) {
                        // elk lays out MOUNTED nodes + their connections, so mount the whole graph
                        // first (windowed is already false here, so mountNodes adds them).
                        await mountAllFromModel();
                    }
                    await arrange.layout({ options: ARRANGE_OPTIONS });
                    rebuildModel();      // capture the recomputed layout into the model
                    saveAllPositions();  // ...and persist it (replaces the saved one)
                } finally {
                    applyingServerGraph = false;
                }
                if (wasWindowed) {
                    // Back to windowed: drop the full mount and re-window to the re-fit viewport.
                    windowed = true;
                    await unmountNodes([...mountedIds]);
                    mountedConnKeys.clear();
                    await fitModel();
                    await reconcileWindow();
                } else {
                    await AreaExtensions.zoomAt(area, editor.getNodes());
                    // A graph mounted only for the layout is still carrying whatever the zoom says
                    // it should not: settle it here rather than waiting on the 'zoomed' pipe, so
                    // the overview and the mounted nodes are never both up when this returns.
                    if (needsMount) { await reconcileWindow(); }
                }
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
            // Flash the node so it's findable in a large graph. Toggle a class on the node's DOM
            // wrapper (a forced reflow restarts the animation if the same node is re-jumped); the
            // keyframes' box-shadow transiently overrides the branch-glow inline box-shadow.
            const flash = (): void => {
                const element = area.nodeViews.get(nodeId)?.element as HTMLElement | undefined;
                if (element) {
                    element.classList.remove('story-flash');
                    void element.offsetWidth;
                    element.classList.add('story-flash');
                    window.setTimeout(() => element.classList.remove('story-flash'), 1600);
                }
            };
            // Keyed on whether the node is MOUNTED, never on whether the graph is windowed.
            //
            // `windowed` is a node-COUNT decision (see shouldWindow), while mounting is a ZOOM one:
            // reconcileWindow unmounts everything below K_DETAIL for every graph size, small ones
            // included, because the overview canvas sits behind the nodes. Unmounting calls
            // editor.removeNode, so on any graph too small to be windowed - a filtered one, most
            // often - getNode returned undefined while zoomed out and the jump silently did
            // nothing. The model path below already handles a node that is not mounted, at any size.
            const mounted = editor.getNode(nodeId);

            if (mounted !== undefined && area.nodeViews.has(nodeId)) {
                void AreaExtensions.zoomAt(area, [mounted]);
                flash();
                return;
            }

            // Not mounted. Centre the viewport on its model position at a zoom past K_DETAIL so the
            // reconcile mounts it, then flash once it exists.
            const m = graphModel.get(nodeId);
            if (!m) { return; }
            const cx = m.x + m.w / 2, cy = m.y + m.h / 2;
            const w = container.clientWidth, h = container.clientHeight;
            const k = Math.max(area.area.transform.k, K_DETAIL + 0.15);
            area.area.transform.x = w / 2 - cx * k;
            area.area.transform.y = h / 2 - cy * k;
            void area.area.zoom(k, 0, 0).then(() => reconcileWindow()).then(() => flash());
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
            const nodes = [...graphModel.values()].map(m => ({ x: m.x, y: m.y, w: m.w, h: m.h }));
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
        isWindowed(): boolean { return windowed; },
        drawLodTo(canvas: HTMLCanvasElement): void {
            const ctx = canvas.getContext('2d');
            if (!ctx) { return; }
            // Once per frame, never per node: it reads the computed style, and the whole point of
            // this overview is that a large campaign stays cheap to draw. Per frame rather than
            // cached across frames is what lets a theme switch land without anything listening.
            const colour = colourResolver(container);
            const w = container.clientWidth, h = container.clientHeight;
            const dpr = window.devicePixelRatio || 1;
            if (canvas.width !== Math.round(w * dpr) || canvas.height !== Math.round(h * dpr)) {
                canvas.width = Math.round(w * dpr);
                canvas.height = Math.round(h * dpr);
            }
            ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
            ctx.clearRect(0, 0, w, h);
            // Two reasons to paint: a windowed graph always needs stand-ins for the nodes it has
            // not mounted and the edges rete is not drawing, and ANY graph zoomed past K_DETAIL is
            // showing the overview instead of real nodes. This used to test `windowed` alone, on
            // the assumption that a small graph is always fully mounted - no longer true now that
            // zooming out unmounts one, which left a blank canvas rather than an overview.
            if (!windowed && !lodActive) { return; }
            const { k, x, y } = area.area.transform;
            // Edges: culled to the viewport, and cheap straight lines when zoomed out but socket-
            // anchored beziers once zoomed in (where the curve actually reads). Both go output (right)
            // → input (left) to match the left-to-right layout. Drawn for ALL edges (windowed mode
            // mounts no rete connections), so connections are consistent regardless of what's on screen.
            const bezier = k >= K_LABEL;
            ctx.globalAlpha = 0.4;
            ctx.strokeStyle = colour('--colour-muted');
            ctx.lineWidth = 1;
            ctx.beginPath();
            for (const e of lastEdges) {
                const a = graphModel.get(e.fromId), b = graphModel.get(e.toId);
                if (!a || !b) { continue; }
                const x1 = (a.x + a.w) * k + x, y1 = (a.y + a.h / 2) * k + y;
                const x2 = b.x * k + x, y2 = (b.y + b.h / 2) * k + y;
                if ((x1 < 0 && x2 < 0) || (x1 > w && x2 > w)
                    || (y1 < 0 && y2 < 0) || (y1 > h && y2 > h)) { continue; } // fully off one side
                ctx.moveTo(x1, y1);
                if (bezier) {
                    const dx = Math.max(20, Math.abs(x2 - x1) * 0.4);
                    ctx.bezierCurveTo(x1 + dx, y1, x2 - dx, y2, x2, y2);
                } else {
                    ctx.lineTo(x2, y2);
                }
            }
            ctx.stroke();
            // Nodes: coloured rects; from K_LABEL up the event title is drawn inside too (mid stage).
            // Off-screen nodes skipped. Font/colour set once, labels truncated (no per-node clip).
            const showLabels = k >= K_LABEL;
            let advancePerPx = 0;
            let sizeFor: ((width: number, height: number) => { fontPx: number; maxLines: number }) | null = null;
            let lastFontPx = 0;
            // Resolved with the rest of the frame's colours rather than re-read here: it was the
            // one canvas colour that already followed the theme, and now they all do.
            const labelColor = colour('--colour-editor-ink');
            if (showLabels) {
                // Measure the font once per frame instead of assuming 0.55em per character. The
                // guess is what made the truncation land in the wrong place, so most names were
                // sliced down to their first few characters.
                ctx.font = `${LABEL_MEASURE_PX}px sans-serif`;
                advancePerPx = ctx.measureText(LABEL_SAMPLE).width
                    / LABEL_SAMPLE.length / LABEL_MEASURE_PX;

                // Sized so the graph's longest label fits - from the whole model, not just what is
                // on screen, so the text does not resize while panning. The BOX, though, is each
                // node's own: they differ by several hundred pixels (an event's height follows its
                // param count), and one size for all of them either overflowed the short ones or
                // truncated them. See createLabelSizer.
                let longest = '';
                for (const m of graphModel.values()) {
                    if (m.dto.kind !== 'Event') { continue; }
                    if (m.dto.label.length > longest.length) { longest = m.dto.label; }
                }
                sizeFor = createLabelSizer(longest, advancePerPx,
                    Math.min(13, Math.max(8, Math.round(k * 55))));
                ctx.textBaseline = 'middle';
            }
            for (const m of graphModel.values()) {
                // A mounted node draws itself; its stand-in rect would only sit behind it. For most
                // nodes that is merely wasted paint, but a junction's box is transparent (the
                // diamond is its shape), so the rect showed through as a coloured square around it.
                if (mountedIds.has(m.dto.id)) { continue; }

                const sx = m.x * k + x, sy = m.y * k + y, sw = m.w * k, sh = m.h * k;
                if (sx + sw < 0 || sy + sh < 0 || sx > w || sy > h) { continue; }
                const color = colour(m.colorToken);
                ctx.globalAlpha = 0.28; ctx.fillStyle = color; ctx.fillRect(sx, sy, sw, sh);
                ctx.globalAlpha = 0.9; ctx.strokeStyle = color; ctx.lineWidth = 1.5; ctx.strokeRect(sx, sy, sw, sh);
                if (showLabels && sizeFor !== null && m.dto.kind === 'Event' && sw > 30) {
                    ctx.globalAlpha = 1;
                    ctx.fillStyle = labelColor;
                    const { fontPx, maxLines } = sizeFor(
                        Math.max(0, sw - LABEL_PAD * 2), Math.max(0, sh - LABEL_PAD * 2));
                    // Only when it actually changes: parsing the font shorthand per node is the
                    // cost that made one size per frame attractive, and nodes of a size cluster.
                    if (fontPx !== lastFontPx) {
                        ctx.font = `${fontPx}px sans-serif`;
                        lastFontPx = fontPx;
                    }
                    // Arithmetic from the measured advance rather than measureText per node -
                    // hundreds of these are drawn per frame.
                    const lines = wrapLabel(m.dto.label, sw - LABEL_PAD * 2, maxLines,
                        s => s.length * advancePerPx * fontPx);
                    // Centre the block vertically, so a one-line label still sits mid-node.
                    const step = fontPx * LINE_RATIO;
                    let ly = sy + sh / 2 - (lines.length - 1) * step / 2;
                    for (const line of lines) {
                        ctx.fillText(line, sx + LABEL_PAD, ly);
                        ly += step;
                    }
                }
            }
            ctx.globalAlpha = 1;
        },
        getGroupBounds(by: 'thread' | 'chapter') { return computeGroupBounds(by); },
        drawSwimlanesTo(canvas: HTMLCanvasElement, showThread: boolean, showChapter: boolean): void {
            const ctx = canvas.getContext('2d');
            if (!ctx) { return; }
            // One read for the frame, as in drawLodTo. There are far fewer lanes than nodes, but
            // the resolver also memoises, so a lane repeating a hue costs nothing after the first.
            const colour = colourResolver(container);
            const w = container.clientWidth, h = container.clientHeight;
            const dpr = window.devicePixelRatio || 1;
            if (canvas.width !== Math.round(w * dpr) || canvas.height !== Math.round(h * dpr)) {
                canvas.width = Math.round(w * dpr);
                canvas.height = Math.round(h * dpr);
            }
            ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
            ctx.clearRect(0, 0, w, h);
            const { k, x, y } = area.area.transform;
            const drawLanes = (by: 'thread' | 'chapter', dashed: boolean): void => {
                ctx.font = 'bold 11px sans-serif';
                ctx.textBaseline = 'top';
                for (const g of computeGroupBounds(by)) {
                    const sx = g.x * k + x, sy = g.y * k + y, sw = g.w * k, sh = g.h * k;
                    if (sx + sw < 0 || sy + sh < 0 || sx > w || sy > h) { continue; }
                    const color = colour(laneToken(by + ':' + g.key));
                    ctx.globalAlpha = 0.07; ctx.fillStyle = color; ctx.fillRect(sx, sy, sw, sh);
                    ctx.globalAlpha = 0.9; ctx.strokeStyle = color; ctx.lineWidth = 1.5;
                    ctx.setLineDash(dashed ? [8, 4] : []);
                    ctx.strokeRect(sx, sy, sw, sh);
                    ctx.setLineDash([]);
                    ctx.globalAlpha = 0.85; ctx.fillStyle = color;
                    ctx.textAlign = dashed ? 'right' : 'left'; // chapter labels on the right, thread on the left
                    ctx.fillText(g.title, dashed ? sx + sw - 5 : sx + 6, sy + 4);
                }
                ctx.textAlign = 'left';
            };
            if (showThread) { drawLanes('thread', false); }
            if (showChapter) { drawLanes('chapter', true); }
            ctx.globalAlpha = 1;
            ctx.setLineDash([]);
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
            let newDto: StoryGraphNodeDto | undefined;
            if (node && node.dto.kind === 'Event') {
                newDto = update(node.dto);
                node.applyDto(newDto);
                void remeasure(node);
            }
            // Mirror into the model so an unmounted (windowed) node keeps the staged edit + overview
            // colour, and re-mounts with it applied.
            const m = graphModel.get(nodeId);
            if (m && m.dto.kind === 'Event') {
                m.dto = newDto ?? update(m.dto);
                m.colorToken = overviewToken(m.dto, m.dto.branch ?? null);
            }
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
    border-radius: var(--radius-6);
    box-sizing: border-box;
    display: flex;
    flex-direction: column;
    justify-content: center;
    padding: var(--space-2) var(--space-8);
    cursor: pointer;
    /* The OR node's box is transparent - the rotated inner square is its shape - so outlining the
       box draws a rectangle around a diamond. Outline the diamond instead: an outline on a rotated
       element rotates with it. */
    ${p => p.selected ? `
        &:not(.k-OrJunction):not(.k-StagingOr) {
            outline: 2px solid var(--vscode-focusBorder);
            outline-offset: 2px;
        }
        &.k-OrJunction .diamond, &.k-StagingOr .diamond {
            outline: 2px solid var(--vscode-focusBorder);
            outline-offset: 2px;
        }
    ` : ''}

    .title {
        font-size: var(--font-size-12);
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
        border-radius: var(--radius-3);
        background: var(--vscode-editorWidget-background, var(--vscode-editor-background));
    }
    &.k-OrJunction .title, &.k-StagingOr .title { position: relative; z-index: 1; }
    &.k-Portal, &.k-TacticalPlot {
        border-style: dashed;
        border-radius: var(--radius-6);
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
        border: var(--space-1) solid var(--vscode-disabledForeground, #888);
        color: var(--vscode-descriptionForeground);
        cursor: pointer;
        font-size: var(--font-size-10);
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
        border: var(--space-1) solid var(--vscode-disabledForeground, #888);
        color: var(--vscode-descriptionForeground);
        cursor: pointer;
        font-size: var(--font-size-10);
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

function VirtualNodeView(props: { data: StoryNode; emit: RenderEmit<Schemes> }): React.JSX.Element {
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
                    ><span className="codicon codicon-close" /></button>
                </Drag.NoDrag>
            ) : null}
            {dto.kind === 'TacticalPlot' && output ? (
                <Drag.NoDrag>
                    <button
                        className="jump" title="Jump to this battle's own story"
                        onClick={() => onReachableFromRequested(dto.id)}
                    ><span className="codicon codicon-arrow-right" /></button>
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

function StoryNodeView(props: { data: StoryNode; emit: RenderEmit<Schemes> }): React.JSX.Element {
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
    border-radius: var(--radius-6);
    box-sizing: border-box;
    display: flex;
    flex-direction: column;
    padding: var(--space-4) var(--space-8) var(--space-6);
    font-size: var(--font-size-11);
    font-family: var(--vscode-font-family);
    cursor: default;
    ${p => p.selected ? 'outline: 2px solid var(--vscode-focusBorder); outline-offset: 2px;' : ''}

    /* Generated from LIFECYCLE_TOKENS, which the legend swatches and the overview rects also read.
       These four rules, those swatches and a hex mirror for the canvas used to be three separate
       copies of one mapping, kept in step by hand. */
    ${Object.entries(LIFECYCLE_TOKENS)
        .map(([lifecycle, token]) => `&.lc-${lifecycle} { border-color: var(${token}); }`)
        .join('\n    ')}
    &.unreachable { opacity: 0.5; }
    &.untested    { border-style: dashed; }

    .header {
        display: flex;
        align-items: center;
        gap: var(--space-2);
        height: ${EVENT_HEADER_H}px;
        flex-shrink: 0;
        border-bottom: var(--space-1) solid var(--vscode-panel-border);
        margin-bottom: var(--space-2);
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
        border: var(--space-1) solid var(--vscode-focusBorder);
        font-size: var(--font-size-11);
        font-family: inherit;
        padding: 0 var(--space-2);
    }
    .header button {
        flex-shrink: 0;
        background: transparent;
        border: none;
        color: var(--vscode-descriptionForeground);
        cursor: pointer;
        padding: 0 var(--space-2);
        font-size: var(--font-size-11);
        line-height: 1.6;
    }
    .header button:hover { color: var(--vscode-editor-foreground); }
    .header button.danger:hover { color: var(--vscode-errorForeground, #f44); }

    .row {
        display: flex;
        align-items: center;
        gap: var(--space-4);
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
        border-top: var(--space-1) solid var(--vscode-panel-border);
        margin-top: var(--space-2);
    }
    .section-toggle {
        flex: 1;
        min-width: 0;
        align-self: center;
        cursor: pointer;
        font-weight: bold;
        font-size: var(--font-size-10);
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
        font-size: var(--font-size-11);
        font-family: inherit;
        background: var(--vscode-input-background);
        color: var(--vscode-input-foreground);
        border: var(--space-1) solid var(--vscode-input-border, transparent);
        padding: 0 var(--space-2);
    }
    .row input[type=checkbox] { margin: 0; }
    .row input.missing, .row select.missing { border-color: var(--vscode-errorForeground, #f44); }
    .row input.diag-error, .row select.diag-error {
        border-color: var(--vscode-errorForeground, #f44);
        outline: var(--space-1) solid var(--vscode-errorForeground, #f44);
    }
    .row input.diag-warning, .row select.diag-warning {
        border-color: var(--vscode-charts-yellow, #cca700);
        outline: var(--space-1) solid var(--vscode-charts-yellow, #cca700);
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
        padding: 0 var(--space-2);
        font-size: var(--font-size-11);
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
}): React.JSX.Element {
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
    fetchOptions: (prefix: string) => Promise<StoryParamOptionDto[]>;
    onInput?: (v: string) => void;
    placeholder?: string; className?: string;
}): React.JSX.Element {
    const [value, setValue] = useState(props.value);
    const [options, setOptions] = useState<StoryParamOptionDto[]>([]);
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
}): React.JSX.Element {
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
                    + (diagnostic ? `\nWarning: ${diagnostic.message}` : '');
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
                                ><span className="codicon codicon-go-to-file" /></button>
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
}): React.JSX.Element {
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
                    <span className={`codicon codicon-chevron-${collapsed ? 'right' : 'down'}`} /> {label}
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
}): React.JSX.Element {
    const clearKind = props.kind === 'trigger' ? 'clearEventType' : 'clearRewardType';
    return (
        <div className="row">
            <label>Type</label>
            {props.typeName ? (
                <>
                    <Drag.NoDrag>
                        <span
                            className="type-chip"
                            style={{
                                background: fadedBg(stepColor(props.kind, props.typeName)),
                                borderColor: stepColor(props.kind, props.typeName),
                                color: 'var(--vscode-editor-foreground)',
                            }}
                            title={`${props.typeName} - remove the type to attach a different one`}
                        >
                            <span
                                className={`codicon ${props.kind === 'trigger' ? 'codicon-zap' : 'codicon-gift'}`}
                                aria-hidden="true"
                            />
                            <span className="type-chip-name">{props.typeName}</span>
                        </span>
                    </Drag.NoDrag>
                    {props.readOnly ? null : (
                        <Drag.NoDrag>
                            <button
                                className="goto chip-remove"
                                title={`Remove this ${props.kind} and its parameters`}
                                onClick={() => sendCommand(
                                    { kind: clearKind, threadUri: props.threadUri, eventName: props.eventName },
                                    `Remove ${props.kind} '${props.typeName}' and its parameters from '${props.eventName}'?`)}
                            ><span className="codicon codicon-close" /></button>
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

function EventNodeView(props: { data: StoryNode; emit: RenderEmit<Schemes> }): React.JSX.Element {
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
            <EventForm dto={dto} readOnly={readOnly} />
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

/**
 * The editable body of an Event - header actions plus the General/Trigger/Reward sections. Node
 * chrome (the EventBody wrapper, lifecycle border, sockets) stays with EventNodeView; this is just
 * the form, so the node modal can mount the very same UI for a single event.
 */
function EventForm(props: { dto: StoryGraphNodeDto; readOnly: boolean }): React.JSX.Element {
    const dto = props.dto;
    const readOnly = props.readOnly;

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
        <>
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
                            ><span className="codicon codicon-check" /></button>
                        </Drag.NoDrag>
                        <Drag.NoDrag>
                            <button
                                title="Cancel (Esc)"
                                onMouseDown={e => { e.preventDefault(); cancelRename(); }}
                            ><span className="codicon codicon-close" /></button>
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
                        <button title="Rename this event" onClick={openRename}><span className="codicon codicon-edit" /></button>
                    </Drag.NoDrag>
                )}
                {(nodeDiagnostics.get(dto.id)?.length ?? 0) > 0 ? (
                    <span
                        className={'diag-badge ' + (nodeDiagnostics.get(dto.id)!.some(d => d.severity === 'error')
                            ? 'diag-error' : 'diag-warning')}
                        title={nodeDiagnostics.get(dto.id)!.map(d => d.message).join('\n')}
                    ><span className="codicon codicon-warning" />{nodeDiagnostics.get(dto.id)!.length}</span>
                ) : null}
                <Drag.NoDrag>
                    <button
                        title="Open in XML"
                        onClick={() => vscode.postMessage({ type: 'openXml', threadUri: dto.threadUri, line: dto.line ?? 0 })}
                    ><span className="codicon codicon-go-to-file" /></button>
                </Drag.NoDrag>
                <Drag.NoDrag>
                    <button
                        title="Show only what's reachable from here"
                        onClick={() => onReachableFromRequested(dto.id)}
                    ><span className="codicon codicon-filter" /></button>
                </Drag.NoDrag>
                {readOnly ? null : (
                    <Drag.NoDrag>
                        <button
                            className="danger" title="Delete this event"
                            onClick={() => sendCommand(
                                { kind: 'deleteEvent', threadUri: dto.threadUri, eventName: dto.label },
                                `Delete story event '${dto.label}'?`)}
                        ><span className="codicon codicon-trash" /></button>
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
        </>
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

function StoryConnectionView(props: { data: StoryConnection }): React.JSX.Element | null {
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

function StorySocketView(): React.JSX.Element {
    return <SocketDot data-testid="socket" />;
}

// ── App chrome ───────────────────────────────────────────────────────────────────────────────────

const GlobalStyle = createGlobalStyle`
    /* The rules below style html, body and #root - the Shell's ANCESTORS - so they cannot inherit
       the layer the Shell carries. Declared at the root, it reaches both them and the Shell. */
    ${tokensRootCss}

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
        border-radius: var(--radius-6);
        z-index: 5;
    }
    /* Same reason as the selection outline: on an OR node the ring belongs to the diamond, not to
       the transparent box around it. */
    .story-flash.k-OrJunction, .story-flash.k-StagingOr { animation: none; }
    .story-flash.k-OrJunction .diamond, .story-flash.k-StagingOr .diamond {
        animation: story-flash 0.8s ease-in-out 2;
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
        border: var(--space-1) solid var(--vscode-focusBorder);
        font-size: var(--font-size-11);
    }
    .suggest-item {
        padding: var(--space-2) var(--space-6);
        cursor: pointer;
        white-space: nowrap;
        overflow: hidden;
        text-overflow: ellipsis;
    }
    .suggest-item:hover { background: var(--vscode-list-hoverBackground, rgba(128, 128, 128, 0.2)); }

    /* Diagnostic severity accents - node header badges and the problems list. */
    .diag-badge {
        font-size: var(--font-size-10);
        font-weight: bold;
        padding: 0 var(--space-2);
    }
    .diag-badge.diag-error { color: var(--vscode-errorForeground, #f44); }
    .diag-badge.diag-warning { color: var(--vscode-charts-yellow, #cca700); }

    /* Immutable-type chips - used in node bodies and the toolbar's create form. In a node body the
       chip is tinted by its trigger/reward family colour (stepColor/fadedBg, set inline) and leads
       with a codicon; the create form leaves the default badge colours. */
    .type-chip {
        display: inline-flex;
        align-items: center;
        gap: var(--space-4);
        min-width: 0;
        padding: var(--space-1) var(--space-6);
        border: var(--space-1) solid var(--vscode-panel-border);
        border-radius: var(--radius-3);
        background: var(--vscode-badge-background, rgba(128, 128, 128, 0.2));
        /* Pair the text with the badge background - without this the chip inherited the dark
           editor foreground and read as near-black on the theme's (often blue) badge colour. */
        color: var(--vscode-badge-foreground, var(--vscode-editor-foreground));
    }
    .type-chip .codicon { font-size: var(--icon-size-12); flex-shrink: 0; }
    .type-chip-name { min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .type-empty {
        padding: var(--space-1) var(--space-6);
        border: var(--space-1) dashed var(--vscode-panel-border);
        border-radius: var(--radius-3);
        color: var(--vscode-descriptionForeground);
        font-style: italic;
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
    }
`;

const Shell = styled.div`
    ${dockChromeCss}
    ${rotarySwitchCss}

    height: 100%;
    display: flex;
    flex-direction: column;

    .toolbar {
        padding: var(--space-4) var(--space-8);
        display: flex;
        gap: var(--space-6);
        align-items: center;
        background: var(--vscode-sideBar-background);
        border-bottom: var(--space-1) solid var(--vscode-panel-border);
        flex-shrink: 0;
    }
    select, input[type=text] {
        background: var(--vscode-input-background);
        color: var(--vscode-input-foreground);
        border: var(--space-1) solid var(--vscode-input-border, transparent);
        padding: var(--space-2) var(--space-6);
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
        padding: var(--space-2) var(--space-8);
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
        border: var(--space-1) solid var(--vscode-panel-border);
        border-radius: var(--radius-3);
        overflow: hidden;
    }
    .mode-switch button {
        border-radius: 0;
    }
    .mode-switch button + button {
        border-left: var(--space-1) solid var(--vscode-panel-border);
    }

    .body { flex: 1; display: flex; overflow: hidden; min-height: 0; }
    /* The canvas column: the graph, with the bottom panels under it. They sit INSIDE this column so
       they border the dock rather than running underneath it - the dock is full height, the same
       arrangement the localisation editors use (.grid-column there).
       min-height: 0 is load-bearing - without it the canvas refuses to shrink below its content and
       pushes the panels off the bottom. */
    .canvas-column { flex: 1; display: flex; flex-direction: column; min-width: 0; min-height: 0; }
    .canvas-area { flex: 1; position: relative; overflow: hidden; min-height: 0; }
    .canvas { position: absolute; inset: 0; z-index: 1; }

    /* Zero-size anchor at the graph origin inside rete's transformed content holder: its absolutely
       positioned children are therefore laid out in graph coordinates and inherit pan/zoom.
       z-index:-1 (not DOM order) keeps it beneath the nodes AND the connections - rete's content
       manager re-orders holder children as nodes/connections come and go, and it inserts
       connections ahead of a merely-prepended element, which left the lanes painting over the
       edges. The holder's will-change:transform makes it a stacking context, so the negative
       index stays contained here. */
    /* Swimlanes: a screen-space canvas behind the nodes and behind the LOD canvas (see SwimlaneCanvas). */
    .swimlane-canvas { position: absolute; inset: 0; z-index: 0; pointer-events: none; }

    /* LOD overview: a screen-space canvas behind the nodes (z-index below .canvas), redrawn on
       pan/zoom. See the LodOverview component. */
    .lod-canvas { position: absolute; inset: 0; z-index: 0; pointer-events: none; }
    .status {
        position: absolute;
        inset: 0;
        display: flex;
        align-items: center;
        justify-content: center;
        padding: var(--space-16);
        color: var(--vscode-disabledForeground);
        background: var(--vscode-editor-background);
    }

    /* ── Right dock ─────────────────────────────────────────────────────── */
    ${rightDockCss}
    ${dockHeaderCss}
    /* Tall enough for the rotary mode dial, which is this editor's alone. */
    .dock-header { min-height: 78px; }
    ${dockBodyCss}
    ${dockOverviewCss}
    /* Tools column sprawls from the vertical centre, minimap to its right with breathing room. */
    /* Tools on the left set the left gap; mirror it on the right, minimap flexes to fill between. */
    .overview-mid { display: flex; align-items: center; gap: var(--space-8); padding-right: var(--space-8); }
    .overview-tools { display: flex; flex-direction: column; gap: var(--space-4); flex-shrink: 0; }
    .filters-below { display: flex; flex-direction: column; gap: var(--space-4); }
    .filters-below select { width: 100%; }

    /* Codicons inherit their button's colour (never coloured individually) and scale per context. */
    .codicon { font-size: var(--icon-size-16); vertical-align: middle; }
    .overview-tools .codicon { font-size: var(--icon-size-16); }
    .rotary-center .codicon { font-size: var(--icon-size-22); }
    .rotary-pos .codicon { font-size: var(--icon-size-14); }
    .sim-head .codicon { font-size: var(--icon-size-14); vertical-align: -1px; }

    .resize-handle-w {
        position: absolute;
        top: 0;
        left: -3px;
        width: 6px;
        height: 100%;
        cursor: ew-resize;
        z-index: 2;
    }
    .resize-handle-w:hover, .resize-handle-w:active {
        background: var(--vscode-sash-hoverBorder, var(--vscode-focusBorder));
    }

    /* ── Palette (dock content, Edit mode) ─────────────────────────────── */
    .palette-scroll { min-width: 0; }
    .palette-scroll .dock-search { margin-bottom: var(--space-8); }
    .palette-new {
        padding-bottom: var(--space-12);
        border-bottom: var(--space-1) solid var(--vscode-panel-border, rgba(128, 128, 128, 0.35));
    }
    /* The .toggle variant lived here: a third hand-rolled folding heading, on a div with an
       onClick, so it could not be reached by keyboard at all. DockSection carries the cursor, the
       hover and a real button. */
    /* Colour family: just a gap between groups - no box (the tile tint is the grouping). */
    .tile-family { margin-bottom: var(--space-6); }
    /* Geometry comes from the shared dock chrome, so a palette tile is the same object as a tile in
       the localisation docks. Only the colour is this editor's own: tiles are tinted by type family,
       which is what makes the palette scannable. */
    .palette-tile {
        border-style: solid;
        border-width: var(--space-1);
        cursor: grab;
    }
    .palette-tile:hover { outline: var(--space-1) solid var(--vscode-focusBorder); }

    .palette-empty { font-size: var(--font-size-11); color: var(--vscode-descriptionForeground); }

    /* ── Minimap (dock overview) ───────────────────────────────────────── */
    .minimap-wrap { flex: 1; min-width: 0; display: flex; }
    .minimap {
        display: block;
        border: var(--space-1) solid var(--vscode-panel-border);
        border-radius: var(--radius-3);
        background: var(--vscode-editor-background);
        cursor: crosshair;
    }
    .minimap.minimap-empty {
        flex: 1; height: 118px;
        display: flex; align-items: center; justify-content: center;
        font-size: var(--font-size-11); color: var(--vscode-descriptionForeground);
    }
    .minimap .mm-node { fill: var(--vscode-descriptionForeground); opacity: 0.55; }
    .minimap .mm-view {
        fill: var(--vscode-focusBorder);
        fill-opacity: 0.12;
        stroke: var(--vscode-focusBorder);
        stroke-width: 1;
    }

    /* ── Simulation controls (dock content, Simulation mode) ───────────── */
    .sim-controls { display: flex; flex-direction: column; gap: var(--space-8); font-size: var(--font-size-12); }
    .sim-section { min-width: 0; }
    .sim-head { font-weight: bold; margin-bottom: var(--space-2); }

    /* ── Bottom panels (full width) ────────────────────────────────────── */
    ${problemsPanelCss}
    .bottom-panels { flex-shrink: 0; display: flex; flex-direction: column; }
    /* The simulation log shares the problems bar's title row, so its bar stays sticky over a long
       scrolling log. The paddings are this editor's own measured values, kept deliberately: the
       shared block carries the localisation grids' 2px/6px, and the two differ by a pixel or two
       from a calibration that was done here. */
    .panel-bar {
        position: sticky;
        top: 0;
        background: var(--vscode-sideBar-background);
        padding: var(--space-1) var(--space-4) var(--space-2);
    }
    .problem-row { padding: var(--space-1) var(--space-4); }
    .sim-log-panel {
        position: relative;
        overflow-y: auto;
        padding: var(--space-4) var(--space-8);
        background: var(--vscode-sideBar-background);
        border-top: var(--space-1) solid var(--vscode-panel-border);
        font-size: var(--font-size-11);
        color: var(--vscode-descriptionForeground);
    }
    .sim-row { display: flex; gap: var(--space-4); align-items: center; margin: var(--space-2) 0; }
    .sim-row input[type=text] { width: 90px; flex: none; }
    .sim-name { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; max-width: 160px; }
    .sim-kind {
        font-size: var(--font-size-10);
        padding: 0 var(--space-4);
        border-radius: var(--radius-3);
        border: var(--space-1) solid var(--vscode-panel-border);
        color: var(--vscode-descriptionForeground);
    }
    .sim-kind.k-lua      { border-color: var(--vscode-charts-blue, #3794ff); }
    .sim-kind.k-tactical { border-color: var(--vscode-charts-yellow, #cca700); }

    .problems {
        position: relative;
        overflow-y: auto;
        border-top: var(--space-1) solid var(--vscode-panel-border);
        background: var(--vscode-sideBar-background);
        flex-shrink: 0;
        font-size: var(--font-size-12);
        padding: var(--space-2) var(--space-4);
    }
    .problem-node {
        flex-shrink: 0;
        max-width: 180px;
        color: var(--vscode-descriptionForeground);
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
    }
    .problem-row button {
        background: transparent;
        border: none;
        color: var(--vscode-descriptionForeground);
        cursor: pointer;
        padding: 0 var(--space-2);
        flex-shrink: 0;
    }
    .problem-row button:hover { background: transparent; color: var(--vscode-editor-foreground); }

    .legend {
        padding: var(--space-2) var(--space-8);
        display: flex;
        gap: var(--space-12);
        font-size: var(--font-size-11);
        color: var(--vscode-descriptionForeground);
        background: var(--vscode-sideBar-background);
        border-top: var(--space-1) solid var(--vscode-panel-border);
        flex-shrink: 0;
        flex-wrap: wrap;
    }
    .legend .swatch {
        display: inline-block;
        width: 10px; height: 10px;
        border-radius: var(--radius-3);
        border: 2px solid;
        vertical-align: -1px;
        margin-right: var(--space-2);
    }

    /* The AND/OR socket shapes, drawn rather than typed. They stand for the shapes the graph
       renders, so they are figures and not text - and the house rule keeps user-facing strings
       ASCII, which a box-drawing character is not. */
    .shape-circle, .shape-diamond {
        display: inline-block;
        width: 9px;
        height: 9px;
        border: 1.5px solid currentColor;
        vertical-align: -1px;
    }
    .shape-circle { border-radius: 50%; }
    .shape-diamond { transform: rotate(45deg); }
`;

const LIFECYCLES = ['Inactive', 'Waiting', 'Armed', 'Fired', 'Disabled'];

/**
 * How the faction's plot manifest registers a thread.
 *
 * Two stops and an 'any', so it is a dropdown rather than a slider - the two are not points on
 * an axis, they are the two lists a manifest keeps.
 */
const PLOT_STATES = ['Active', 'Suspended'];

function App(): React.JSX.Element {
    const containerRef = useRef<HTMLDivElement>(null);
    const editorRef = useRef<EditorHandle | null>(null);
    const filtersRef = useRef<FilterState>({ ...EMPTY_FILTERS });
    // User-driven fetches (filter changes) re-layout; edit-driven refreshes patch in place.
    const fullRenderRef = useRef(true);
    const pendingGraphRef = useRef<{
        nodes: StoryGraphNodeDto[]; edges: StoryGraphEdgeDto[]; layout: StoryLayoutEntryDto[]; full: boolean;
    } | null>(null);

    const [filters, setFiltersState] = useState<FilterState>({ ...EMPTY_FILTERS });
    const [branches, setBranches] = useState<string[]>([]);
    const [threads, setThreads] = useState<string[]>([]);
    const [eventTypes, setEventTypes] = useState<string[]>([]);
    const [rewardTypes, setRewardTypes] = useState<string[]>([]);
    const [status, setStatus] = useState<string | null>('Loading story graph...');
    // True while a full rebuild's auto-arrange is in flight, so the canvas stays covered instead
    // of flashing the pre-layout node stack (every node starts at the same spot) before it settles.
    const [layouting, setLayouting] = useState(false);
    const [createRequest, setCreateRequest] = useState<CreateRequest | null>(null);
    const [simState, setSimState] = useState<StorySimStateDto | null>(null);
    const simRef = useRef<StorySimStateDto | null>(null);
    const [mode, setMode] = useState<EditorMode>('view');
    // Which modes the flags permit. Both default off, matching the extension's own fallbacks, so a
    // panel that somehow never receives the message stays read-only rather than offering modes whose
    // every request the server would reject. View is implied - the panel wouldn't open without it.
    const [availableModes, setAvailableModes] = useState<{ edit: boolean; simulate: boolean }>(
        { edit: false, simulate: false });
    const [problems, setProblems] = useState<StoryDiagnosticDto[]>([]);

    /**
     * The nodes the graph currently holds, for narrowing the problems table to them.
     *
     * The graph is filtered on the SERVER - a filtered-out event is not in the response at all -
     * while diagnostics are computed for the whole campaign, so without this the table reports
     * findings about nodes that are not on screen. Null until the first graph arrives: filtering
     * against a set nobody has filled would empty the table for reasons that have nothing to do
     * with the filter.
     */
    const [graphNodeIds, setGraphNodeIds] = useState<Set<string> | null>(null);

    /** The reader has asked to see the findings the view filter holds back. */
    const [showAllProblems, setShowAllProblems] = useState(false);
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

    const applySimOverlay = useCallback((state: StorySimStateDto | null) => {
        simRef.current = state;
        setSimState(state);
        const handle = editorRef.current;
        if (!handle) { return; }
        handle.applyLifecycles(state?.running
            ? new Map(state.nodes.map(n => [n.nodeId, n.lifecycle]))
            : null);
    }, []);

    const fetchGraph = useCallback((next: FilterState) => {
        filtersRef.current = next;
        setFiltersState(next);
        fullRenderRef.current = true;
        vscode.postMessage({ type: 'fetch', filters: next });
    }, []);

    /** Runs setGraph, keeping the canvas covered for the duration of a full rebuild's auto-arrange. */
    const runSetGraph = useCallback((handle: EditorHandle, g: {
        nodes: StoryGraphNodeDto[]; edges: StoryGraphEdgeDto[]; layout: StoryLayoutEntryDto[]; full: boolean;
    }) => {
        if (g.full) { setLayouting(true); }
        // Re-apply staged edits once the (re)built graph settles, so a reconcile never reverts them.
        const done = handle.setGraph(g.nodes, g.edges, g.layout, g.full)
            .then(() => reapplyStagedCommands());
        if (g.full) { void done.finally(() => setLayouting(false)); }
    }, []);

    const applyGraph = useCallback((
        nodes: StoryGraphNodeDto[], edges: StoryGraphEdgeDto[], layout: StoryLayoutEntryDto[], full: boolean
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
                    setGraphNodeIds(new Set(graphNodes.map(n => n.id)));
                    applyGraph(
                        graphNodes,
                        (msg.edges as StoryGraphEdgeDto[] | undefined) ?? [],
                        (msg.layout as StoryLayoutEntryDto[] | undefined) ?? [],
                        full);
                    // A running simulation keeps painting its lifecycles over fresh renders.
                    if (simRef.current?.running) { applySimOverlay(simRef.current); }
                    break;
                }
                case 'simState':
                    applySimOverlay((msg.state as StorySimStateDto | null) ?? null);
                    break;
                case 'simChanged':
                    sendSim('getState');
                    break;
                case 'paramOptions': {
                    const resolve = pendingOptionRequests.get(msg.requestId as number);
                    if (resolve) {
                        pendingOptionRequests.delete(msg.requestId as number);
                        resolve((msg.options as StoryParamOptionDto[] | undefined) ?? []);
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
                    setStatus('Warning: ' + String(msg.message));
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

    const setFilter = (patch: Partial<FilterState>): void => fetchGraph({ ...filtersRef.current, ...patch });
    const clearFilters = (): void => fetchGraph({ ...EMPTY_FILTERS });
    const toggleLane = (which: 'thread' | 'chapter'): void => {
        const nextThread = which === 'thread' ? !showThreadLanes : showThreadLanes;
        const nextChapter = which === 'chapter' ? !showChapterLanes : showChapterLanes;
        setShowThreadLanes(nextThread);
        setShowChapterLanes(nextChapter);
        vscode.postMessage({ type: 'setLanePref', showThreadLanes: nextThread, showChapterLanes: nextChapter });
    };

    // Validate button reads out validation health (codicon name; coloured via sev-* class):
    // unvalidated (stale/never run) beats error beats warning beats clean. The precedence and the
    // glyph mapping are shared with the localisation editor's Validate tag so the two controls mean
    // the same thing - two copies of this rule drifted apart once already.
    /**
     * What "in this view" means here: the node is in the graph the server sent back.
     *
     * A finding that names no node - a malformed manifest, an unresolved plot entry - is about a
     * FILE and no graph filter can bring it into view, so it is always kept. Undefined until the
     * first graph arrives, which is how the panel knows not to filter yet.
     */
    const problemInView = useMemo(
        () => graphNodeIds === null
            ? undefined
            : (p: StoryDiagnosticDto) => !p.nodeId || graphNodeIds.has(p.nodeId),
        [graphNodeIds]);

    const problemView = useMemo(
        () => filterProblems(problems, problemInView, showAllProblems),
        [problems, problemInView, showAllProblems]);

    // The badge follows what is ON SCREEN, so it agrees with the table under it. What stops that
    // reading as all-clear over a hidden error is the panel's own "n of m" and its filter chip.
    const severity = !validated ? 'unvalidated' : worstSeverity(problemView.shown);

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
                <div className="canvas-column">
                <div className="canvas-area">
                    {/* Screen-space canvases behind the nodes (.canvas is z-index 1), redrawn on pan/zoom. */}
                    <SwimlaneCanvas
                        getHandle={() => editorRef.current}
                        showThread={showThreadLanes} showChapter={showChapterLanes}
                    />
                    <LodOverview getHandle={() => editorRef.current} />
                    <div
                        className="canvas" ref={containerRef}
                        onDragOver={onCanvasDragOver} onDrop={onCanvasDrop}
                    />
                    {status || layouting ? <p className="status">{status ?? 'Arranging layout...'}</p> : null}
                </div>
                <div className="bottom-panels">
                    {showProblems && problems.length ? (
                        <ProblemsBar
                            problems={problemView.shown}
                            label={problemView.label}
                            filter={{
                                hidden: problemView.hidden,
                                showingAll: showAllProblems,
                                filterable: problemView.filterable,
                                onToggle: () => setShowAllProblems(open => !open),
                            }}
                            onJump={id => editorRef.current?.centerNode(id)}
                            onClose={() => setShowProblems(false)}
                        />
                    ) : null}
                    {simState?.running && showSimLog ? (
                        <SimLog state={simState} onClose={() => setShowSimLog(false)} />
                    ) : null}
                    <div className="legend">
                        <span>
                            <span className="swatch" style={{ borderColor: `var(${UNKNOWN_LIFECYCLE_TOKEN})` }} />
                            Inactive
                        </span>
                        {/* The same mapping the node borders are generated from, so a swatch cannot
                            come to disagree with the node it is describing. */}
                        {Object.entries(LIFECYCLE_TOKENS).map(([lifecycle, token]) => (
                            <span key={lifecycle}>
                                <span className="swatch" style={{ borderColor: `var(${token})` }} />
                                {lifecycle}
                            </span>
                        ))}
                        <span><span className="shape-diamond" /> OR</span>
                        <span><span className="shape-circle" /> AND</span>
                        <span>dashed = portal / tactical / untested</span>
                        <span>drag socket to socket = prereq</span>
                    </div>
                </div>
                </div>
                <RightDock
                    memoKey="storyGraph.dock"
                    initialWidth={300}
                    minWidth={210}
                    maxWidth={520}
                    header={<>
                        {mode === 'edit' ? (
                            <IconButton
                                icon="save"
                                className={'header-left' + (pendingCount > 0 ? ' active' : '')}
                                title="Save - write all staged changes to the XML files"
                                badge={pendingCount > 0 ? ` ${pendingCount}` : ''}
                                disabled={pendingCount === 0 || saving}
                                disabledReason={saving
                                    ? 'Save - writing the staged changes now'
                                    : 'Save - nothing is staged'}
                                onClick={saveEdits}
                            />
                        ) : null}
                        <RotaryModeSwitch
                            mode={mode}
                            modes={STORY_MODES.filter(
                                m => (m.id === 'edit' ? availableModes.edit
                                    : m.id === 'simulate' ? availableModes.simulate : true))}
                            onSelect={switchMode}
                        />
                        <SeverityTag
                            severity={severity}
                            count={problems.length}
                            title="Validate - check the story for problems (opens the panel below)"
                            onClick={() => { validateEdits(); }}
                        />
                    </>}
                    content={<>
                        {mode === 'edit'
                            ? <NodePalette eventTypes={eventTypes} rewardTypes={rewardTypes} /> : null}
                        {mode === 'simulate' && simState?.running ? <SimControls state={simState} /> : null}
                        {mode === 'simulate' && !simState?.running
                            ? <div className="dock-hint">Starting simulation...</div> : null}
                        {mode === 'view'
                            ? <div className="dock-hint">Read-only. Switch to Edit to change the story,
                                or Simulation to run it forward.</div> : null}
                    </>}
                    overview={<>
                        <div className="dock-search">
                            <div className="dock-section-title">Filter</div>
                            <div className="search-field">
                                <input
                                    type="text" placeholder="Filter event names..." value={filters.nameFilter}
                                    onChange={e => setFilter({ nameFilter: e.target.value })}
                                />
                            </div>
                        </div>
                        <div className="overview-mid">
                            <div className="overview-tools">
                                <ClearFiltersButton filters={filters} onClear={clearFilters} />
                                <IconButton
                                    icon="arrange"
                                    onClick={() => {
                                        const handle = editorRef.current;
                                        if (!handle) { return; }
                                        setLayouting(true);
                                        // Let the overlay paint before the (heavy, synchronous) arrange
                                        // starts, so a big graph's multi-second freeze is covered by it.
                                        requestAnimationFrame(() => requestAnimationFrame(() => {
                                            void handle.autoArrange().finally(() => setLayouting(false));
                                        }));
                                    }}
                                    title="Arrange - recompute the automatic layout"
                                />
                                <IconButton
                                    icon="frame"
                                    title="Fit graph to view"
                                    onClick={() => editorRef.current?.fit()}
                                />
                                <IconButton
                                    icon="threadLanes"
                                    className={showThreadLanes ? 'active' : undefined}
                                    pressed={showThreadLanes}
                                    title="Toggle thread lanes"
                                    onClick={() => toggleLane('thread')}
                                />
                                <IconButton
                                    icon="chapterLanes"
                                    className={showChapterLanes ? 'active' : undefined}
                                    pressed={showChapterLanes}
                                    title="Toggle chapter lanes"
                                    onClick={() => toggleLane('chapter')}
                                />
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
                            <select
                                value={filters.plotState}
                                onChange={e => setFilter({ plotState: e.target.value })}
                                title="Plot state - how this faction's manifest registers the thread an event lives in"
                            >
                                <option value="">Any plot state</option>
                                {PLOT_STATES.map(p => <option key={p} value={p}>{p}</option>)}
                            </select>
                        </div>
                    </>}
                />
            </div>
        </Shell>
    );
}

/** The sim log opens this tall until the reader drags it somewhere else. */
const SIM_LOG_DEFAULT_HEIGHT = 140;

/**
 * Pointer-capture drag resizing for one panel edge. `axis` maps pointer movement to growth:
 * 'e' = dragging right grows (a left panel's right edge), 'w' = dragging left grows (a right dock's
 * left edge), 'n' = dragging up grows (a bottom bar's top edge). Plain pointer capture on the handle
 * - no window listeners to leak.
 */
/**
 * The campaign's validation problems. A row whose diagnostic lives on a graph node is clickable
 * as a whole (jumps to the node); the ↗ button opens the XML at the diagnostic's line either way.
 *
 * The frame - resizable top edge, counted title, close button, remembered height - is
 * {@link ProblemsPanel}, shared with the localisation editors. Only the rows are the graph's own:
 * these name a node and offer its XML, which is not what a table of translations has to say.
 */
function ProblemsBar(props: {
    problems: StoryDiagnosticDto[];
    /** The count for the heading, which says "3 of 12" while the view filter is holding some back. */
    label: string;
    filter: ProblemFilterControl;
    onJump: (nodeId: string) => void;
    onClose: () => void;
}): React.JSX.Element {
    return (
        <ProblemsPanel
            className="problems"
            memoKey="storyGraph.problems"
            defaultHeight={150}
            title={`Problems (${props.label})`}
            filter={props.filter}
            onClose={props.onClose}
        >
            {props.problems.map((problem, i) => (
                <div
                    className={'problem-row' + (problem.nodeId ? ' clickable' : '')}
                    key={i}
                    title={problem.nodeId ? 'Click to show this node in the graph' : undefined}
                    onClick={problem.nodeId ? () => props.onJump(problem.nodeId!) : undefined}
                >
                    <span className={'diag-badge diag-' + (problem.severity === 'error' ? 'error' : 'warning')}>
                        <span className={'codicon codicon-' + (problem.severity === 'error' ? 'error' : 'warning')} />
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
                    ><span className="codicon codicon-go-to-file" /></button>
                </div>
            ))}
        </ProblemsPanel>
    );
}

/** The running simulation: clock, flag inspector, intervention queue, and the step log. */
/** The simulation driver controls - clock, flags, and pending interventions - stacked for the dock. */
function SimControls(props: { state: StorySimStateDto }): React.JSX.Element {
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
                            {f.value !== 0 ? '1 to 0' : '0 to 1'}
                        </button>
                    </div>
                ))}
                <div className="sim-row">
                    <input
                        type="text" placeholder="Set flag..." value={flagName}
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
                            <option value="">Lua Story_Event...</option>
                            {state.luaNotifications.map(id => <option key={id} value={id}>{id}</option>)}
                        </select>
                    </div>
                ) : null}
            </div>
        </div>
    );
}

/** The simulation step log - full-width bottom panel (VS Code-style), resizable by its top edge. */
function SimLog(props: { state: StorySimStateDto; onClose: () => void }): React.JSX.Element {
    const { size: height, handleProps } = useEdgeResize(
        readPanelSize('storyGraph.simLog', SIM_LOG_DEFAULT_HEIGHT), 60, 320, 'n',
        v => { writePanelSize('storyGraph.simLog', v); });
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
 * The three editor modes on a "half-horizon" arc: they sit ABOVE the horizontal line through the
 * lower half of the centre readout. Edit and Simulation are feature-flagged and a mode that is off
 * is omitted entirely rather than shown disabled - there is nothing the user could do about it from
 * here, and every request it makes would be refused server-side.
 */
const STORY_MODES: RotaryMode<EditorMode>[] = [
    { id: 'view', icon: 'eye', label: 'View', angle: 210 },
    { id: 'edit', icon: 'edit', label: 'Edit', angle: 270 },
    { id: 'simulate', icon: 'play', label: 'Simulation', angle: 330 },
];

const MINIMAP_H = 118;

/**
 * A hand-built overview of the whole graph (the rete minimap plugin can only overlay the canvas, not
 * dock here). Draws every node scaled to the graph extent plus the current viewport rectangle;
 * click/drag pans. Width is dynamic - measured from its flex slot - with a fixed height. Stays live
 * via the `onAreaChanged` bridge (pan/zoom/node-move) and re-reads node geometry on every render.
 */
function Minimap(props: { getHandle: () => EditorHandle | null }): React.JSX.Element {
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

    let inner: React.JSX.Element;
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


/**
 * The zoomed-out overview: one cheap SVG (no per-node React roots) drawing every node as a coloured
 * rect and every edge as a line, straight from graphModel, portalled into rete's transformed content
 * holder so pan/zoom carries it exactly like the nodes it stands in for. Shown only while the graph
 * is in LOD mode (large + zoomed out); zooming past K_DETAIL mounts the real nodes and this returns
 * null. This is what makes opening a large campaign instant - nothing is mounted into rete.
 */
function LodOverview(props: { getHandle: () => EditorHandle | null }): React.JSX.Element {
    const canvasRef = useRef<HTMLCanvasElement>(null);
    // A screen-space canvas sitting behind the nodes (z-index below .canvas). Redrawing a few
    // thousand rects/lines imperatively is ~1-2ms, so pan/zoom stays smooth - unlike an SVG in the
    // transformed holder, which re-rasterised every element on each zoom frame. Always mounted; the
    // draw is a cheap clear when the graph is small (not windowed).
    useEffect(() => {
        let raf = 0;
        const draw = (): void => {
            if (raf) { return; }
            raf = requestAnimationFrame(() => {
                raf = 0;
                const c = canvasRef.current;
                if (c) { props.getHandle()?.drawLodTo(c); }
            });
        };
        draw();
        const unA = subscribeAreaChange(draw);
        const unG = subscribeGeometryChange(draw);
        return () => { if (raf) { cancelAnimationFrame(raf); } unA(); unG(); };
    }, [props]);
    return <canvas ref={canvasRef} className="lod-canvas" />;
}

/**
 * Tinted grouping rectangles behind the nodes - one per thread (solid) or chapter (dashed), drawn on
 * a screen-space canvas and redrawn on pan/zoom. Like the LOD overview, this avoids the transformed-
 * holder divs the swimlanes used to be, which re-rasterised (and stuttered) on every zoom frame.
 */
function SwimlaneCanvas(props: {
    getHandle: () => EditorHandle | null; showThread: boolean; showChapter: boolean;
}): React.JSX.Element {
    const canvasRef = useRef<HTMLCanvasElement>(null);
    const { showThread, showChapter } = props;
    useEffect(() => {
        let raf = 0;
        const draw = (): void => {
            if (raf) { return; }
            raf = requestAnimationFrame(() => {
                raf = 0;
                const c = canvasRef.current;
                if (c) { props.getHandle()?.drawSwimlanesTo(c, showThread, showChapter); }
            });
        };
        draw();
        const unA = subscribeAreaChange(draw);
        const unG = subscribeGeometryChange(draw);
        return () => { if (raf) { cancelAnimationFrame(raf); } unA(); unG(); };
    }, [props, showThread, showChapter]);
    return <canvas ref={canvasRef} className="swimlane-canvas" />;
}

/** Wires the palette's Tactical category to `createTacticalAttachment` - no other UI reaches it. */
function TacticalCreateBar(props: {
    threads: string[];
    initialType: string | null;
    onCreate(threadUri: string, newName: string, value: 'land' | 'space', file: string): void;
    onClose(): void;
}): React.JSX.Element {
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
                type="text" placeholder="Tactical plot manifest file..." value={file}
                onChange={e => setFile(e.target.value)}
                onKeyDown={e => { if (e.key === 'Enter') { create(); } }}
            />
            <select value={thread} onChange={e => setThread(e.target.value)} title="Thread file">
                {props.threads.length === 0 ? <option value="">(no thread files)</option> : null}
                {props.threads.map(t => <option key={t} value={t}>{baseName(t)}</option>)}
            </select>
            <Button
                className="primary"
                disabled={!thread || !file.trim()}
                disabledReason="Pick a thread file and give the story a name"
                onClick={create}
            >
                Create
            </Button>
            <Button onClick={props.onClose}>Cancel</Button>
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

/** A tile/badge background: its family colour washed into the widget background (matches the palette). */
function fadedBg(color: string): string {
    return `color-mix(in srgb, ${color} 20%, var(--vscode-editorWidget-background))`;
}

function NodePalette(props: { eventTypes: string[]; rewardTypes: string[] }): React.JSX.Element {
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
    const tile = (
        key: string, glyph: React.JSX.Element | null, label: string, drag: PaletteDrag, hint: string
    ): React.JSX.Element => {
        const color = stepColor(drag.category, drag.type);
        return (
            <div
                key={key} className="dock-tile palette-tile" draggable
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
    ): React.JSX.Element | null => {
        if (!items.length) { return null; }
        const isCollapsed = q === '' && collapsed[label];
        const families = new Map<string, string[]>();
        for (const t of items) {
            const c = stepColor(category, t);
            const list = families.get(c);
            if (list) { list.push(t); } else { families.set(c, [t]); }
        }
        return (
            <DockSection
                id={label}
                title={label}
                count={items.length}
                collapsed={isCollapsed}
                onToggle={() => setCollapsed(c => ({ ...c, [label]: !c[label] }))}
            >
                {[...families.entries()].map(([color, names]) => (
                    <div key={color} className="tile-family">
                        <div className="tile-grid">
                            {names.map(t => tile(t, null, t, { category, type: t }, hint))}
                        </div>
                    </div>
                ))}
            </DockSection>
        );
    };

    return (
        <div className="palette-scroll">
            <div className="dock-search">
                <div className="search-field">
                    <input
                        type="text" placeholder="Search node types..." value={search}
                        onChange={e => setSearch(e.target.value)}
                    />
                </div>
            </div>
            {showBlank || showAndJunction || showOrJunction ? (
                <div className="dock-section palette-new">
                    <div className="tile-grid">
                        {showBlank ? tile('blank', <span className="codicon codicon-add" />, 'New event',
                            { category: 'blank', type: null },
                            'Drag onto the canvas to create a new untyped event, then edit it in place') : null}
                        {showAndJunction ? tile('and', <span className="junction-glyph shape-circle" />, 'AND',
                            { category: 'andJunction', type: null },
                            'Drag onto the canvas, wire event outputs into it, then drag its output onto the event that should require all of them together') : null}
                        {showOrJunction ? tile('or', <span className="junction-glyph shape-diamond" />, 'OR',
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

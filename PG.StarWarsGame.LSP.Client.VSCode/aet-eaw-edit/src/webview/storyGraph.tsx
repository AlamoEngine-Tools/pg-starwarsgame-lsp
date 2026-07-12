// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Story graph webview app: rete.js renders and lays out the graph (auto-arrange = elk.js),
// React renders toolbar/detail chrome. The extension side (storyGraphPanel.ts) owns all LSP
// traffic; this file only exchanges postMessage envelopes with it. Editing is command-based
// and never optimistic: every mutation round-trips through aet/executeStoryCommand and the
// graph re-renders on the server's aet/storyGraphChanged push.

import { useCallback, useEffect, useRef, useState } from 'react';
import { createRoot } from 'react-dom/client';
import { ClassicPreset, GetSchemes, NodeEditor } from 'rete';
import { AreaExtensions, AreaPlugin } from 'rete-area-plugin';
import { AutoArrangePlugin, Presets as ArrangePresets } from 'rete-auto-arrange-plugin';
import { ConnectionPlugin, Presets as ConnectionPresets } from 'rete-connection-plugin';
import { Presets, ReactArea2D, ReactPlugin, RenderEmit } from 'rete-react-plugin';
import styled, { createGlobalStyle } from 'styled-components';

declare function acquireVsCodeApi(): { postMessage(message: unknown): void };
const vscode = acquireVsCodeApi();

// ── Protocol DTOs (camelCased server records; keep in sync with StoryProtocol.cs) ───────────────

interface StoryGraphNodeDto {
    id: string; kind: string; label: string; threadUri?: string | null; line?: number | null;
    eventType?: string | null; rewardType?: string | null; branch?: string | null;
    lifecycle?: string | null; reachable: boolean;
}
interface StoryGraphEdgeDto { fromId: string; toId: string; kind: string; label?: string | null; }
interface StoryNodeDetailDto {
    id: string; name: string; threadUri?: string | null; line: number;
    eventType?: string | null; eventFilter?: string | null;
    eventParams: { position: number; value: string }[];
    rewardType?: string | null; rewardParams: { position: number; value: string }[];
    prereqGroups: string[][]; branch?: string | null; perpetual: boolean;
    storyDialog?: string | null; storyChapter?: number | null;
    tags: { name: string; value: string }[];
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

const EMPTY_FILTERS: GraphFilters = { nameFilter: '', branch: '', lifecycle: '', reachableFrom: '' };

/** Event/reward type names flagged `untested` in the schema — set once, read during render. */
const untestedTypes = new Set<string>();

function baseName(uri: string | null | undefined): string {
    if (!uri) { return ''; }
    const idx = uri.lastIndexOf('/');
    return idx < 0 ? uri : uri.slice(idx + 1);
}

/** Sends a mutation to the extension (which owns confirmation dialogs and error toasts). */
function sendCommand(payload: Record<string, unknown>, confirm?: string, refreshDetail?: string): void {
    vscode.postMessage({ type: 'command', payload, confirm, refreshDetail });
}

// ── Rete setup ───────────────────────────────────────────────────────────────────────────────────

const flowSocket = new ClassicPreset.Socket('flow');

class StoryNode extends ClassicPreset.Node {
    width: number;
    height: number;

    constructor(public readonly dto: StoryGraphNodeDto, hasInputs: boolean, hasOutputs: boolean) {
        super(dto.label);
        // Event nodes always expose sockets so prereq edges can be drawn to/from them.
        if (hasInputs || dto.kind === 'Event') { this.addInput('in', new ClassicPreset.Input(flowSocket)); }
        if (hasOutputs || dto.kind === 'Event') { this.addOutput('out', new ClassicPreset.Output(flowSocket)); }
        if (dto.kind === 'AndJunction' || dto.kind === 'OrJunction') {
            this.width = 48; this.height = 48;
        } else if (dto.kind === 'Event') {
            this.width = Math.max(180, Math.min(dto.label.length, 34) * 7 + 40);
            this.height = dto.eventType || dto.rewardType ? 64 : 44;
        } else {
            this.width = Math.max(140, Math.min(dto.label.length, 30) * 6.5 + 32);
            this.height = 40;
        }
    }
}

class StoryConnection extends ClassicPreset.Connection<StoryNode, StoryNode> {
    constructor(source: StoryNode, target: StoryNode, public readonly kind: string) {
        super(source, 'out', target, 'in');
    }
}

type Schemes = GetSchemes<StoryNode, StoryConnection>;
type AreaExtra = ReactArea2D<Schemes>;

interface EditorHandle {
    setGraph(nodes: StoryGraphNodeDto[], edges: StoryGraphEdgeDto[], layout: StoryLayoutEntry[]): Promise<void>;
    /** Overrides node lifecycles from the simulation (null restores the static analysis view). */
    applyLifecycles(byNodeId: ReadonlyMap<string, string> | null): void;
    fit(): void;
    destroy(): void;
}

/** Set by the React app before the editor exists; invoked from area/editor pipes. */
let onNodePicked: (node: StoryNode) => void = () => { /* replaced by App */ };

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

    let applyingServerGraph = false;

    // Edge gesture: a user-drawn event→event connection becomes an addPrereq command (new
    // OR-line on the target). The local connection is blocked — the real edge arrives with
    // the server's re-render.
    editor.addPipe(context => {
        if (context.type === 'connectioncreate' && !applyingServerGraph) {
            const source = editor.getNode(context.data.source);
            const target = editor.getNode(context.data.target);
            if (source?.dto.kind === 'Event' && target?.dto.kind === 'Event' && target.dto.threadUri) {
                sendCommand({
                    kind: 'addPrereq',
                    threadUri: target.dto.threadUri,
                    eventName: target.dto.label,
                    token: source.dto.label,
                });
            }
            return undefined; // never materialise gesture connections locally
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
        if (context.type === 'nodepicked') {
            const node = editor.getNode(context.data.id);
            if (node) { onNodePicked(node); }
        }
        if (context.type === 'nodedragged') {
            saveAllPositions();
        }
        return context;
    });

    // Original (static analysis) lifecycles, so ending a simulation restores the pre-sim view.
    const staticLifecycles = new Map<string, string | null | undefined>();

    return {
        async setGraph(
            nodes: StoryGraphNodeDto[], edges: StoryGraphEdgeDto[], layout: StoryLayoutEntry[]
        ): Promise<void> {
            staticLifecycles.clear();
            for (const dto of nodes) { staticLifecycles.set(dto.id, dto.lifecycle); }
            applyingServerGraph = true;
            try {
                await editor.clear();
                const byId = new Map<string, StoryNode>();
                const hasIn = new Set(edges.map(e => e.toId));
                const hasOut = new Set(edges.map(e => e.fromId));
                for (const dto of nodes) {
                    const node = new StoryNode(dto, hasIn.has(dto.id), hasOut.has(dto.id));
                    byId.set(dto.id, node);
                    await editor.addNode(node);
                }
                for (const edge of edges) {
                    const source = byId.get(edge.fromId);
                    const target = byId.get(edge.toId);
                    if (source && target) {
                        await editor.addConnection(new StoryConnection(source, target, edge.kind));
                    }
                }
                await arrange.layout({ options: { 'elk.direction': 'RIGHT' } });

                // Stored positions win over auto-layout for the events that have them.
                const stored = new Map(layout.map(e => [`${e.file} ${e.eventName}`.toLowerCase(), e]));
                for (const node of editor.getNodes()) {
                    if (node.dto.kind !== 'Event') { continue; }
                    const key = `${baseName(node.dto.threadUri)} ${node.dto.label}`.toLowerCase();
                    const entry = stored.get(key);
                    if (entry) { await area.translate(node.id, { x: entry.x, y: entry.y }); }
                }

                void AreaExtensions.zoomAt(area, editor.getNodes());
            } finally {
                applyingServerGraph = false;
            }
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
        destroy(): void {
            area.destroy();
        },
    };
}

// ── Node / connection / socket components ────────────────────────────────────────────────────────

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
    .sub {
        font-size: 9px;
        opacity: 0.75;
        text-align: center;
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
    }

    &.lc-Waiting  { border-color: var(--vscode-charts-blue,   #3794ff); }
    &.lc-Armed    { border-color: var(--vscode-charts-green,  #89d185); }
    &.lc-Fired    { border-color: var(--vscode-charts-purple, #b180d7); }
    &.lc-Disabled { border-color: var(--vscode-charts-red,    #f14c4c); }
    &.unreachable { opacity: 0.4; }
    &.untested    { border-style: dashed; }

    &.k-AndJunction, &.k-OrJunction {
        border-radius: 50%;
        padding: 0;
        align-items: center;
    }
    &.k-OrJunction { border-radius: 4px; transform: rotate(45deg); }
    &.k-OrJunction .title { transform: rotate(-45deg); }
    &.k-Portal, &.k-TacticalPlot {
        border-style: dashed;
        border-radius: 12px;
    }

    .input-socket  { position: absolute; left: -7px;  top: 50%; transform: translateY(-50%); }
    .output-socket { position: absolute; right: -7px; top: 50%; transform: translateY(-50%); }
    &.k-OrJunction .input-socket  { transform: translateY(-50%) rotate(-45deg); }
    &.k-OrJunction .output-socket { transform: translateY(-50%) rotate(-45deg); }
`;

const { RefSocket } = Presets.classic;

function StoryNodeView(props: { data: StoryNode; emit: RenderEmit<Schemes> }): JSX.Element {
    const dto = props.data.dto;
    const input = props.data.inputs['in'];
    const output = props.data.outputs['out'];
    const untested = untestedTypes.has(dto.eventType ?? '') || untestedTypes.has(dto.rewardType ?? '');
    const classes = [
        'k-' + dto.kind,
        dto.kind === 'Event' ? 'lc-' + (dto.lifecycle ?? 'Inactive') : '',
        dto.reachable ? '' : 'unreachable',
        untested ? 'untested' : '',
    ].filter(c => c).join(' ');
    const title = dto.kind === 'AndJunction' ? 'AND'
        : dto.kind === 'OrJunction' ? 'OR'
        : dto.label;
    const sub = dto.kind === 'Event'
        ? [dto.eventType, dto.rewardType].filter(v => v).join(' → ')
        : '';

    return (
        <NodeBox
            className={classes}
            selected={props.data.selected}
            $w={props.data.width}
            $h={props.data.height}
            onDoubleClick={() => {
                if (dto.threadUri) {
                    vscode.postMessage({ type: 'openXml', threadUri: dto.threadUri, line: dto.line ?? 0 });
                }
            }}
            data-testid="node"
        >
            <div className="title" title={dto.label}>{title}</div>
            {sub ? <div className="sub" title={sub}>{sub}</div> : null}
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
    &.k-Tactical path { stroke: var(--vscode-charts-yellow, #cca700); stroke-dasharray: 8 4; }
    &.k-Flag path     { stroke: var(--vscode-charts-blue, #3794ff);   stroke-dasharray: 2 4; }
`;

function StoryConnectionView(props: { data: StoryConnection }): JSX.Element | null {
    const { path } = Presets.classic.useConnection();
    if (!path) { return null; }
    return (
        <ConnSvg className={'k-' + (props.data.kind ?? '')} data-testid="connection">
            <path d={path} />
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

    .main { flex: 1; display: flex; overflow: hidden; }
    .canvas { flex: 1; position: relative; overflow: hidden; }
    .status { padding: 16px; color: var(--vscode-disabledForeground); }

    .detail {
        width: 320px;
        flex-shrink: 0;
        border-left: 1px solid var(--vscode-panel-border);
        background: var(--vscode-sideBar-background);
        overflow-y: auto;
        padding: 8px;
    }
    .detail h2 { font-size: 13px; margin-bottom: 6px; word-break: break-all; }
    .detail .row { display: flex; gap: 4px; align-items: center; margin: 3px 0; font-size: 12px; }
    .detail .row label { min-width: 80px; color: var(--vscode-descriptionForeground); flex-shrink: 0; }
    .detail .row input[type=text], .detail .row select { flex: 1; }
    .detail .section { margin-top: 10px; font-weight: bold; font-size: 12px; }
    .detail .chips { display: flex; flex-wrap: wrap; gap: 4px; margin: 3px 0; align-items: center; }
    .detail .chip {
        display: inline-flex;
        gap: 2px;
        align-items: center;
        padding: 1px 4px;
        border: 1px solid var(--vscode-panel-border);
        border-radius: 3px;
        font-size: 11px;
    }
    .detail .chip button { padding: 0 3px; background: transparent; color: var(--vscode-descriptionForeground); }
    .detail .chip button:hover { color: var(--vscode-errorForeground, #f44); }
    .detail .or-sep { color: var(--vscode-descriptionForeground); font-style: italic; font-size: 11px; }
    .detail .actions { margin-top: 12px; display: flex; gap: 6px; flex-wrap: wrap; }
    .detail p.hint { font-size: 11px; color: var(--vscode-descriptionForeground); margin-top: 6px; }

    .simbar {
        display: flex;
        gap: 12px;
        max-height: 180px;
        overflow: hidden;
        padding: 6px 8px;
        background: var(--vscode-sideBar-background);
        border-top: 1px solid var(--vscode-panel-border);
        flex-shrink: 0;
        font-size: 12px;
    }
    .sim-section { min-width: 140px; overflow-y: auto; }
    .sim-section.sim-log { flex: 1; font-size: 11px; color: var(--vscode-descriptionForeground); }
    .sim-head { font-weight: bold; margin-bottom: 3px; }
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

interface VirtualDetail { kind: 'virtual'; label: string; description: string; }
interface EventDetail { kind: 'event'; node: StoryNodeDetailDto; }
type Detail = VirtualDetail | EventDetail | null;

const VIRTUAL_DESCRIPTIONS: Record<string, string> = {
    AndJunction: 'AND junction — every input on this prereq line must fire.',
    OrJunction: 'OR junction — any one prereq line arms the event.',
    Portal: 'Portal — stands in for a cross-file target event.',
    TacticalPlot: 'Tactical plot manifest attached to this campaign.',
};

function App(): JSX.Element {
    const containerRef = useRef<HTMLDivElement>(null);
    const editorRef = useRef<EditorHandle | null>(null);
    const filtersRef = useRef<GraphFilters>({ ...EMPTY_FILTERS });
    const pendingGraphRef = useRef<{
        nodes: StoryGraphNodeDto[]; edges: StoryGraphEdgeDto[]; layout: StoryLayoutEntry[];
    } | null>(null);

    const [filters, setFiltersState] = useState<GraphFilters>({ ...EMPTY_FILTERS });
    const [branches, setBranches] = useState<string[]>([]);
    const [threads, setThreads] = useState<string[]>([]);
    const [eventTypes, setEventTypes] = useState<string[]>([]);
    const [rewardTypes, setRewardTypes] = useState<string[]>([]);
    const [detail, setDetail] = useState<Detail>(null);
    const [status, setStatus] = useState<string | null>('Loading story graph…');
    const [createOpen, setCreateOpen] = useState(false);
    const [simState, setSimState] = useState<SimState | null>(null);
    const simRef = useRef<SimState | null>(null);

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
        vscode.postMessage({ type: 'fetch', filters: next });
    }, []);

    const applyGraph = useCallback((
        nodes: StoryGraphNodeDto[], edges: StoryGraphEdgeDto[], layout: StoryLayoutEntry[]
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
            void handle.setGraph(nodes, edges, layout);
        } else {
            pendingGraphRef.current = { nodes, edges, layout };
        }
    }, []);

    useEffect(() => {
        onNodePicked = node => {
            if (node.dto.kind === 'Event') {
                vscode.postMessage({ type: 'detail', nodeId: node.dto.id });
            } else {
                setDetail({
                    kind: 'virtual',
                    label: node.dto.label || node.dto.kind,
                    description: VIRTUAL_DESCRIPTIONS[node.dto.kind] ?? node.dto.kind,
                });
            }
        };

        const onMessage = (event: MessageEvent): void => {
            const msg = event.data as { type: string; [key: string]: unknown };
            switch (msg.type) {
                case 'schema': {
                    untestedTypes.clear();
                    for (const name of (msg.untestedEventTypes as string[] | undefined) ?? []) { untestedTypes.add(name); }
                    for (const name of (msg.untestedRewardTypes as string[] | undefined) ?? []) { untestedTypes.add(name); }
                    setEventTypes((msg.eventTypes as string[] | undefined) ?? []);
                    setRewardTypes((msg.rewardTypes as string[] | undefined) ?? []);
                    break;
                }
                case 'graph':
                    applyGraph(
                        (msg.nodes as StoryGraphNodeDto[] | undefined) ?? [],
                        (msg.edges as StoryGraphEdgeDto[] | undefined) ?? [],
                        (msg.layout as StoryLayoutEntry[] | undefined) ?? []);
                    // A running simulation keeps painting its lifecycles over fresh renders.
                    if (simRef.current?.running) { applySimOverlay(simRef.current); }
                    break;
                case 'simState':
                    applySimOverlay((msg.state as SimState | null) ?? null);
                    break;
                case 'simChanged':
                    sendSim('getState');
                    break;
                case 'detail': {
                    const node = msg.node as StoryNodeDetailDto | null;
                    setDetail(node
                        ? { kind: 'event', node }
                        : { kind: 'virtual', label: 'Unavailable', description: String(msg.error ?? 'No detail available.') });
                    break;
                }
                case 'invalidate':
                    vscode.postMessage({ type: 'fetch', filters: filtersRef.current });
                    break;
                case 'error':
                    setStatus('⚠ ' + String(msg.message));
                    break;
            }
        };
        window.addEventListener('message', onMessage);
        vscode.postMessage({ type: 'ready' });
        return () => window.removeEventListener('message', onMessage);
    }, [applyGraph, applySimOverlay]);

    useEffect(() => {
        const container = containerRef.current;
        if (!container) { return; }
        let disposed = false;
        let handle: EditorHandle | null = null;
        void createEditor(container).then(created => {
            if (disposed) { created.destroy(); return; }
            handle = created;
            editorRef.current = created;
            const pending = pendingGraphRef.current;
            if (pending) {
                pendingGraphRef.current = null;
                void created.setGraph(pending.nodes, pending.edges, pending.layout);
            }
        });
        return () => {
            disposed = true;
            editorRef.current = null;
            handle?.destroy();
        };
    }, []);

    const setFilter = (patch: Partial<GraphFilters>): void => fetchGraph({ ...filtersRef.current, ...patch });

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
            <div className="toolbar">
                <input
                    type="text" placeholder="Filter event names…" value={filters.nameFilter}
                    onChange={e => setFilter({ nameFilter: e.target.value })}
                />
                <select value={filters.branch} onChange={e => setFilter({ branch: e.target.value })} title="Branch">
                    <option value="">All branches</option>
                    {branches.map(b => <option key={b} value={b}>{b}</option>)}
                </select>
                <select value={filters.lifecycle} onChange={e => setFilter({ lifecycle: e.target.value })} title="Lifecycle">
                    <option value="">Any lifecycle</option>
                    {LIFECYCLES.map(l => <option key={l} value={l}>{l}</option>)}
                </select>
                {filters.reachableFrom
                    ? <button onClick={() => setFilter({ reachableFrom: '' })} title="Clear the reachable-from filter">⭯ Whole graph</button>
                    : null}
                <button className="primary" onClick={() => setCreateOpen(v => !v)} title="Create a new story event">
                    ＋ Event
                </button>
                {simState?.running
                    ? <button className="danger" onClick={() => sendSim('stop')} title="End the simulation">■ Stop sim</button>
                    : <button onClick={() => sendSim('start')} title="Simulate this campaign's story">▶ Simulate</button>}
                <button onClick={() => editorRef.current?.fit()} title="Fit graph to view">Fit</button>
            </div>
            {createOpen ? (
                <CreateBar
                    eventTypes={eventTypes}
                    threads={threads}
                    onClose={() => setCreateOpen(false)}
                />
            ) : null}
            <div className="main">
                <div className="canvas" ref={containerRef} style={{ display: status ? 'none' : undefined }} />
                {status ? <p className="status">{status}</p> : null}
                {detail ? (
                    <DetailPanel
                        detail={detail}
                        eventTypes={eventTypes}
                        rewardTypes={rewardTypes}
                        onReachableFrom={id => setFilter({ reachableFrom: id })}
                        onClosed={() => setDetail(null)}
                    />
                ) : null}
            </div>
            {simState?.running ? <SimBar state={simState} /> : null}
            <div className="legend">
                <span><span className="swatch" style={{ borderColor: 'var(--vscode-disabledForeground, #888)' }} />Inactive</span>
                <span><span className="swatch" style={{ borderColor: 'var(--vscode-charts-blue, #3794ff)' }} />Waiting</span>
                <span><span className="swatch" style={{ borderColor: 'var(--vscode-charts-green, #89d185)' }} />Armed</span>
                <span><span className="swatch" style={{ borderColor: 'var(--vscode-charts-purple, #b180d7)' }} />Fired</span>
                <span><span className="swatch" style={{ borderColor: 'var(--vscode-charts-red, #f14c4c)' }} />Disabled</span>
                <span>◇ OR</span><span>○ AND</span><span>dashed = portal / tactical / untested</span>
                <span>drag socket→socket = prereq</span>
            </div>
        </Shell>
    );
}

/** The running simulation: clock, flag inspector, intervention queue, and the step log. */
function SimBar(props: { state: SimState }): JSX.Element {
    const state = props.state;
    const [advanceBy, setAdvanceBy] = useState('10');
    const [flagName, setFlagName] = useState('');

    return (
        <div className="simbar">
            <div className="sim-section">
                <div className="sim-head">⏱ {state.clock.toFixed(0)}s</div>
                <div className="sim-row">
                    <input type="text" value={advanceBy} onChange={e => setAdvanceBy(e.target.value)} title="Seconds" />
                    <button onClick={() => {
                        const seconds = Number(advanceBy);
                        if (seconds > 0) { sendSim('advanceClock', { seconds }); }
                    }}>Advance</button>
                </div>
            </div>
            <div className="sim-section">
                <div className="sim-head">Flags</div>
                {state.flags.map(f => (
                    <div className="sim-row" key={f.name}>
                        <span className="sim-name" title={f.name}>{f.name}</span>
                        <button onClick={() => sendSim('setFlag', { flag: f.name, value: f.value !== 0 ? 0 : 1 })}>
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
                {state.interventions.length === 0 ? <div className="sim-row">nothing — story exhausted</div> : null}
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
            <div className="sim-section sim-log">
                <div className="sim-head">Log</div>
                {state.log.slice(-30).map((line, i) => <div key={i}>{line}</div>)}
            </div>
        </div>
    );
}

function CreateBar(props: { eventTypes: string[]; threads: string[]; onClose(): void }): JSX.Element {
    const [name, setName] = useState('New_Event');
    const [eventType, setEventType] = useState('');
    const [thread, setThread] = useState(props.threads[0] ?? '');

    const create = (): void => {
        if (!name.trim() || !thread) { return; }
        sendCommand({
            kind: 'createEvent',
            threadUri: thread,
            newName: name.trim(),
            eventType: eventType || null,
        });
        props.onClose();
    };

    return (
        <div className="toolbar">
            <span>New event:</span>
            <input type="text" value={name} onChange={e => setName(e.target.value)}
                onKeyDown={e => { if (e.key === 'Enter') { create(); } }} />
            <select value={eventType} onChange={e => setEventType(e.target.value)} title="Event type">
                <option value="">(no event type)</option>
                {props.eventTypes.map(t => <option key={t} value={t}>{t}</option>)}
            </select>
            <select value={thread} onChange={e => setThread(e.target.value)} title="Thread file">
                {props.threads.length === 0 ? <option value="">(no thread files)</option> : null}
                {props.threads.map(t => <option key={t} value={t}>{baseName(t)}</option>)}
            </select>
            <button className="primary" onClick={create} disabled={!thread}>Create</button>
            <button onClick={props.onClose}>Cancel</button>
        </div>
    );
}

function DetailPanel(props: {
    detail: Exclude<Detail, null>;
    eventTypes: string[];
    rewardTypes: string[];
    onReachableFrom(id: string): void;
    onClosed(): void;
}): JSX.Element {
    if (props.detail.kind === 'virtual') {
        return (
            <div className="detail">
                <h2>{props.detail.label}</h2>
                <p>{props.detail.description}</p>
            </div>
        );
    }
    return (
        <EventForm
            key={props.detail.node.id}
            node={props.detail.node}
            eventTypes={props.eventTypes}
            rewardTypes={props.rewardTypes}
            onReachableFrom={props.onReachableFrom}
            onClosed={props.onClosed}
        />
    );
}

/** Editable property view. Every commit is a command; the panel re-fetches detail on success. */
function EventForm(props: {
    node: StoryNodeDetailDto;
    eventTypes: string[];
    rewardTypes: string[];
    onReachableFrom(id: string): void;
    onClosed(): void;
}): JSX.Element {
    const node = props.node;
    const target = { threadUri: node.threadUri, eventName: node.name };
    const refresh = node.id;

    const [name, setName] = useState(node.name);
    const [branch, setBranch] = useState(node.branch ?? '');
    const [dialog, setDialog] = useState(node.storyDialog ?? '');
    const [newToken, setNewToken] = useState('');

    const commitTag = (kind: string, value: string): void =>
        sendCommand({ kind, ...target, value: value || null }, undefined, refresh);

    const commitParam = (paramKind: 'event' | 'reward', position: number, value: string): void =>
        sendCommand({
            kind: 'setParams', ...target, paramKind,
            params: [{ position, value: value || null }],
        }, undefined, refresh);

    return (
        <div className="detail">
            <h2>{node.name}</h2>
            <div className="row">
                <label>Name</label>
                <input type="text" value={name} onChange={e => setName(e.target.value)} />
                <button
                    disabled={name.trim() === node.name || !name.trim()}
                    onClick={() => sendCommand({ kind: 'renameEvent', eventName: node.name, newName: name.trim() })}
                    title="Renames the event and every reference, campaign-wide"
                >Rename</button>
            </div>
            <div className="row">
                <label>Event type</label>
                <select value={node.eventType ?? ''} onChange={e => commitTag('setEventType', e.target.value)}>
                    <option value="">(none)</option>
                    {[...new Set([node.eventType ?? '', ...props.eventTypes])].filter(t => t)
                        .map(t => <option key={t} value={t}>{t}</option>)}
                </select>
            </div>
            <ParamRows kind="event" params={node.eventParams} onCommit={commitParam} />
            <div className="row">
                <label>Reward type</label>
                <select value={node.rewardType ?? ''} onChange={e => commitTag('setRewardType', e.target.value)}>
                    <option value="">(none)</option>
                    {[...new Set([node.rewardType ?? '', ...props.rewardTypes])].filter(t => t)
                        .map(t => <option key={t} value={t}>{t}</option>)}
                </select>
            </div>
            <ParamRows kind="reward" params={node.rewardParams} onCommit={commitParam} />
            <div className="row">
                <label>Branch</label>
                <input
                    type="text" value={branch}
                    onChange={e => setBranch(e.target.value)}
                    onBlur={() => { if (branch !== (node.branch ?? '')) { commitTag('setBranch', branch); } }}
                />
            </div>
            <div className="row">
                <label>Perpetual</label>
                <input
                    type="checkbox" checked={node.perpetual}
                    onChange={e => sendCommand({ kind: 'setPerpetual', ...target, flag: e.target.checked }, undefined, refresh)}
                />
            </div>
            <div className="row">
                <label>Dialog</label>
                <input
                    type="text" value={dialog}
                    onChange={e => setDialog(e.target.value)}
                    onBlur={() => { if (dialog !== (node.storyDialog ?? '')) { commitTag('setDialog', dialog); } }}
                />
            </div>

            <div className="section">Prerequisites</div>
            {node.prereqGroups.map((group, gi) => (
                <div key={gi}>
                    {gi > 0 ? <div className="or-sep">— or —</div> : null}
                    <div className="chips">
                        {group.map(token => (
                            <span className="chip" key={token}>
                                {token}
                                <button
                                    title="Remove prereq token"
                                    onClick={() => sendCommand(
                                        { kind: 'removePrereq', ...target, groupIndex: gi, token },
                                        undefined, refresh)}
                                >×</button>
                            </span>
                        ))}
                        <button
                            title="AND another event onto this line"
                            onClick={() => {
                                const token = newToken.trim();
                                if (token) {
                                    sendCommand({ kind: 'addPrereq', ...target, groupIndex: gi, token }, undefined, refresh);
                                    setNewToken('');
                                }
                            }}
                        >+&amp;</button>
                    </div>
                </div>
            ))}
            <div className="row">
                <input
                    type="text" placeholder="Event name…" value={newToken}
                    onChange={e => setNewToken(e.target.value)}
                />
                <button
                    title="Add as a new OR line"
                    onClick={() => {
                        const token = newToken.trim();
                        if (token) {
                            sendCommand({ kind: 'addPrereq', ...target, token }, undefined, refresh);
                            setNewToken('');
                        }
                    }}
                >+ OR</button>
            </div>
            <p className="hint">Dragging a connection between two events also adds an OR-line prereq.</p>

            <div className="actions">
                <button onClick={() => vscode.postMessage({ type: 'openXml', threadUri: node.threadUri, line: node.line })}>
                    Open XML
                </button>
                <button onClick={() => props.onReachableFrom(node.id)}>
                    Reachable from here
                </button>
                <button
                    className="danger"
                    onClick={() => {
                        sendCommand({ kind: 'deleteEvent', ...target },
                            `Delete story event '${node.name}'? References to it are not removed.`);
                        props.onClosed();
                    }}
                >Delete event</button>
            </div>
        </div>
    );
}

function ParamRows(props: {
    kind: 'event' | 'reward';
    params: { position: number; value: string }[];
    onCommit(kind: 'event' | 'reward', position: number, value: string): void;
}): JSX.Element {
    const [values, setValues] = useState<Record<number, string>>(
        Object.fromEntries(props.params.map(p => [p.position, p.value])));
    const [newValue, setNewValue] = useState('');
    const nextFree = props.params.length
        ? Math.max(...props.params.map(p => p.position)) + 1
        : 0;

    const addParam = (): void => {
        if (newValue.trim()) {
            props.onCommit(props.kind, nextFree, newValue.trim());
            setNewValue('');
        }
    };

    return (
        <>
            {props.params.map(p => (
                <div className="row" key={p.position}>
                    <label>{props.kind === 'event' ? 'Param' : 'Reward'} {p.position + 1}</label>
                    <input
                        type="text" value={values[p.position] ?? ''}
                        onChange={e => setValues(v => ({ ...v, [p.position]: e.target.value }))}
                        onBlur={() => {
                            if ((values[p.position] ?? '') !== p.value) {
                                props.onCommit(props.kind, p.position, values[p.position] ?? '');
                            }
                        }}
                    />
                </div>
            ))}
            <div className="row">
                <label>{props.kind === 'event' ? 'Param' : 'Reward'} {nextFree + 1}</label>
                <input
                    type="text" placeholder="New value…" value={newValue}
                    onChange={e => setNewValue(e.target.value)}
                    onKeyDown={e => { if (e.key === 'Enter') { addParam(); } }}
                />
                <button title={`Add ${props.kind} param ${nextFree + 1}`} onClick={addParam}>＋</button>
            </div>
        </>
    );
}

const rootElement = document.getElementById('root');
if (rootElement) {
    createRoot(rootElement).render(<App />);
}

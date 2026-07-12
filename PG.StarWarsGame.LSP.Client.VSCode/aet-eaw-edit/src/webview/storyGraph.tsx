// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Story graph webview app: rete.js renders and lays out the graph (auto-arrange = elk.js),
// React renders toolbar/detail chrome. The extension side (storyGraphPanel.ts) owns all LSP
// traffic; this file only exchanges postMessage envelopes with it.

import { useCallback, useEffect, useRef, useState } from 'react';
import { createRoot } from 'react-dom/client';
import { ClassicPreset, GetSchemes, NodeEditor } from 'rete';
import { AreaExtensions, AreaPlugin } from 'rete-area-plugin';
import { AutoArrangePlugin, Presets as ArrangePresets } from 'rete-auto-arrange-plugin';
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
interface GraphFilters { nameFilter: string; branch: string; lifecycle: string; reachableFrom: string; }

const EMPTY_FILTERS: GraphFilters = { nameFilter: '', branch: '', lifecycle: '', reachableFrom: '' };

/** Event/reward type names flagged `untested` in the schema — set once, read during render. */
const untestedTypes = new Set<string>();

// ── Rete setup ───────────────────────────────────────────────────────────────────────────────────

const flowSocket = new ClassicPreset.Socket('flow');

class StoryNode extends ClassicPreset.Node {
    width: number;
    height: number;

    constructor(public readonly dto: StoryGraphNodeDto, hasInputs: boolean, hasOutputs: boolean) {
        super(dto.label);
        if (hasInputs) { this.addInput('in', new ClassicPreset.Input(flowSocket)); }
        if (hasOutputs) { this.addOutput('out', new ClassicPreset.Output(flowSocket)); }
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
    setGraph(nodes: StoryGraphNodeDto[], edges: StoryGraphEdgeDto[]): Promise<void>;
    fit(): void;
    destroy(): void;
}

/** Set by the React app before the editor exists; invoked from the area's nodepicked pipe. */
let onNodePicked: (node: StoryNode) => void = () => { /* replaced by App */ };

async function createEditor(container: HTMLElement): Promise<EditorHandle> {
    const editor = new NodeEditor<Schemes>();
    const area = new AreaPlugin<Schemes, AreaExtra>(container);
    const render = new ReactPlugin<Schemes, AreaExtra>({ createRoot });
    const arrange = new AutoArrangePlugin<Schemes>();

    render.addPreset(Presets.classic.setup({
        customize: {
            node: () => StoryNodeView,
            connection: () => StoryConnectionView,
            socket: () => StorySocketView,
        },
    }));
    arrange.addPreset(ArrangePresets.classic.setup());

    editor.use(area);
    area.use(render);
    area.use(arrange);

    AreaExtensions.selectableNodes(area, AreaExtensions.selector(), {
        accumulating: AreaExtensions.accumulateOnCtrl(),
    });
    AreaExtensions.simpleNodesOrder(area);

    area.addPipe(context => {
        if (context.type === 'nodepicked') {
            const node = editor.getNode(context.data.id);
            if (node) { onNodePicked(node); }
        }
        return context;
    });

    return {
        async setGraph(nodes: StoryGraphNodeDto[], edges: StoryGraphEdgeDto[]): Promise<void> {
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
            void AreaExtensions.zoomAt(area, editor.getNodes());
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

    .input-socket  { position: absolute; left: -5px;  top: 50%; transform: translateY(-50%); }
    .output-socket { position: absolute; right: -5px; top: 50%; transform: translateY(-50%); }
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
        <ConnSvg className={'k-' + props.data.kind} data-testid="connection">
            <path d={path} />
        </ConnSvg>
    );
}

const SocketDot = styled.div`
    width: 10px;
    height: 10px;
    border-radius: 50%;
    background: var(--vscode-charts-foreground, #999);
    opacity: 0.5;
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

    .main { flex: 1; display: flex; overflow: hidden; }
    .canvas { flex: 1; position: relative; overflow: hidden; }
    .status { padding: 16px; color: var(--vscode-disabledForeground); }

    .detail {
        width: 300px;
        flex-shrink: 0;
        border-left: 1px solid var(--vscode-panel-border);
        background: var(--vscode-sideBar-background);
        overflow-y: auto;
        padding: 8px;
    }
    .detail h2 { font-size: 13px; margin-bottom: 6px; word-break: break-all; }
    .detail dl { display: grid; grid-template-columns: auto 1fr; gap: 2px 8px; font-size: 12px; }
    .detail dt { color: var(--vscode-descriptionForeground); white-space: nowrap; }
    .detail dd { word-break: break-all; }
    .detail .section { margin-top: 8px; font-weight: bold; font-size: 12px; }
    .detail ul { list-style: none; font-size: 12px; }
    .detail li { padding: 1px 0; word-break: break-all; }
    .detail li.or-sep { color: var(--vscode-descriptionForeground); font-style: italic; }
    .detail .actions { margin-top: 10px; display: flex; gap: 6px; }

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
    const pendingGraphRef = useRef<{ nodes: StoryGraphNodeDto[]; edges: StoryGraphEdgeDto[] } | null>(null);

    const [filters, setFiltersState] = useState<GraphFilters>({ ...EMPTY_FILTERS });
    const [branches, setBranches] = useState<string[]>([]);
    const [detail, setDetail] = useState<Detail>(null);
    const [status, setStatus] = useState<string | null>('Loading story graph…');

    const fetchGraph = useCallback((next: GraphFilters) => {
        filtersRef.current = next;
        setFiltersState(next);
        vscode.postMessage({ type: 'fetch', filters: next });
    }, []);

    const applyGraph = useCallback((nodes: StoryGraphNodeDto[], edges: StoryGraphEdgeDto[]) => {
        setBranches(previous => {
            const found = [...new Set(nodes.map(n => n.branch).filter((b): b is string => !!b))].sort();
            // Keep a currently-filtered branch selectable even when the filter removed its nodes.
            const active = filtersRef.current.branch;
            return active && !found.includes(active) ? [...found, active].sort() : found;
        });
        if (!nodes.length) {
            setStatus('No events match the current filters.');
            return;
        }
        setStatus(null);
        const handle = editorRef.current;
        if (handle) {
            pendingGraphRef.current = null;
            void handle.setGraph(nodes, edges);
        } else {
            pendingGraphRef.current = { nodes, edges };
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
                    break;
                }
                case 'graph':
                    applyGraph(
                        (msg.nodes as StoryGraphNodeDto[] | undefined) ?? [],
                        (msg.edges as StoryGraphEdgeDto[] | undefined) ?? []);
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
    }, [applyGraph]);

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
                void created.setGraph(pending.nodes, pending.edges);
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
                <button onClick={() => editorRef.current?.fit()} title="Fit graph to view">Fit</button>
            </div>
            <div className="main">
                <div className="canvas" ref={containerRef} style={{ display: status ? 'none' : undefined }} />
                {status ? <p className="status">{status}</p> : null}
                {detail ? <DetailPanel detail={detail} onReachableFrom={id => setFilter({ reachableFrom: id })} /> : null}
            </div>
            <div className="legend">
                <span><span className="swatch" style={{ borderColor: 'var(--vscode-disabledForeground, #888)' }} />Inactive</span>
                <span><span className="swatch" style={{ borderColor: 'var(--vscode-charts-blue, #3794ff)' }} />Waiting</span>
                <span><span className="swatch" style={{ borderColor: 'var(--vscode-charts-green, #89d185)' }} />Armed</span>
                <span><span className="swatch" style={{ borderColor: 'var(--vscode-charts-purple, #b180d7)' }} />Fired</span>
                <span><span className="swatch" style={{ borderColor: 'var(--vscode-charts-red, #f14c4c)' }} />Disabled</span>
                <span>◇ OR</span><span>○ AND</span><span>dashed = portal / tactical / untested</span>
                <span>dimmed = unreachable</span>
            </div>
        </Shell>
    );
}

function DetailPanel(props: { detail: Exclude<Detail, null>; onReachableFrom(id: string): void }): JSX.Element {
    if (props.detail.kind === 'virtual') {
        return (
            <div className="detail">
                <h2>{props.detail.label}</h2>
                <p>{props.detail.description}</p>
            </div>
        );
    }
    const node = props.detail.node;
    const rows: [string, string][] = [];
    const push = (label: string, value: string | number | null | undefined): void => {
        if (value !== null && value !== undefined && value !== '') { rows.push([label, String(value)]); }
    };
    push('Event type', node.eventType);
    push('Event filter', node.eventFilter);
    for (const p of node.eventParams ?? []) { push(`Param ${p.position}`, p.value); }
    push('Reward type', node.rewardType);
    for (const p of node.rewardParams ?? []) { push(`Reward ${p.position}`, p.value); }
    push('Branch', node.branch);
    push('Perpetual', node.perpetual ? 'yes' : '');
    push('Dialog', node.storyDialog);
    push('Chapter', node.storyChapter);

    return (
        <div className="detail">
            <h2>{node.name}</h2>
            <dl>
                {rows.map(([label, value], i) => [
                    <dt key={`t${i}`}>{label}</dt>,
                    <dd key={`d${i}`}>{value}</dd>,
                ])}
            </dl>
            {(node.prereqGroups ?? []).length ? (
                <>
                    <div className="section">Prerequisites</div>
                    <ul>
                        {node.prereqGroups.flatMap((group, i) => [
                            ...(i > 0 ? [<li className="or-sep" key={`s${i}`}>— or —</li>] : []),
                            <li key={`g${i}`}>{group.join(' + ')}</li>,
                        ])}
                    </ul>
                </>
            ) : null}
            {(node.tags ?? []).length ? (
                <>
                    <div className="section">Raw tags</div>
                    <ul>
                        {node.tags.map((tag, i) => <li key={i}>{tag.name} = {tag.value}</li>)}
                    </ul>
                </>
            ) : null}
            <div className="actions">
                <button onClick={() => vscode.postMessage({ type: 'openXml', threadUri: node.threadUri, line: node.line })}>
                    Open XML
                </button>
                <button onClick={() => props.onReachableFrom(node.id)}>
                    Reachable from here
                </button>
            </div>
        </div>
    );
}

const rootElement = document.getElementById('root');
if (rootElement) {
    createRoot(rootElement).render(<App />);
}

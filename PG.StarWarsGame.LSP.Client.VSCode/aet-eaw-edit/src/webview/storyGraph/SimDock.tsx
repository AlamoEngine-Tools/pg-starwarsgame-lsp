// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The simulation's dock, at the dock's three levels (issue #90).
//
// Header: the tick and what the story is waiting on - state and warnings. Content: the inventories -
// decisions the author owes, the world's facts, the flags, the scripts. Foot: the transport, which
// is the preview's animation player with ticks for frames. A row's detail opens BESIDE the dock as
// a stage flyout, never by unfolding inside the list.
//
// Everything here reads the flat state document and sends a request; storyGraph.tsx owns the state
// and the canvas, and simModel.ts owns the arithmetic.

import {useEffect, useMemo, useState} from 'react';

import type {
    StoryGraphEdgeDto, StoryGraphNodeDto, StorySimInterventionDto, StorySimNodeStateDto, StorySimStateDto,
    StorySimStepDto, StorySimWorldChangeDto,
} from '../../protocol/story';
import {IconButton} from '../shared/Button';
import {DockSection} from '../shared/DockSection';
import {Icon, type IconName} from '../shared/Icon';
import {ProblemsPanel} from '../shared/ProblemsPanel';

import {
    armingLines, type DecisionKind, describeChange, filterTrace, groupDecisions, labelFor, pickerCandidates,
    type SimPace, traceRows,
} from './simModel';

// ── What the dock talks to ───────────────────────────────────────────────────

/** The requests the dock sends; the app routes them to the extension. */
export interface SimActions {
    tick: () => void;
    back: () => void;
    restart: () => void;
    playPause: () => void;
    runToDecision: () => void;
    satisfy: (nodeId: string) => void;
    world: (change: StorySimWorldChangeDto) => void;
    luaNotify: (id: string) => void;
    setFlag: (name: string, value: number) => void;
    setBreakpoints: (nodeIds: string[], onGates: boolean) => void;
    centerNode: (nodeId: string) => void;
    copy: (text: string) => void;
}

/** What the flyout beside the dock is showing. */
export type SimSelection =
    | { kind: 'decision'; nodeId: string }
    | { kind: 'node'; nodeId: string }
    | { kind: 'planet'; name: string }
    | { kind: 'flag'; name: string }
    | { kind: 'script'; scriptUri: string };

/** The three canvas lenses: each shows one slice of the running story. */
export interface SimLenses {
    /** Dim every edge and node the story has not run through. */
    activeOnly: boolean;
    /** Take the flow tints off the edges. */
    hideFlow: boolean;
    /** Take the script states off the canvas. */
    hideLua: boolean;
}

/** The graph the flyout reads arming lines off. */
export interface SimGraph {
    nodes: readonly StoryGraphNodeDto[];
    edges: readonly StoryGraphEdgeDto[];
}

const DECISION_ICON: Record<DecisionKind, IconName> = {
    world: 'world', tactical: 'tactical', lua: 'script', assume: 'decision',
};

const DECISION_TITLE: Record<DecisionKind, string> = {
    world: 'World', tactical: 'Battles', lua: 'Scripts', assume: 'Assumptions',
};

function shortId(nodeId: string): string {
    return labelFor(nodeId, () => undefined);
}

// ── Header ───────────────────────────────────────────────────────────────────

/**
 * The dock header's simulation chip: the tick, then the one thing that stops the clock - a
 * breakpoint hit, or decisions the author owes. Pressing it opens that thing beside the dock.
 */
export function SimHeaderChip(props: {
    state: StorySimStateDto;
    labelOf: (id: string) => string | undefined;
    onSelect: (selection: SimSelection) => void;
}): React.JSX.Element {
    const {state} = props;
    const owed = state.interventions.length;
    if (state.haltedAt) {
        const name = labelFor(state.haltedAt, props.labelOf);
        return (
            <button
                type="button"
                className="sim-chip halted"
                title={`Halted at ${name} - press to show it`}
                onClick={() => props.onSelect({kind: 'node', nodeId: state.haltedAt!})}
            >
                <span className="sim-chip-tick">t{state.tick}</span>
                <span className="sim-chip-sep"/>
                <Icon name="breakpoint" size={13}/>
                <span className="sim-chip-text">{name}</span>
            </button>
        );
    }
    if (owed > 0) {
        const first = state.interventions[0];
        return (
            <button
                type="button"
                className="sim-chip owed"
                title={`${owed} decision${owed === 1 ? '' : 's'} waiting - press to answer`}
                onClick={() => props.onSelect({kind: 'decision', nodeId: first.nodeId})}
            >
                <span className="sim-chip-tick">t{state.tick}</span>
                <span className="sim-chip-sep"/>
                <Icon name="decision" size={13}/>
                <span className="sim-chip-text">{owed}</span>
            </button>
        );
    }
    return (
        <span className="sim-chip" title={`Tick ${state.tick} - ${state.clock.toFixed(0)} s of story time`}>
            <Icon name="trace" size={13}/>
            <span className="sim-chip-tick">t{state.tick}</span>
        </span>
    );
}

// ── Content ──────────────────────────────────────────────────────────────────

/**
 * The inventories. Every row is a button that opens its detail beside the dock; a decision row
 * also carries its one-press answer where the event's own parameters supply one.
 */
export function SimInventory(props: {
    state: StorySimStateDto;
    selection: SimSelection | null;
    labelOf: (id: string) => string | undefined;
    actions: SimActions;
    onSelect: (selection: SimSelection | null) => void;
}): React.JSX.Element {
    const {state, selection, actions} = props;
    const groups = useMemo(() => groupDecisions(state.interventions), [state.interventions]);
    const [folded, setFolded] = useState<Set<string>>(() => new Set());
    const toggle = (id: string): void => setFolded(prev => {
        const next = new Set(prev);
        if (next.has(id)) {
            next.delete(id);
        } else {
            next.add(id);
        }
        return next;
    });
    const isSelected = (s: SimSelection): boolean =>
        selection !== null && JSON.stringify(selection) === JSON.stringify(s);
    const pick = (s: SimSelection): void => props.onSelect(isSelected(s) ? null : s);

    const owners = useMemo(() => {
        const byOwner = new Map<string, number>();
        for (const planet of state.world.planets) {
            const owner = planet.owner ?? 'Neutral';
            byOwner.set(owner, (byOwner.get(owner) ?? 0) + 1);
        }
        return [...byOwner.entries()].sort((a, b) => b[1] - a[1]);
    }, [state.world.planets]);
    const unitCount = state.world.units.reduce((n, u) => n + u.count, 0);

    return (
        <div className="sim-inventory">
            <DockSection
                title="Decisions"
                count={state.interventions.length}
                id="sim-decisions" collapsed={folded.has('sim-decisions')} onToggle={toggle}
            >
                {groups.length === 0 ? (
                    <p className="field-note">
                        {state.haltedAt ? 'Halted at a breakpoint' : 'Nothing waits on you - press play'}
                    </p>
                ) : groups.map(group => (
                    <div className="sim-group" key={group.kind}>
                        <div className="sim-group-title">
                            <Icon name={DECISION_ICON[group.kind]} size={13}/>
                            {DECISION_TITLE[group.kind]}
                        </div>
                        {group.items.map(item => (
                            <DecisionRow
                                key={item.nodeId}
                                item={item}
                                selected={isSelected({kind: 'decision', nodeId: item.nodeId})}
                                labelOf={props.labelOf}
                                actions={actions}
                                onSelect={() => pick({kind: 'decision', nodeId: item.nodeId})}
                            />
                        ))}
                    </div>
                ))}
            </DockSection>

            <DockSection
                title="World"
                count={`${state.world.planets.length} planets`}
                id="sim-world" collapsed={folded.has('sim-world')} onToggle={toggle}
            >
                {owners.map(([owner, count]) => (
                    <div className="sim-row static" key={owner}>
                        <Icon name="planet" size={13}/>
                        <span className="sim-row-name">{owner}</span>
                        <span className="sim-row-value">{count}</span>
                    </div>
                ))}
                <div className="sim-row static">
                    <Icon name="unit" size={13}/>
                    <span className="sim-row-name">Units</span>
                    <span className="sim-row-value">{unitCount}</span>
                </div>
                {state.world.tech.map(t => (
                    <div className="sim-row static" key={'tech-' + t.name}>
                        <Icon name="tech" size={13}/>
                        <span className="sim-row-name">{t.name} tech</span>
                        <span className="sim-row-value">{t.value}</span>
                    </div>
                ))}
                {state.world.credits.map(c => (
                    <div className="sim-row static" key={'credits-' + c.name}>
                        <Icon name="credits" size={13}/>
                        <span className="sim-row-name">{c.name} credits</span>
                        <span className="sim-row-value">{c.value}</span>
                    </div>
                ))}
                <div className="sim-planet-list">
                    {state.world.planets.map(planet => (
                        <button
                            type="button"
                            key={planet.name}
                            className={'sim-row' + (isSelected({kind: 'planet', name: planet.name}) ? ' selected' : '')
                                + (planet.destroyed ? ' gone' : '')}
                            title={`${planet.name} - ${planet.owner ?? 'Neutral'}${planet.corrupted ? ', corrupted' : ''}`}
                            onClick={() => pick({kind: 'planet', name: planet.name})}
                        >
                            <span className="sim-row-name">{planet.name}</span>
                            <span className="sim-row-value">{planet.owner ?? 'Neutral'}</span>
                        </button>
                    ))}
                </div>
            </DockSection>

            <DockSection
                title="Flags"
                count={state.flags.length}
                id="sim-flags" collapsed={folded.has('sim-flags')} onToggle={toggle}
            >
                {/* A row, not a title-end button: the foldable title is itself a button, and a
                    button inside a button is invalid HTML that React refuses to hydrate. */}
                <button
                    type="button"
                    className={'sim-row add' + (isSelected({kind: 'flag', name: ''}) ? ' selected' : '')}
                    title="Set a flag the story has not written yet"
                    onClick={() => pick({kind: 'flag', name: ''})}
                >
                    <Icon name="add" size={13}/>
                    <span className="sim-row-name">Set a flag</span>
                </button>
                {state.flags.map(flag => (
                    <button
                        type="button"
                        key={flag.name}
                        className={'sim-row' + (isSelected({kind: 'flag', name: flag.name}) ? ' selected' : '')}
                        title={`${flag.name} = ${flag.value}`}
                        onClick={() => pick({kind: 'flag', name: flag.name})}
                    >
                        <Icon name="flag" size={13}/>
                        <span className="sim-row-name">{flag.name}</span>
                        <span className="sim-row-value">{flag.value}</span>
                    </button>
                ))}
            </DockSection>

            <DockSection
                title="Scripts"
                count={state.luaStates.length}
                id="sim-scripts" collapsed={folded.has('sim-scripts')} onToggle={toggle}
            >
                {state.luaStates.length === 0 ? <p className="field-note">No campaign script in this plot</p> : null}
                {state.luaStates.map(script => (
                    <button
                        type="button"
                        key={script.scriptUri}
                        className={'sim-row'
                            + (isSelected({kind: 'script', scriptUri: script.scriptUri}) ? ' selected' : '')}
                        title={`${script.scriptName} - ${script.current ?? 'not started'}`
                            + (script.next ? ` -> ${script.next}` : '')}
                        onClick={() => pick({kind: 'script', scriptUri: script.scriptUri})}
                    >
                        <Icon name="script" size={13}/>
                        <span className="sim-row-name">{script.scriptName}</span>
                        <span className="sim-row-value">
                            {script.current ?? '-'}{script.pending.length ? ` +${script.pending.length}` : ''}
                        </span>
                    </button>
                ))}
            </DockSection>
        </div>
    );
}

function DecisionRow(props: {
    item: StorySimInterventionDto;
    selected: boolean;
    labelOf: (id: string) => string | undefined;
    actions: SimActions;
    onSelect: () => void;
}): React.JSX.Element {
    const {item, actions} = props;
    const quick = item.kind === 'lua' && item.options.length === 1
        ? {title: `Story_Event("${item.options[0]}")`, run: () => actions.luaNotify(item.options[0])}
        : item.suggested
            ? {title: describeChange(item.suggested), run: () => actions.world(item.suggested!)}
            : !item.facet && item.kind !== 'lua'
                ? {title: 'Assume the trigger met', run: () => actions.satisfy(item.nodeId)}
                : null;
    return (
        <div className={'sim-row decision' + (props.selected ? ' selected' : '')}>
            <button
                type="button"
                className="sim-row-main"
                title={`${item.eventName} (${item.eventType ?? '?'}) - press for the choices`}
                onClick={props.onSelect}
            >
                <span className="sim-row-name">{labelFor(item.nodeId, props.labelOf)}</span>
                <span className="sim-row-value">{item.eventType ?? ''}</span>
            </button>
            <IconButton
                icon="check"
                title={quick?.title ?? 'Pick an answer'}
                disabled={quick === null}
                disabledReason="Pick an answer beside the dock"
                onClick={() => quick?.run()}
            />
        </div>
    );
}

// ── Flyout ───────────────────────────────────────────────────────────────────

/** The detail of one row, beside the dock. */
export function SimFlyout(props: {
    state: StorySimStateDto;
    selection: SimSelection;
    graph: SimGraph;
    labelOf: (id: string) => string | undefined;
    actions: SimActions;
    onSelect: (selection: SimSelection | null) => void;
    onClose: () => void;
}): React.JSX.Element {
    const {selection} = props;
    const title = selection.kind === 'decision' ? 'Decision'
        : selection.kind === 'node' ? 'Event'
            : selection.kind === 'planet' ? 'Planet'
                : selection.kind === 'flag' ? 'Flag' : 'Script';
    return (
        <div className="stage-flyout on-right sizeable sim-flyout" role="dialog" aria-label={title}>
            <div className="stage-flyout-head">
                {title}
                <IconButton icon="close" title="Close" onClick={props.onClose}/>
            </div>
            <div className="stage-flyout-body">
                {selection.kind === 'decision' ? <DecisionDetail {...props} nodeId={selection.nodeId}/> : null}
                {selection.kind === 'node' ? <NodeDetail {...props} nodeId={selection.nodeId}/> : null}
                {selection.kind === 'planet' ? <PlanetDetail {...props} name={selection.name}/> : null}
                {selection.kind === 'flag' ? <FlagDetail {...props} name={selection.name}/> : null}
                {selection.kind === 'script' ? <ScriptDetail {...props} scriptUri={selection.scriptUri}/> : null}
            </div>
        </div>
    );
}

type DetailProps = {
    state: StorySimStateDto;
    graph: SimGraph;
    labelOf: (id: string) => string | undefined;
    actions: SimActions;
    onSelect: (selection: SimSelection | null) => void;
};

function factionOf(item: StorySimInterventionDto | undefined, state: StorySimStateDto): string | null {
    const named = item?.suggested?.faction;
    if (named) {
        return named;
    }
    // The player's faction is the one with a credit line; a campaign seeds exactly one.
    return state.world.credits[0]?.name ?? null;
}

function DecisionDetail(props: DetailProps & { nodeId: string }): React.JSX.Element {
    const {state, actions} = props;
    const item = state.interventions.find(i => i.nodeId === props.nodeId);
    const [chosen, setChosen] = useState('');
    useEffect(() => setChosen(''), [props.nodeId]);
    if (!item) {
        return <p className="field-note">Answered - the story moved on</p>;
    }
    const faction = factionOf(item, state);
    const candidates = pickerCandidates(item.facet, item.options, state.world, faction);
    const name = labelFor(item.nodeId, props.labelOf);
    const change = (value: string): StorySimWorldChangeDto | null => {
        if (!item.facet) {
            return null;
        }
        const base: StorySimWorldChangeDto = {...(item.suggested ?? {kind: item.facet}), kind: item.facet};
        if (candidates.kind === 'planet') {
            return {...base, planet: value, faction: base.faction ?? faction};
        }
        if (candidates.kind === 'unit') {
            return {...base, unitType: value, faction: base.faction ?? faction};
        }
        return {...base, name: value};
    };
    const custom = chosen.trim() ? change(chosen.trim()) : null;

    return (
        <>
            <div className="sim-detail-head">
                <button type="button" className="link" title="Show in the graph"
                        onClick={() => actions.centerNode(item.nodeId)}>
                    {name}
                </button>
                <span className="sim-detail-type">{item.eventType ?? ''}</span>
            </div>
            {item.kind === 'lua' ? (
                <DockSection title="Script_Event" count={item.options.length}>
                    <p className="field-note">The script owes one of these; sending it fires the event</p>
                    {item.options.map(id => (
                        <button type="button" className="btn sim-answer" key={id} title={`Story_Event("${id}")`}
                                onClick={() => actions.luaNotify(id)}>
                            <Icon name="script" size={13}/>{id}
                        </button>
                    ))}
                </DockSection>
            ) : null}
            {item.suggested ? (
                <DockSection title="From the event">
                    <button type="button" className="btn sim-answer" title="Apply this change to the world"
                            onClick={() => actions.world(item.suggested!)}>
                        <Icon name={item.kind === 'tactical' ? 'tactical' : 'world'} size={13}/>
                        {describeChange(item.suggested)}
                    </button>
                </DockSection>
            ) : null}
            {candidates.kind !== 'none' ? (
                <DockSection
                    title={candidates.kind === 'planet' ? 'Planet' : candidates.kind === 'unit' ? 'Unit type' : 'Name'}>
                    <p className="field-note">
                        {candidates.preferred.length
                            ? 'Likely first, then everything the world knows; any name can be typed'
                            : 'Nothing in the world fits yet; type a name'}
                    </p>
                    <input
                        type="text"
                        list={'sim-pick-' + candidates.kind}
                        placeholder={candidates.kind === 'planet' ? 'Planet...' : candidates.kind === 'unit' ? 'Unit type...' : 'Name...'}
                        value={chosen}
                        onChange={e => setChosen(e.target.value)}
                        onKeyDown={e => {
                            if (e.key === 'Enter' && custom) {
                                actions.world(custom);
                            }
                        }}
                    />
                    <datalist id={'sim-pick-' + candidates.kind}>
                        {[...new Set([...candidates.preferred, ...candidates.all])].map(v => <option key={v}
                                                                                                     value={v}/>)}
                    </datalist>
                    <div className="sim-chip-row">
                        {candidates.preferred.slice(0, 8).map(v => (
                            <button type="button" className="btn sim-pick" key={v}
                                    title={change(v) ? describeChange(change(v)!) : v}
                                    onClick={() => {
                                        const c = change(v);
                                        if (c) {
                                            actions.world(c);
                                        }
                                    }}>{v}</button>
                        ))}
                    </div>
                    <button type="button" className="btn sim-answer" disabled={!custom}
                            title={custom ? describeChange(custom) : 'Type or pick a name first'}
                            onClick={() => custom && actions.world(custom)}>
                        <Icon name="check" size={13}/>Apply
                    </button>
                </DockSection>
            ) : null}
            <DockSection title="Or">
                <button type="button" className="btn sim-answer" title="Fire the event without changing the world"
                        onClick={() => actions.satisfy(item.nodeId)}>
                    <Icon name="decision" size={13}/>Assume the trigger met
                </button>
            </DockSection>
        </>
    );
}

function NodeDetail(props: DetailProps & { nodeId: string }): React.JSX.Element {
    const {state, actions, graph} = props;
    const nodeState: StorySimNodeStateDto | undefined = state.nodes.find(n => n.nodeId === props.nodeId);
    const dto = graph.nodes.find(n => n.id === props.nodeId);
    const name = labelFor(props.nodeId, props.labelOf);
    const lines = useMemo(
        () => armingLines(props.nodeId, graph.nodes, graph.edges,
            id => state.nodes.find(n => n.nodeId === id)?.lifecycle),
        [props.nodeId, graph, state.nodes]);
    const hasBreakpoint = state.breakpoints.includes(props.nodeId);
    const owed = state.interventions.find(i => i.nodeId === props.nodeId);
    const toggleBreakpoint = (): void => actions.setBreakpoints(
        hasBreakpoint ? state.breakpoints.filter(id => id !== props.nodeId) : [...state.breakpoints, props.nodeId],
        state.breakOnGates);
    return (
        <>
            <div className="sim-detail-head">
                <button type="button" className="link" title="Show in the graph"
                        onClick={() => actions.centerNode(props.nodeId)}>
                    {name}
                </button>
                <span className="sim-detail-type">{dto?.eventType ?? ''}</span>
            </div>
            <DockSection title="State">
                <div className="sim-row static">
                    <span className="sim-row-name">Lifecycle</span>
                    <span
                        className={'sim-row-value lc-' + (nodeState?.lifecycle ?? 'Inactive')}>{nodeState?.lifecycle ?? 'Inactive'}</span>
                </div>
                <div className="sim-row static">
                    <span className="sim-row-name">Fired</span>
                    <span className="sim-row-value">{nodeState?.fireCount ?? 0}x</span>
                </div>
                {nodeState?.gateLabel ? (
                    <div className="sim-row static">
                        <span className="sim-row-name">Gate</span>
                        <span className="sim-row-value">{nodeState.gateLabel}</span>
                    </div>
                ) : null}
                {dto?.perpetual ? <p className="field-note">Perpetual - re-arms after every fire</p> : null}
            </DockSection>
            <DockSection title="Arms when" count={lines.length || undefined}>
                {lines.length === 0 ? <p className="field-note">A root - armed when its plot loads</p> : null}
                {lines.map((line, i) => (
                    <div className={'sim-arm-line' + (line.satisfied ? ' satisfied' : '')} key={i}>
                        {i > 0 ? <span className="sim-arm-or">or</span> : null}
                        {line.members.map((m, j) => (
                            <span key={m.nodeId} className="sim-arm-member">
                                {j > 0 ? <span className="sim-arm-and">and</span> : null}
                                <button type="button" className={'link' + (m.fired ? ' fired' : '')}
                                        title={m.fired ? 'Fired - press to show it' : 'Not fired yet - press to show it'}
                                        onClick={() => props.onSelect({kind: 'node', nodeId: m.nodeId})}>
                                    {m.label}
                                </button>
                            </span>
                        ))}
                    </div>
                ))}
            </DockSection>
            <DockSection title="Do">
                <button type="button" className={'btn sim-answer' + (hasBreakpoint ? ' active' : '')}
                        aria-pressed={hasBreakpoint}
                        title={hasBreakpoint ? 'Breakpoint set - the clock halts after this fires' : 'Halt the clock after this fires'}
                        onClick={toggleBreakpoint}>
                    <Icon name="breakpoint" size={13}/>{hasBreakpoint ? 'Clear breakpoint' : 'Break here'}
                </button>
                {owed ? (
                    <button type="button" className="btn sim-answer" title="Open the decision this event waits on"
                            onClick={() => props.onSelect({kind: 'decision', nodeId: props.nodeId})}>
                        <Icon name="decision" size={13}/>Answer its decision
                    </button>
                ) : (
                    <button type="button" className="btn sim-answer"
                            disabled={nodeState?.lifecycle !== 'Armed'}
                            title={nodeState?.lifecycle === 'Armed' ? 'Fire it now, whatever it waits on' : 'Only an armed event can be fired'}
                            onClick={() => actions.satisfy(props.nodeId)}>
                        <Icon name="fire" size={13}/>Fire now
                    </button>
                )}
            </DockSection>
        </>
    );
}

function PlanetDetail(props: DetailProps & { name: string }): React.JSX.Element {
    const {state, actions} = props;
    const planet = state.world.planets.find(p => p.name === props.name);
    const factions = useMemo(() => {
        const set = new Set<string>();
        for (const p of state.world.planets) {
            if (p.owner) {
                set.add(p.owner);
            }
        }
        for (const c of state.world.credits) {
            set.add(c.name);
        }
        return [...set].sort();
    }, [state.world]);
    const [owner, setOwner] = useState('');
    useEffect(() => setOwner(''), [props.name]);
    if (!planet) {
        return <p className="field-note">Not in the world</p>;
    }
    const here = state.world.units.filter(u => u.planet === planet.name);
    return (
        <>
            <div className="sim-detail-head">
                <span className="sim-detail-name">{planet.name}</span>
                <span className="sim-detail-type">{planet.owner ?? 'Neutral'}</span>
            </div>
            <DockSection title="Facts">
                <div className="sim-row static"><span className="sim-row-name">Revealed</span><span
                    className="sim-row-value">{planet.revealed ? 'yes' : 'no'}</span></div>
                <div className="sim-row static"><span className="sim-row-name">Corrupted</span><span
                    className="sim-row-value">{planet.corrupted ? 'yes' : 'no'}</span></div>
                <div className="sim-row static"><span className="sim-row-name">Destroyed</span><span
                    className="sim-row-value">{planet.destroyed ? 'yes' : 'no'}</span></div>
            </DockSection>
            <DockSection title="Units here" count={here.reduce((n, u) => n + u.count, 0)}>
                {here.length === 0 ? <p className="field-note">None</p> : null}
                {here.map(u => (
                    <div className="sim-row static" key={u.type + '|' + u.owner}>
                        <span className="sim-row-name">{u.type}</span>
                        <span className="sim-row-value">{u.owner} x{u.count}</span>
                    </div>
                ))}
            </DockSection>
            <DockSection title="Capture">
                <p className="field-note">Hands the planet to a faction, as a conquest would</p>
                <input type="text" list="sim-factions" placeholder="Faction..." value={owner}
                       onChange={e => setOwner(e.target.value)}
                       onKeyDown={e => {
                           if (e.key === 'Enter' && owner.trim()) {
                               actions.world({kind: 'capturePlanet', planet: planet.name, faction: owner.trim()});
                           }
                       }}/>
                <datalist id="sim-factions">{factions.map(f => <option key={f} value={f}/>)}</datalist>
                <div className="sim-chip-row">
                    {factions.filter(f => f !== planet.owner).map(f => (
                        <button type="button" className="btn sim-pick" key={f} title={`${f} captures ${planet.name}`}
                                onClick={() => actions.world({
                                    kind: 'capturePlanet',
                                    planet: planet.name,
                                    faction: f
                                })}>{f}</button>
                    ))}
                </div>
            </DockSection>
        </>
    );
}

function FlagDetail(props: DetailProps & { name: string }): React.JSX.Element {
    const {state, actions} = props;
    const existing = state.flags.find(f => f.name === props.name);
    const [name, setName] = useState(props.name);
    const [value, setValue] = useState(String(existing?.value ?? 1));
    useEffect(() => {
        setName(props.name);
        setValue(String(state.flags.find(f => f.name === props.name)?.value ?? 1));
    }, [props.name, state.flags]);
    const valid = name.trim().length > 0 && Number.isFinite(Number(value));
    const apply = (): void => {
        if (valid) {
            actions.setFlag(name.trim(), Number(value));
        }
    };
    return (
        <>
            <div className="sim-detail-head">
                <span className="sim-detail-name">{existing ? existing.name : 'New flag'}</span>
                {existing ? <span className="sim-detail-type">= {existing.value}</span> : null}
            </div>
            <DockSection title="Set">
                <p className="field-note">A STORY_FLAG event compares against this on the next tick</p>
                {existing ? null : (
                    <input type="text" placeholder="Flag name..." value={name} onChange={e => setName(e.target.value)}/>
                )}
                <div className="sim-row static">
                    <span className="sim-row-name">Value</span>
                    <input type="number" className="sim-number" value={value} onChange={e => setValue(e.target.value)}
                           onKeyDown={e => {
                               if (e.key === 'Enter') {
                                   apply();
                               }
                           }}/>
                </div>
                <div className="sim-chip-row">
                    {[0, 1, 2, 3].map(v => (
                        <button type="button" className="btn sim-pick" key={v} title={`Set to ${v}`}
                                disabled={!name.trim()}
                                onClick={() => actions.setFlag(name.trim(), v)}>{v}</button>
                    ))}
                </div>
                <button type="button" className="btn sim-answer" disabled={!valid}
                        title={valid ? 'Write the flag' : 'Name and a number first'}
                        onClick={apply}>
                    <Icon name="check" size={13}/>Set flag
                </button>
            </DockSection>
        </>
    );
}

function ScriptDetail(props: DetailProps & { scriptUri: string }): React.JSX.Element {
    const {state, actions} = props;
    const script = state.luaStates.find(s => s.scriptUri === props.scriptUri);
    if (!script) {
        return <p className="field-note">Not in this plot</p>;
    }
    const stateNodeId = (name: string): string => `${script.scriptUri}#lua#${name.toLowerCase()}`;
    return (
        <>
            <div className="sim-detail-head">
                <span className="sim-detail-name">{script.scriptName}</span>
                <span className="sim-detail-type">PGStateMachine</span>
            </div>
            <DockSection title="Where it is">
                <div className="sim-row static">
                    <span className="sim-row-name">Current</span>
                    {script.current ? (
                        <button type="button" className="link" title="Show the state on the graph"
                                onClick={() => actions.centerNode(stateNodeId(script.current!))}>{script.current}</button>
                    ) : <span className="sim-row-value">not started</span>}
                </div>
                <div className="sim-row static">
                    <span className="sim-row-name">Next</span>
                    <span className="sim-row-value">{script.next ?? '-'}</span>
                </div>
            </DockSection>
            <DockSection title="Owes" count={script.pending.length}>
                {script.pending.length === 0 ? <p className="field-note">No Story_Event pending</p> : null}
                {script.pending.map(p => (
                    <div className="sim-row static" key={p.id + '@' + p.dueClock}>
                        <span className="sim-row-name">{p.id}</span>
                        <span className="sim-row-value">{p.state} at {p.dueClock.toFixed(0)} s</span>
                    </div>
                ))}
            </DockSection>
            {state.luaNotifications.length ? (
                <DockSection title="Send" count={state.luaNotifications.length}>
                    <p className="field-note">Any Story_Event id the plot listens for, as if the script called it
                        now</p>
                    <div className="sim-chip-row">
                        {state.luaNotifications.map(id => (
                            <button type="button" className="btn sim-pick" key={id} title={`Story_Event("${id}")`}
                                    onClick={() => actions.luaNotify(id)}>{id}</button>
                        ))}
                    </div>
                </DockSection>
            ) : null}
        </>
    );
}

// ── Foot ─────────────────────────────────────────────────────────────────────

const PACE_STOPS: SimPace['mode'][] = ['step', 'pulse', 'custom'];

/**
 * The tick transport, in the dock foot. The preview's animation player row: |<< back to the
 * start, |< one tick back, play or pause, >| one tick, >>| run to the next decision - then the
 * pace under it, three stops on a slider because it is an ordered axis with few values.
 */
export function SimTransport(props: {
    state: StorySimStateDto;
    pace: SimPace;
    playing: boolean;
    lenses: SimLenses;
    traceOpen: boolean;
    setPace: (pace: SimPace) => void;
    setLenses: (lenses: SimLenses) => void;
    setTraceOpen: (open: boolean) => void;
    actions: SimActions;
}): React.JSX.Element {
    const {state, pace, playing, lenses, actions} = props;
    const owed = state.interventions.length > 0;
    const halted = !!state.haltedAt;
    const blocked = owed ? 'Answer the decisions first' : null;
    const stop = PACE_STOPS.indexOf(pace.mode);
    return (
        <div className="player sim-transport">
            <div className="player-row">
                <IconButton
                    icon="firstFrame"
                    title="Restart from tick 0"
                    disabled={state.tick === 0}
                    disabledReason="Already at tick 0"
                    onClick={actions.restart}
                />
                <IconButton
                    icon="previousClip"
                    title="One tick back"
                    disabled={state.tick === 0}
                    disabledReason="Already at tick 0"
                    onClick={actions.back}
                />
                <IconButton
                    icon={playing ? 'pause' : 'play'}
                    title={pace.mode === 'step' ? 'One tick' : playing ? 'Pause' : halted ? 'Resume from the breakpoint' : 'Play'}
                    disabled={blocked !== null}
                    disabledReason={blocked ?? ''}
                    onClick={actions.playPause}
                />
                <IconButton
                    icon="nextClip"
                    title="One tick"
                    disabled={blocked !== null}
                    disabledReason={blocked ?? ''}
                    onClick={actions.tick}
                />
                <IconButton
                    icon="lastFrame"
                    title="Run to the next decision or breakpoint"
                    disabled={blocked !== null}
                    disabledReason={blocked ?? ''}
                    onClick={actions.runToDecision}
                />
                <span className="player-time" title={`Tick ${state.tick}, ${state.clock.toFixed(0)} s of story time`}>
                    t{state.tick}{' - '}{state.clock.toFixed(0)}s
                </span>
            </div>
            <div className="player-row sim-pace">
                <span
                    className="sim-pace-label">{pace.mode === 'step' ? 'Step' : pace.mode === 'pulse' ? 'Pulse 1 s' : `${pace.ticksPerSecond}/s`}</span>
                <input
                    className="sim-pace-slider"
                    type="range" min={0} max={2} step={1} value={stop < 0 ? 1 : stop}
                    title="Pace - step, 1 s pulse, or a custom rate"
                    aria-label="Pace"
                    onChange={e => props.setPace({...pace, mode: PACE_STOPS[Number(e.target.value)]})}
                />
                <input
                    className="sim-rate-slider"
                    type="range" min={1} max={30} step={1} value={pace.ticksPerSecond}
                    disabled={pace.mode !== 'custom'}
                    title={pace.mode === 'custom' ? `${pace.ticksPerSecond} ticks per second` : 'Custom pace only'}
                    aria-label="Ticks per second"
                    onChange={e => props.setPace({...pace, ticksPerSecond: Number(e.target.value)})}
                />
            </div>
            <div className="player-row sim-lenses">
                <IconButton
                    icon="trace"
                    className={props.traceOpen ? 'active' : undefined}
                    pressed={props.traceOpen}
                    title={props.traceOpen ? 'Trace panel open' : 'Open the trace panel'}
                    onClick={() => props.setTraceOpen(!props.traceOpen)}
                />
                <IconButton
                    icon="breakpoint"
                    className={state.breakOnGates ? 'active' : undefined}
                    pressed={state.breakOnGates}
                    title={state.breakOnGates ? 'Halting at every clock and flag gate' : 'Halt at every clock and flag gate'}
                    onClick={() => actions.setBreakpoints(state.breakpoints, !state.breakOnGates)}
                />
                <IconButton
                    icon="flow"
                    className={lenses.hideFlow ? undefined : 'active'}
                    pressed={!lenses.hideFlow}
                    title={lenses.hideFlow ? 'Flow tints off' : 'Flow tints on the edges the story ran along'}
                    onClick={() => props.setLenses({...lenses, hideFlow: !lenses.hideFlow})}
                />
                <IconButton
                    icon="fire"
                    className={lenses.activeOnly ? 'active' : undefined}
                    pressed={lenses.activeOnly}
                    title={lenses.activeOnly ? 'Showing the path the story took' : 'Dim everything the story has not reached'}
                    onClick={() => props.setLenses({...lenses, activeOnly: !lenses.activeOnly})}
                />
                <IconButton
                    icon="script"
                    className={lenses.hideLua ? undefined : 'active'}
                    pressed={!lenses.hideLua}
                    title={lenses.hideLua ? 'Script states hidden' : 'Script states on the canvas'}
                    onClick={() => props.setLenses({...lenses, hideLua: !lenses.hideLua})}
                />
            </div>
        </div>
    );
}

// ── Trace ────────────────────────────────────────────────────────────────────

/** The trace, in the bottom panel: one row per step, filterable, copyable, each row a jump. */
export function TracePanel(props: {
    state: StorySimStateDto;
    steps: readonly StorySimStepDto[];
    labelOf: (id: string) => string | undefined;
    actions: SimActions;
    onClose: () => void;
}): React.JSX.Element {
    const [query, setQuery] = useState('');
    const rows = useMemo(() => traceRows(props.steps, props.labelOf), [props.steps, props.labelOf]);
    const shown = useMemo(() => filterTrace(rows, query), [rows, query]);
    const tail = shown.slice(-400);
    const copyText = (): string => shown
        .map(r => `t${r.tick}\t${r.node}\t${r.cause}${r.to ? `\t${r.from ?? ''} -> ${r.to}` : ''}${r.via ? `\tvia ${r.via}` : ''}${r.detail ? `\t${r.detail}` : ''}`)
        .join('\n');
    return (
        <ProblemsPanel
            className="sim-trace"
            memoKey="storyGraph.simLog"
            defaultHeight={160}
            title={`Trace (${query ? `${shown.length} of ${rows.length}` : rows.length})`}
            onClose={props.onClose}
        >
            <div className="sim-trace-tools">
                <input
                    type="text" placeholder="Filter the trace, or t12 for one tick" value={query}
                    onChange={e => setQuery(e.target.value)}
                />
                <IconButton icon="copy" title="Copy the shown rows" disabled={shown.length === 0}
                            disabledReason="Nothing to copy" onClick={() => props.actions.copy(copyText())}/>
            </div>
            {tail.length < shown.length ? (
                <div className="sim-trace-row muted">{shown.length - tail.length} earlier rows not shown - narrow the
                    filter</div>
            ) : null}
            {tail.map(row => (
                <div
                    className={'sim-trace-row cause-' + row.cause + (row.nodeId ? ' clickable' : '')}
                    key={row.seq}
                    title={row.nodeId ? 'Show this node in the graph' : undefined}
                    onClick={row.nodeId ? () => props.actions.centerNode(row.nodeId) : undefined}
                >
                    <span className="sim-trace-tick">t{row.tick}</span>
                    <span className="sim-trace-node" title={row.nodeId}>{row.node || shortId(row.nodeId)}</span>
                    <span className="sim-trace-cause">{row.cause}</span>
                    <span className="sim-trace-change">{row.to ? `${row.from ?? ''} -> ${row.to}` : ''}</span>
                    <span className="sim-trace-via"
                          title={row.viaId ?? undefined}>{row.via ? `via ${row.via}` : ''}</span>
                    <span className="sim-trace-detail problem-msg"
                          title={row.detail ?? undefined}>{row.detail ?? ''}</span>
                </div>
            ))}
        </ProblemsPanel>
    );
}

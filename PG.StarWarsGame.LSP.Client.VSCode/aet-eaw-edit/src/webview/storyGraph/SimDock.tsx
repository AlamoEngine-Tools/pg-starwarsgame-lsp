// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The simulation's dock, at the dock's three levels (issue #90).
//
// Header: the tick and what the story is waiting on - state and warnings. Content: the inventories -
// decisions the author owes, the world's facts, the flags, the scripts. Foot: the transport, which
// is the preview's animation player with ticks for frames. A row's detail opens as ONE dialog -
// never by unfolding inside the list, never as a flyout, never as a dialog on top of another.
//
// Everything here reads the flat state document and sends a request; storyGraph.tsx owns the state
// and the canvas, and simModel.ts owns the arithmetic.

import {useEffect, useMemo, useRef, useState} from 'react';

import type {
    StoryGraphEdgeDto,
    StoryGraphNodeDto,
    StorySimBattleDto,
    StorySimFlagDto,
    StorySimInterventionDto,
    StorySimNodeStateDto,
    StorySimStateDto,
    StorySimStepDto,
    StorySimWorldChangeDto,
} from '../../protocol/story';
import {Button, IconButton} from '../shared/Button';
import {Combobox} from '../shared/Combobox';
import {IntegerField} from '../shared/IntegerField';
import {parseInteger} from '../shared/integerValue';
import {Modal} from '../shared/Modal';
import {DockSection} from '../shared/DockSection';
import {Field} from '../shared/Field';
import {Icon, type IconName} from '../shared/Icon';
import {ProblemsPanel} from '../shared/ProblemsPanel';

import {
    armingLines,
    type DecisionKind,
    describeChange,
    filterTrace,
    groupDecisions,
    labelFor,
    pickerCandidates,
    retryableBattle,
    type SimPace,
    SPEED_STOPS,
    speedLabel,
    speedStopOf,
    traceRows,
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
    /** The author's call that an armed listener will not fire in this run, or may after all. */
    ruleOut: (nodeId: string, ruledOut: boolean) => void;
    world: (change: StorySimWorldChangeDto) => void;
    luaNotify: (id: string) => void;
    setFlag: (name: string, value: number) => void;
    setBreakpoints: (nodeIds: string[], onGates: boolean) => void;
    centerNode: (nodeId: string) => void;
    copy: (text: string) => void;
    /** The battle's own panel; `simulate` opens it straight into its session. */
    openBattle: (battleKey: string, label: string, simulate: boolean) => void;
    /**
     * The battle's end: its own outcome listeners fire, its flag writes and the author's picks
     * merge into the galaxy, and the outcome fires there.
     */
    resolveBattle: (battleKey: string, won: boolean, picks?: readonly StorySimFlagDto[]) => void;
    /** The game's retry after a lost battle: the galaxy back to just before the outcome. */
    retryBattle: (battleKey: string) => void;
}

/** What the detail dialog is showing. */
export type SimSelection =
    | { kind: 'decision'; nodeId: string }
    | { kind: 'node'; nodeId: string }
    | { kind: 'planet'; name: string }
    | { kind: 'flag'; name: string }
    | { kind: 'script'; scriptUri: string }
    | { kind: 'battle'; key: string };

/** Why a session takes no command right now, or null: the reason every transport control shows. */
export function frozenReason(state: StorySimStateDto): string | null {
    if (state.pausedFor) {
        return `Galaxy paused - battle ${state.pausedFor}`;
    }
    if (state.outcome) {
        return `Battle ${state.outcome} - re-enter Simulation to replay`;
    }
    return null;
}

/** A battle's standing as the galactic session reports it, in the dock's words. */
export function battleStatusLabel(battle: StorySimBattleDto): string {
    switch (battle.status) {
        case 'pending':
            return 'Pending - fight or auto-resolve';
        case 'fight':
            return 'Starting';
        case 'autoResolve':
            return 'Auto-resolve - decide the outcome';
        case 'running':
            return `Running - t${battle.tick}`;
        case 'won':
            return 'Won';
        case 'lost':
            return 'Lost';
        default:
            return 'Not started';
    }
}

/** The three canvas lenses: each shows one slice of the running story. */
export interface SimLenses {
    /** Dim every edge and node the story has not run through. */
    activeOnly: boolean;
    /** Take the flow tints off the edges. */
    hideFlow: boolean;
    /** Take the script states off the canvas. */
    hideLua: boolean;
}

/** The graph the event dialog reads arming lines off. */
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

/** Nothing more happens until the author answers something: the clock has no work left of its own. */
export function waitingOnAuthor(state: StorySimStateDto): boolean {
    return state.interventions.length > 0 && (state.clockPending ?? 1) === 0;
}

/**
 * The player's status readout, in the slot the preview's player gives its scrubber: what the run
 * is doing on the left, the tick and the clock at the right. Running (a live dot), paused, halted at
 * a breakpoint, waiting for input because the clock alone has nothing left to change, the galaxy
 * paused for a battle, or a battle's outcome. A real campaign has hundreds of armed world listeners
 * from tick 0, so the mere count of decisions is an inventory figure, not a warning. Pressing it
 * acts on the state it shows: running pauses, paused plays, halted and waiting open the event
 * concerned. The panel opens the decision the story hangs on by itself when a wait begins
 * (storyGraph.tsx, applySimOverlay); the press is the way back to it.
 */
export function SimStatus(props: {
    state: StorySimStateDto;
    playing: boolean;
    labelOf: (id: string) => string | undefined;
    onSelect: (selection: SimSelection) => void;
    onPlayPause: () => void;
    /** The game's retry after a lost battle: the galaxy back to just before the outcome. */
    onRetry?: (battleKey: string) => void;
}): React.JSX.Element {
    const {state} = props;
    const time = `t${state.tick} - ${state.clock.toFixed(0)}s`;
    const retry = retryableBattle(state);
    const owed = waitingOnAuthor(state) ? state.interventions.length : 0;
    const status = ((): {
        kind: string; glyph: React.ReactNode; text: string; title: string; onClick?: () => void;
    } => {
        if (state.pausedFor) {
            // The game freezes the galaxy during a tactical battle; the readout names the battle and
            // opens it, since that is where the story goes on.
            const battle = state.battles?.find(b => b.label === state.pausedFor);
            return {
                kind: 'battle', glyph: <Icon name="tactical" size={14}/>, text: state.pausedFor,
                title: `Galaxy paused - battle ${state.pausedFor}`,
                onClick: () => battle && props.onSelect({kind: 'battle', key: battle.key}),
            };
        }
        if (state.outcome) {
            const text = state.outcome === 'won' ? 'Won' : 'Lost';
            return {
                kind: 'resolved ' + state.outcome, glyph: <Icon name="tactical" size={14}/>, text,
                title: `Battle ${state.outcome}`,
            };
        }
        if (retry && props.onRetry) {
            // Where the game shows its retry dialog: the story cannot go on from a lost battle.
            const label = state.battles?.find(b => b.key === retry)?.label ?? retry;
            return {
                kind: 'lost', glyph: <Icon name="restart" size={14}/>, text: `Battle lost - ${label}`,
                title: 'Retry - back to just before the outcome',
                onClick: () => props.onRetry!(retry),
            };
        }
        if (state.haltedAt) {
            const name = labelFor(state.haltedAt, props.labelOf);
            return {
                kind: 'halted', glyph: <Icon name="breakpoint" size={14}/>, text: name,
                title: `Breakpoint - ${name}`,
                onClick: () => props.onSelect({kind: 'node', nodeId: state.haltedAt!}),
            };
        }
        if (owed > 0) {
            const first = state.interventions[0];
            return {
                kind: 'owed', glyph: <Icon name="decision" size={14}/>, text: 'Waiting for input',
                title: `Waiting for input - ${owed} decision${owed === 1 ? '' : 's'}`,
                onClick: () => props.onSelect({kind: 'decision', nodeId: first.nodeId}),
            };
        }
        if (props.playing) {
            return {
                kind: 'running', glyph: <span className="sim-status-dot" aria-hidden="true"/>, text: 'Running',
                title: 'Running - press to pause', onClick: props.onPlayPause,
            };
        }
        return {
            kind: 'paused', glyph: <Icon name="pause" size={14}/>, text: 'Paused',
            title: 'Paused - press to play', onClick: props.onPlayPause,
        };
    })();
    const body = (
        <>
            {status.glyph}
            <span className="sim-status-text">{status.text}</span>
            <span className="sim-status-time">{time}</span>
        </>
    );
    return status.onClick
        ? <button type="button" className={'sim-status ' + status.kind} title={status.title}
                  onClick={status.onClick}>{body}</button>
        : <span className={'sim-status ' + status.kind} title={status.title}>{body}</span>;
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
    // A row opens its detail beside the dock and, for anything that IS an event, shows that event
    // on the canvas too: the decision is about the event, so the reader wants to see it.
    const pick = (s: SimSelection): void => {
        props.onSelect(isSelected(s) ? null : s);
        if (s.kind === 'decision' || s.kind === 'node') {
            actions.centerNode(s.nodeId);
        }
    };

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
                        {state.haltedAt ? 'Halted at breakpoint' : 'No decision pending'}
                    </p>
                ) : null}
                {groups.length > 0 ? (
                    <p className="field-note">
                        {waitingOnAuthor(state) ? 'Clock idle - answer one to continue' : 'Clock running - answer any to steer'}
                    </p>
                ) : null}
                {groups.map(group => (
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
                {state.ruledOut?.length ? (
                    // Ruled out by the author for this run: armed as in the game, no decision.
                    // Listed, not hidden - the call is reversible from here.
                    <div className="sim-group">
                        <div className="sim-group-title">
                            <Icon name="remove" size={13}/>
                            Ruled out
                        </div>
                        {state.ruledOut.map(nodeId => (
                            <div className="sim-row decision" key={nodeId}>
                                <button
                                    type="button"
                                    className="sim-row-main"
                                    title="Never fires in this run - open the event"
                                    onClick={() => pick({kind: 'node', nodeId})}
                                >
                                    <span className="sim-row-name">{labelFor(nodeId, props.labelOf)}</span>
                                    <span className="sim-row-value">Ruled out</span>
                                </button>
                                <IconButton
                                    icon="reset"
                                    title="Reconsider - a decision again"
                                    onClick={() => actions.ruleOut(nodeId, false)}
                                />
                            </div>
                        ))}
                    </div>
                ) : null}
            </DockSection>

            {state.scope ? (
                <DockSection title="Battle" id="sim-battle" collapsed={folded.has('sim-battle')} onToggle={toggle}>
                    <BattleControls state={state} battleKey={state.scope} actions={actions}/>
                </DockSection>
            ) : null}
            {!state.scope && state.battles?.length ? (
                <DockSection
                    title="Battles"
                    count={state.battles.length}
                    id="sim-battles" collapsed={folded.has('sim-battles')} onToggle={toggle}
                >
                    {state.battles.map(battle => (
                        <button
                            type="button"
                            key={battle.key}
                            className={'sim-row' + (isSelected({kind: 'battle', key: battle.key}) ? ' selected' : '')
                                + (battle.status === 'running' ? ' running' : '')}
                            title={`${battle.label} - ${battleStatusLabel(battle)}`}
                            onClick={() => pick({kind: 'battle', key: battle.key})}
                        >
                            <Icon name="tactical" size={13}/>
                            <span className="sim-row-name">{battle.label}</span>
                            <span className="sim-row-value">{battleStatusLabel(battle)}</span>
                        </button>
                    ))}
                </DockSection>
            ) : null}

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
                    title="Set a flag"
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
                {state.luaStates.length === 0 ? <p className="field-note">No script attached</p> : null}
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
                title={`${item.eventName} - ${item.eventType ?? 'unknown type'}`}
                onClick={props.onSelect}
            >
                <span className="sim-row-name">{labelFor(item.nodeId, props.labelOf)}</span>
                <span className="sim-row-value">{item.eventType ?? ''}</span>
            </button>
            <IconButton
                icon="check"
                title={quick?.title ?? 'No one-press answer'}
                disabled={quick === null}
                disabledReason="No one-press answer - open the row"
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
    // Every detail is ONE dialog. One with a value applies it on OK; the rest show, answer through
    // their button row, and close. Never a flyout, and never a dialog on top of another window.
    if (selection.kind === 'flag') {
        return <FlagDialog state={props.state} name={selection.name} actions={props.actions}
                           onClose={props.onClose}/>;
    }
    if (selection.kind === 'planet') {
        return <PlanetDialog state={props.state} name={selection.name} actions={props.actions}
                             onClose={props.onClose}/>;
    }
    const decision = selection.kind === 'decision'
        ? props.state.interventions.find(i => i.nodeId === selection.nodeId)
        : undefined;
    if (decision && decisionPicker(decision, props.state)) {
        return <DecisionDialog state={props.state} item={decision} actions={props.actions}
                               onClose={props.onClose}/>;
    }
    const detail = {...props, onClose: props.onClose};
    if (selection.kind === 'decision') {
        return <AnswerDialog {...detail} nodeId={selection.nodeId}/>;
    }
    if (selection.kind === 'node') {
        return <EventDialog {...detail} nodeId={selection.nodeId}/>;
    }
    if (selection.kind === 'script') {
        return <ScriptDialog {...detail} scriptUri={selection.scriptUri}/>;
    }
    return <BattleDialog {...detail} battleKey={selection.key}/>;
}

type DetailProps = {
    state: StorySimStateDto;
    graph: SimGraph;
    labelOf: (id: string) => string | undefined;
    actions: SimActions;
    onSelect: (selection: SimSelection | null) => void;
    onClose: () => void;
};

/** "Show in graph": the dialog would cover what it points at, so it closes first. */
function ShowInGraph(props: { nodeId: string; actions: SimActions; onClose: () => void }): React.JSX.Element {
    return (
        <Button title="Centre the graph on it" onClick={() => {
            props.onClose();
            props.actions.centerNode(props.nodeId);
        }}>
            <Icon name="search" size={13}/>Show
        </Button>
    );
}

function factionOf(item: StorySimInterventionDto | undefined, state: StorySimStateDto): string | null {
    const named = item?.suggested?.faction;
    if (named) {
        return named;
    }
    // The player's faction is the one with a credit line; a campaign seeds exactly one.
    return state.world.credits[0]?.name ?? null;
}

/**
 * A battle's controls: enter it (its own panel and session), show it while it runs, or decide
 * its outcome here without entering - the tactical decision the game leaves to play. Shared by
 * the battle row, the tactical decision that waits on it, and the paused chip.
 */
function BattleControls(props: {
    state: StorySimStateDto; battleKey: string; actions: SimActions;
    /** The dialog title already names the battle: the status row then names only its state. */
    titled?: boolean;
}): React.JSX.Element {
    const {state, battleKey, actions} = props;
    const inside = state.scope === battleKey;
    const battle = state.battles?.find(b => b.key === battleKey);
    const label = battle?.label ?? battleKey;
    const status = inside
        ? (state.outcome === 'won' ? 'Won' : state.outcome === 'lost' ? 'Lost' : `Running - t${state.tick}`)
        : battle ? battleStatusLabel(battle) : 'Unknown';
    const resolved = inside ? !!state.outcome : battle?.status === 'won' || battle?.status === 'lost';
    const running = inside ? !state.outcome : battle?.status === 'running';
    // The game's pending-battle choice, as measured: the left button fights, the right one
    // auto-resolves. The author's press is a real click, so the click event is raised with it.
    const pending = !inside && battle?.status === 'pending';
    const starting = !inside && battle?.status === 'fight';
    // Deciding on the portal skips the battle's own run - before it is entered, or instead of
    // entering it once the fight is chosen - so the flags it could have written are offered here;
    // a battle that is running or being played writes them itself.
    const writes = !inside && !running && !pending ? battle?.writes ?? [] : [];
    const [picked, setPicked] = useState<Set<string>>(() => new Set());
    useEffect(() => setPicked(new Set()), [battleKey]);
    const picks = writes.filter(w => picked.has(w.name));
    const togglePick = (name: string): void => setPicked(prev => {
        const next = new Set(prev);
        if (next.has(name)) {
            next.delete(name);
        } else {
            next.add(name);
        }
        return next;
    });
    return (
        <>
            <div className="sim-row static">
                <Icon name="tactical" size={13}/>
                <span className="sim-row-name">{props.titled ? 'Status' : label}</span>
                <span className="sim-row-value">{status}</span>
            </div>
            {pending ? (
                <div className="sim-chip-row">
                    <button type="button" className="btn sim-answer"
                            title="Fight - the battle runs in its own panel"
                            onClick={() => actions.world({kind: 'clickGui', name: 'choice_button_left'})}>
                        <Icon name="tactical" size={13}/>Fight
                    </button>
                    <button type="button" className="btn sim-answer"
                            title="Auto-resolve - decide the outcome here"
                            onClick={() => actions.world({kind: 'clickGui', name: 'choice_button_right'})}>
                        <Icon name="decision" size={13}/>Auto-resolve
                    </button>
                </div>
            ) : null}
            {!inside && !pending ? (
                <button type="button" className="btn sim-answer" disabled={resolved}
                        title={resolved ? `Battle ${status.toLowerCase()} - restart the galaxy to play it again`
                            : running ? 'Show the battle panel'
                                : starting ? 'Play the battle in its own panel'
                                    : 'Open the battle in its own panel'}
                        onClick={() => actions.openBattle(battleKey, label, !running)}>
                    <Icon name="tactical"
                          size={13}/>{running ? 'Show the battle' : starting ? 'Play the battle' : 'Enter the battle'}
                </button>
            ) : null}
            {writes.length > 0 && !resolved ? (
                <div className="sim-picks">
                    <p className="field-note">Flags the battle can write - pick what it did</p>
                    {writes.map(w => (
                        <label className="sim-row static sim-pick-row" key={w.name} title={`${w.name} = ${w.value}`}>
                            <input type="checkbox" checked={picked.has(w.name)} onChange={() => togglePick(w.name)}/>
                            <Icon name="flag" size={13}/>
                            <span className="sim-row-name">{w.name}</span>
                            <span className="sim-row-value">{w.value}</span>
                        </label>
                    ))}
                </div>
            ) : null}
            {!pending ? (
                <div className="sim-chip-row">
                    <button type="button" className="btn sim-answer" disabled={resolved}
                            title={resolved ? `Battle ${status.toLowerCase()}` : `${label} won - the galaxy takes the victory`}
                            onClick={() => actions.resolveBattle(battleKey, true, picks)}>
                        <Icon name="check" size={13}/>Won
                    </button>
                    <button type="button" className="btn sim-answer" disabled={resolved}
                            title={resolved ? `Battle ${status.toLowerCase()}` : `${label} lost - the galaxy takes the defeat`}
                            onClick={() => actions.resolveBattle(battleKey, false, picks)}>
                        <Icon name="close" size={13}/>Lost
                    </button>
                </div>
            ) : null}
            {!inside && battle?.status === 'lost' ? (
                // The game's answer to a lost battle: retry, which reloads the pre-battle autosave.
                <button type="button" className="btn sim-answer" title="Retry - back to just before the outcome"
                        onClick={() => actions.retryBattle(battleKey)}>
                    <Icon name="restart" size={13}/>Retry
                </button>
            ) : null}
        </>
    );
}

function BattleDialog(props: DetailProps & { battleKey: string }): React.JSX.Element {
    const battle = props.state.battles?.find(b => b.key === props.battleKey);
    return (
        <Modal title={battle?.label ?? props.battleKey} onCancel={props.onClose}>
            <BattleControls state={props.state} battleKey={props.battleKey} actions={props.actions} titled/>
        </Modal>
    );
}

/**
 * A decision with no value to pick: a script event, a battle's outcome, a bare trigger. The answers
 * are the button row - the event's own change, Fire, Rule out - and each one closes the dialog.
 */
function AnswerDialog(props: DetailProps & { nodeId: string }): React.JSX.Element {
    const {state, actions} = props;
    const item = state.interventions.find(i => i.nodeId === props.nodeId);
    // A battle's decision sits on its portal, whose id is the manifest file: name the battle instead.
    const name = item?.kind === 'battle' ? item.eventName : labelFor(props.nodeId, props.labelOf);
    const answer = (run: () => void): void => {
        run();
        props.onClose();
    };
    if (!item) {
        return <Modal title={name} onCancel={props.onClose}><p className="modal-note">Answered</p></Modal>;
    }
    return (
        <Modal
            title={name} onCancel={props.onClose}
            actions={<>
                <ShowInGraph nodeId={item.nodeId} actions={actions} onClose={props.onClose}/>
                {!item.battleKey && item.suggested ? (
                    <Button title="The change the event names"
                            onClick={() => answer(() => actions.world(item.suggested!))}>
                        <Icon name={item.kind === 'tactical' ? 'tactical' : 'world'} size={13}/>
                        {describeChange(item.suggested)}
                    </Button>
                ) : null}
                {item.kind !== 'battle' ? (
                    // A battle is not a trigger: it ends through its outcome or nothing.
                    <>
                        <Button title="Assume the trigger met"
                                onClick={() => answer(() => actions.satisfy(item.nodeId))}>
                            <Icon name="decision" size={13}/>Fire
                        </Button>
                        <Button title="Never fires in this run - stays armed"
                                onClick={() => answer(() => actions.ruleOut(item.nodeId, true))}>
                            <Icon name="remove" size={13}/>Rule out
                        </Button>
                    </>
                ) : null}
            </>}
        >
            <p className="modal-note">{item.eventType ?? ''}</p>
            {item.kind === 'lua' ? item.options.map(id => (
                <button type="button" className="btn sim-answer" key={id} title={`Story_Event("${id}")`}
                        onClick={() => answer(() => actions.luaNotify(id))}>
                    <Icon name="script" size={13}/>{id}
                </button>
            )) : null}
            {item.battleKey ? (
                // The outcome this listener waits on is a battle's: deciding it here resolves the
                // battle, so the portal, the dock and the galaxy agree on what happened.
                <BattleControls state={state} battleKey={item.battleKey} actions={actions}/>
            ) : null}
        </Modal>
    );
}

function EventDialog(props: DetailProps & { nodeId: string }): React.JSX.Element {
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
    const armed = nodeState?.lifecycle === 'Armed';
    return (
        <Modal
            title={name} onCancel={props.onClose}
            actions={<>
                <ShowInGraph nodeId={props.nodeId} actions={actions} onClose={props.onClose}/>
                <Button title={hasBreakpoint ? 'Clear the breakpoint' : 'Break after this fires'}
                        className={hasBreakpoint ? 'active' : undefined} onClick={toggleBreakpoint}>
                    <Icon name="breakpoint" size={13}/>{hasBreakpoint ? 'Clear break' : 'Break here'}
                </Button>
                {owed ? (
                    <Button title="Its decision"
                            onClick={() => props.onSelect({kind: 'decision', nodeId: props.nodeId})}>
                        <Icon name="decision" size={13}/>Answer
                    </Button>
                ) : (
                    <Button title="Fire now" disabled={!armed} disabledReason="Not armed"
                            onClick={() => actions.satisfy(props.nodeId)}>
                        <Icon name="fire" size={13}/>Fire
                    </Button>
                )}
            </>}
        >
            <p className="modal-note">{dto?.eventType ?? ''}</p>
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
                {lines.length === 0 ? <p className="field-note">Root - armed at plot load</p> : null}
                {lines.map((line, i) => (
                    <div className={'sim-arm-line' + (line.satisfied ? ' satisfied' : '')} key={i}>
                        {i > 0 ? <span className="sim-arm-or">or</span> : null}
                        {line.members.map((m, j) => (
                            <span key={m.nodeId} className="sim-arm-member">
                                {j > 0 ? <span className="sim-arm-and">and</span> : null}
                                <button type="button" className={'link' + (m.fired ? ' fired' : '')}
                                        title={m.fired ? 'Fired' : 'Not fired'}
                                        onClick={() => props.onSelect({kind: 'node', nodeId: m.nodeId})}>
                                    {m.label}
                                </button>
                            </span>
                        ))}
                    </div>
                ))}
            </DockSection>
        </Modal>
    );
}

/**
 * A planet as one dialog: its owner is the value to change - a new owner applies on OK and fires
 * the capture listeners - and the facts and units beside it are read-only. The draft is taken
 * once, when the dialog opens, so a tick landing mid-typing leaves it alone.
 */
function PlanetDialog(props: {
    state: StorySimStateDto; name: string; actions: SimActions; onClose: () => void;
}): React.JSX.Element {
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
    const [owner, setOwner] = useState(() => planet?.owner ?? '');
    const draft = owner.trim();
    const changed = !!planet && draft.length > 0 && draft.toLowerCase() !== (planet.owner ?? '').toLowerCase();
    const here = planet ? state.world.units.filter(u => u.planet === planet.name) : [];
    return (
        <Modal
            form title={props.name} confirmLabel="OK"
            canConfirm={changed} disabledReason={draft ? 'Owner unchanged' : 'Owner required'}
            onConfirm={() => {
                if (changed) {
                    actions.world({kind: 'capturePlanet', planet: planet!.name, faction: draft});
                    props.onClose();
                }
            }}
            onCancel={props.onClose}
        >
            {planet ? (
                <>
                    <Combobox
                        value={owner} onChange={setOwner} placeholder="Owner" ariaLabel="Owner" icon="search"
                        options={factions.map(f => ({value: f}))} autoFocus
                    />
                    <div className="sim-row static"><span className="sim-row-name">Revealed</span><span
                        className="sim-row-value">{planet.revealed ? 'yes' : 'no'}</span></div>
                    <div className="sim-row static"><span className="sim-row-name">Corrupted</span><span
                        className="sim-row-value">{planet.corrupted ? 'yes' : 'no'}</span></div>
                    <div className="sim-row static"><span className="sim-row-name">Destroyed</span><span
                        className="sim-row-value">{planet.destroyed ? 'yes' : 'no'}</span></div>
                    {here.map(u => (
                        <div className="sim-row static" key={u.type + '|' + u.owner}>
                            <span className="sim-row-name">{u.type}</span>
                            <span className="sim-row-value">{u.owner} x{u.count}</span>
                        </div>
                    ))}
                </>
            ) : <p className="modal-note">Not in the world</p>}
        </Modal>
    );
}

/**
 * Setting a flag: a form, so it is a modal with OK and Cancel rather than a flyout - the row already
 * shows the flag and its value, and nothing changes until OK. The draft is taken once, when the
 * dialog opens: a running simulation re-renders on every tick and must not overwrite what is being
 * typed.
 */
function FlagDialog(props: {
    state: StorySimStateDto; name: string; actions: SimActions; onClose: () => void;
}): React.JSX.Element {
    const {state, actions} = props;
    const existing = state.flags.find(f => f.name === props.name);
    const [name, setName] = useState(props.name);
    const [value, setValue] = useState(() => String(existing?.value ?? 1));
    const parsed = parseInteger(value);
    const valid = name.trim().length > 0 && parsed !== null;
    return (
        <Modal
            form title={existing ? `Set ${existing.name}` : 'Set flag'} confirmLabel="OK"
            canConfirm={valid}
            disabledReason={name.trim() ? 'Whole number required' : 'Flag name required'}
            onConfirm={() => {
                if (valid) {
                    actions.setFlag(name.trim(), parsed!);
                    props.onClose();
                }
            }}
            onCancel={props.onClose}
        >
            <div className="form-row">
                {existing ? null : (
                    <Combobox
                        value={name} onChange={setName} placeholder="Flag name" ariaLabel="Flag name" icon="search"
                        options={state.flags.map(f => ({value: f.name, detail: `= ${f.value}`}))}
                        autoFocus
                    />
                )}
                <IntegerField value={value} onChange={setValue} ariaLabel="Value" autoFocus={!!existing}/>
            </div>
        </Modal>
    );
}

/**
 * A decision waiting for a value - a planet, a unit type, a name - as one dialog. The field starts
 * at what the event itself names and applies on OK; Fire and Rule out answer at once without a
 * value.
 */
function DecisionDialog(props: {
    state: StorySimStateDto; item: StorySimInterventionDto; actions: SimActions; onClose: () => void;
}): React.JSX.Element {
    const {state, item, actions} = props;
    const picker = decisionPicker(item, state)!;
    const [text, setText] = useState(() => picker.initial);
    const value = text.trim();
    const answer = (run: () => void): void => {
        run();
        props.onClose();
    };
    return (
        <Modal
            form title={picker.title} confirmLabel="OK"
            canConfirm={value.length > 0} disabledReason={`${picker.noun} required`}
            onConfirm={() => {
                const change = value ? picker.change(value) : null;
                if (change) {
                    answer(() => actions.world(change));
                }
            }}
            onCancel={props.onClose}
            actions={<>
                <Button title="Assume the trigger met" onClick={() => answer(() => actions.satisfy(item.nodeId))}>
                    <Icon name="decision" size={13}/>Fire
                </Button>
                <Button title="Never fires in this run - stays armed"
                        onClick={() => answer(() => actions.ruleOut(item.nodeId, true))}>
                    <Icon name="remove" size={13}/>Rule out
                </Button>
            </>}
        >
            <Combobox
                value={text} onChange={setText} placeholder={picker.noun} ariaLabel={picker.noun} icon="search"
                options={picker.options.map(o => ({value: o}))} autoFocus
            />
        </Modal>
    );
}

/**
 * What a decision's value is and how it becomes a world change - null when the decision takes no
 * value (a script event, a battle's outcome, a trigger with no facet), which opens as an answer dialog instead.
 */
function decisionPicker(item: StorySimInterventionDto, state: StorySimStateDto): {
    title: string; noun: string; initial: string; options: string[];
    change: (value: string) => StorySimWorldChangeDto | null;
} | null {
    if (!item.facet || item.battleKey || item.kind === 'lua' || item.kind === 'battle') {
        return null;
    }
    const faction = factionOf(item, state);
    const candidates = pickerCandidates(item.facet, item.options, state.world, faction);
    if (candidates.kind === 'none') {
        return null;
    }
    const facet = item.facet;
    const base: StorySimWorldChangeDto = {...(item.suggested ?? {kind: facet}), kind: facet};
    const kind = candidates.kind;
    return {
        title: kind === 'planet' ? 'Select a planet' : kind === 'unit' ? 'Select a unit type' : 'Enter a name',
        noun: kind === 'planet' ? 'Planet' : kind === 'unit' ? 'Unit type' : 'Name',
        initial: (kind === 'planet' ? base.planet : kind === 'unit' ? base.unitType : base.name) ?? '',
        options: [...new Set([...candidates.preferred, ...candidates.all])],
        change: value => kind === 'planet' ? {...base, planet: value, faction: base.faction ?? faction}
            : kind === 'unit' ? {...base, unitType: value, faction: base.faction ?? faction}
                : {...base, name: value},
    };
}

function ScriptDialog(props: DetailProps & { scriptUri: string }): React.JSX.Element {
    const {state, actions} = props;
    const script = state.luaStates.find(s => s.scriptUri === props.scriptUri);
    if (!script) {
        return <Modal title="Script" onCancel={props.onClose}><p className="modal-note">Not in this plot</p></Modal>;
    }
    const stateNodeId = (name: string): string => `${script.scriptUri}#lua#${name.toLowerCase()}`;
    return (
        <Modal
            title={script.scriptName} onCancel={props.onClose}
            actions={script.current
                ? <ShowInGraph nodeId={stateNodeId(script.current)} actions={actions} onClose={props.onClose}/>
                : undefined}
        >
            <DockSection title="Where it is">
                <div className="sim-row static">
                    <span className="sim-row-name">Current</span>
                    <span className="sim-row-value">{script.current ?? 'not started'}</span>
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
                    <p className="field-note">Story_Event ids the plot listens for</p>
                    <div className="sim-chip-row">
                        {state.luaNotifications.map(id => (
                            <button type="button" className="btn sim-pick" key={id} title={`Story_Event("${id}")`}
                                    onClick={() => actions.luaNotify(id)}>{id}</button>
                        ))}
                    </div>
                </DockSection>
            ) : null}
        </Modal>
    );
}

// ── Foot ─────────────────────────────────────────────────────────────────────

/**
 * The tick transport, in the dock foot, laid out as the preview's animation player: the transport
 * row - |<< back to the start, |< one tick back, play or pause, >| one tick, >>| run to the next
 * decision, and Break on gates at the far end where a player keeps its record button - then the
 * status readout in the scrubber's slot, then the speed, one slider over an ordered axis.
 */
export function SimTransport(props: {
    state: StorySimStateDto;
    pace: SimPace;
    playing: boolean;
    setPace: (pace: SimPace) => void;
    actions: SimActions;
    labelOf: (id: string) => string | undefined;
    onSelect: (selection: SimSelection) => void;
    /** What the server last refused, shown under the readout until the next state. */
    notice: string | null;
}): React.JSX.Element {
    const {state, pace, playing, actions} = props;
    const halted = !!state.haltedAt;
    // Never gated on decisions: the engine's clock runs whatever the story waits on, and a real
    // campaign waits on hundreds of things from tick 0. The title says when a tick is idle.
    const idle = waitingOnAuthor(state);
    // Gated on a frozen session: the galaxy while a battle is up, a battle once it has resolved.
    // A disabled control always carries its reason, so the gate is one object spread in.
    const frozen = frozenReason(state);
    const gate = frozen === null ? {} : {disabled: true as const, disabledReason: frozen};
    const stop = speedStopOf(pace);
    return (
        <div className="player sim-transport">
            <div className="player-row">
                <IconButton
                    icon="firstFrame"
                    title="Restart"
                    disabled={state.tick === 0 || frozen !== null}
                    disabledReason={frozen ?? 'Already at tick 0'}
                    onClick={actions.restart}
                />
                <IconButton
                    icon="previousClip"
                    title="Back one tick"
                    disabled={state.tick === 0 || frozen !== null}
                    disabledReason={frozen ?? 'Already at tick 0'}
                    onClick={actions.back}
                />
                <IconButton
                    icon={playing ? 'pause' : 'play'}
                    title={pace.mode === 'step' ? 'One tick'
                        : playing ? 'Pause'
                            : halted ? 'Resume from the breakpoint'
                                : idle ? 'Play - clock idle' : 'Play'}
                    {...gate}
                    onClick={actions.playPause}
                />
                <IconButton
                    icon="nextClip"
                    title={idle ? 'One tick - clock idle' : 'One tick'}
                    {...gate}
                    onClick={actions.tick}
                />
                <IconButton
                    icon="lastFrame"
                    title="Run to next decision or breakpoint"
                    {...gate}
                    onClick={actions.runToDecision}
                />
                <IconButton
                    icon="breakpoint"
                    className={'sim-break-gates' + (state.breakOnGates ? ' active' : '')}
                    pressed={state.breakOnGates}
                    title="Break on gates"
                    onClick={() => actions.setBreakpoints(state.breakpoints, !state.breakOnGates)}
                />
            </div>
            <SimStatus state={state} playing={playing} labelOf={props.labelOf} onSelect={props.onSelect}
                       onPlayPause={actions.playPause} onRetry={actions.retryBattle}/>
            {props.notice ? <span className="sim-notice" title={props.notice}>{props.notice}</span> : null}
            <Field label="Speed" value={speedLabel(pace)}>
                <input
                    type="range" min={0} max={SPEED_STOPS.length - 1} step={1} value={stop}
                    title="Speed" aria-label="Speed"
                    onChange={e => props.setPace(SPEED_STOPS[Number(e.target.value)])}
                />
            </Field>
        </div>
    );
}

/**
 * The simulation's view toggles, on a plate in the canvas's top-left corner - where the preview
 * keeps its viewport toggles - so every view setting sits in one place: the trace panel and the
 * three lenses on the running story.
 */
export function SimViewToggles(props: {
    lenses: SimLenses;
    traceOpen: boolean;
    setLenses: (lenses: SimLenses) => void;
    setTraceOpen: (open: boolean) => void;
}): React.JSX.Element {
    const {lenses} = props;
    return (
        <div className="stage-chrome icon-only lens-corner">
            <IconButton
                icon="trace"
                className={props.traceOpen ? 'active' : undefined}
                pressed={props.traceOpen}
                title="Trace"
                onClick={() => props.setTraceOpen(!props.traceOpen)}
            />
            <IconButton
                icon="flow"
                className={lenses.hideFlow ? undefined : 'active'}
                pressed={!lenses.hideFlow}
                title="Flow"
                onClick={() => props.setLenses({...lenses, hideFlow: !lenses.hideFlow})}
            />
            <IconButton
                icon="fire"
                className={lenses.activeOnly ? 'active' : undefined}
                pressed={lenses.activeOnly}
                title="Active path"
                onClick={() => props.setLenses({...lenses, activeOnly: !lenses.activeOnly})}
            />
            <IconButton
                icon="script"
                className={lenses.hideLua ? undefined : 'active'}
                pressed={!lenses.hideLua}
                title="Script states"
                onClick={() => props.setLenses({...lenses, hideLua: !lenses.hideLua})}
            />
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
    // A trace reads newest-last, so new rows keep the end in view. Measured on the shadow run:
    // after a tick the panel sat at its top showing tick 0 rows while the new ones were below the
    // fold. Only while the reader is not filtering - a filter is them looking at something else.
    const endRef = useRef<HTMLDivElement>(null);
    useEffect(() => {
        if (!query) {
            endRef.current?.scrollIntoView({block: 'end'});
        }
    }, [shown.length, query]);
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
                    type="text" placeholder="Filter (t12 = tick 12)" value={query}
                    onChange={e => setQuery(e.target.value)}
                />
                <IconButton icon="copy" title="Copy" disabled={shown.length === 0}
                            disabledReason="Nothing to copy" onClick={() => props.actions.copy(copyText())}/>
            </div>
            <div className="problem-list">
                {tail.length < shown.length ? (
                    <div className="sim-trace-row muted">{shown.length - tail.length} earlier rows not shown - narrow
                        the
                        filter</div>
                ) : null}
                {tail.map((row, i) => (
                    <div
                        className={'sim-trace-row cause-' + row.cause + (row.nodeId ? ' clickable' : '')}
                        key={row.seq}
                        ref={i === tail.length - 1 ? endRef : undefined}
                        title={row.nodeId ? 'Show in graph' : undefined}
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
            </div>
        </ProblemsPanel>
    );
}

// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

/**
 * The simulation dock's model: how interventions group into decisions, how trace steps become
 * rows, what would arm a node, and which candidates a picker lists first. Pure functions over
 * the wire types; the components in SimDock.tsx render them and storyGraph.tsx owns the state.
 */

import {
    StoryGraphEdgeDto, StoryGraphNodeDto, StorySimInterventionDto, StorySimStepDto, StorySimWorldChangeDto,
    StorySimWorldDto,
} from '../../protocol/story';

// ── Decisions ────────────────────────────────────────────────────────────────

/** What kind of answer a decision takes: a world change, a battle outcome, a Lua notification, or an assumption. */
/**
 * The decision the story hangs on, to open beside the dock by itself: a pending or starting battle
 * (the galaxy is frozen for it), else the decision armed last - the next step of the chain that
 * just ran, not a world listener armed at plot load - else the first in the dock's order. Null
 * when nothing waits.
 */
export function hangingDecision(
    interventions: readonly StorySimInterventionDto[],
    steps: readonly StorySimStepDto[],
): StorySimInterventionDto | null {
    const battle = interventions.find(i => i.kind === 'battle');
    if (battle) {
        return battle;
    }
    if (interventions.length === 0) {
        return null;
    }
    const waiting = new Set(interventions.map(i => i.nodeId));
    for (let i = steps.length - 1; i >= 0; i--) {
        const step = steps[i];
        if (step.to === 'Armed' && waiting.has(step.nodeId)) {
            return interventions.find(x => x.nodeId === step.nodeId) ?? null;
        }
    }
    return groupDecisions(interventions)[0]?.items[0] ?? null;
}

/**
 * Where the galaxy takes the reader back once a battle resolved inside its own panel: the galactic
 * event behind the exit portal for that outcome (the victory listener for a win, the loss listener
 * for a loss), else any exit portal (the summary-closed listener, say), else the entry portal's
 * event - the link that brought the story here. Null when the graph has no portal at all.
 */
export function galacticReturnNode(
    nodes: readonly StoryGraphNodeDto[],
    edges: readonly StoryGraphEdgeDto[],
    outcome: 'won' | 'lost' | string,
): string | null {
    const portals = nodes.filter(n => n.kind === 'GalacticPortal' && n.portalTarget);
    if (portals.length === 0) {
        return null;
    }
    const exitIds = new Set(edges.filter(e => e.kind === 'Tactical' && e.label === 'outcome').map(e => e.toId));
    const exits = portals.filter(p => exitIds.has(p.id));
    const typeOf = (p: StoryGraphNodeDto): string => (p.eventType ?? '').toUpperCase();
    const wanted = outcome === 'won' ? 'STORY_VICTORY' : outcome === 'lost' ? 'STORY_MISSION_LOST' : null;
    const opposite = outcome === 'won' ? 'STORY_MISSION_LOST' : outcome === 'lost' ? 'STORY_VICTORY' : null;
    // The listener for this outcome; else the summary-closed listener, which the story goes on
    // from whatever happened (the tutorial's win is a flag read behind it); else any exit that is
    // not the other outcome's listener - the game never takes that route now.
    const landing = (wanted && exits.find(p => typeOf(p) === wanted))
        ?? exits.find(p => typeOf(p) === 'STORY_GENERIC')
        ?? exits.find(p => typeOf(p) !== opposite)
        ?? portals[0];
    return landing.portalTarget ?? null;
}

export type PortalPickAction = 'select' | 'open' | 'none';

/**
 * What a pick on a battle's portal does. In Simulation the portal is the battle's decision, so a
 * pick selects it beside the dock - fight, auto-resolve, won or lost live there - and never opens
 * the other panel under the pointer; in View the portal is a doorway and the pick goes through;
 * in Edit a pick is the start of a drag. The jump arrow on the portal opens the graph in every
 * mode, into Simulation while the galaxy simulates.
 */
export function portalPickAction(mode: 'view' | 'edit' | 'simulate'): PortalPickAction {
    return mode === 'simulate' ? 'select' : mode === 'view' ? 'open' : 'none';
}

export type DecisionKind = 'world' | 'tactical' | 'lua' | 'assume';

export interface DecisionGroup {
    kind: DecisionKind;
    items: StorySimInterventionDto[];
}

export function decisionKind(intervention: StorySimInterventionDto): DecisionKind {
    if (intervention.kind === 'lua') {
        return 'lua';
    }
    // The pending battle sits with the tactical decisions: it is decided on its portal like them.
    if (intervention.kind === 'tactical' || intervention.kind === 'battle') {
        return 'tactical';
    }
    return intervention.facet ? 'world' : 'assume';
}

const DECISION_ORDER: DecisionKind[] = ['world', 'tactical', 'lua', 'assume'];

/** Decisions grouped by kind in a fixed order, each group in wire order; empty groups are dropped. */
export function groupDecisions(interventions: readonly StorySimInterventionDto[]): DecisionGroup[] {
    const groups = new Map<DecisionKind, StorySimInterventionDto[]>();
    for (const intervention of interventions) {
        const kind = decisionKind(intervention);
        const list = groups.get(kind) ?? [];
        list.push(intervention);
        groups.set(kind, list);
    }
    return DECISION_ORDER.filter(kind => groups.has(kind)).map(kind => ({kind, items: groups.get(kind)!}));
}

// ── Trace ────────────────────────────────────────────────────────────────────

export interface TraceRow {
    seq: number;
    tick: number;
    nodeId: string;
    node: string;
    from: string | null;
    to: string | null;
    via: string | null;
    viaId: string | null;
    cause: string;
    detail: string | null;
}

/** A node's display name: the graph label, or for a script state its name after the marker. */
export function labelFor(nodeId: string, labelOf: (id: string) => string | undefined): string {
    const known = labelOf(nodeId);
    if (known) {
        return known;
    }
    const lua = nodeId.indexOf('#lua#');
    if (lua >= 0) {
        return nodeId.slice(lua + 5);
    }
    const hash = nodeId.lastIndexOf('#');
    return hash >= 0 ? nodeId.slice(hash + 1) : nodeId;
}

export function traceRows(steps: readonly StorySimStepDto[], labelOf: (id: string) => string | undefined): TraceRow[] {
    return steps.map(step => ({
        seq: step.seq,
        tick: step.tick,
        nodeId: step.nodeId,
        node: step.nodeId ? labelFor(step.nodeId, labelOf) : '',
        from: step.from ?? null,
        to: step.to ?? null,
        via: step.sourceNodeId ? labelFor(step.sourceNodeId, labelOf) : null,
        viaId: step.sourceNodeId ?? null,
        cause: step.cause,
        detail: step.detail ?? null,
    }));
}

/** Case-insensitive substring over everything a row shows; "t12" narrows to a tick. */
export function filterTrace(rows: readonly TraceRow[], query: string): TraceRow[] {
    const trimmed = query.trim().toLowerCase();
    if (!trimmed) {
        return [...rows];
    }
    const tickMatch = /^t(\d+)$/.exec(trimmed);
    if (tickMatch) {
        const tick = Number(tickMatch[1]);
        return rows.filter(row => row.tick === tick);
    }
    return rows.filter(row =>
        [row.node, row.via ?? '', row.cause, row.detail ?? '', row.to ?? '', row.from ?? '']
            .join(' ').toLowerCase().includes(trimmed));
}

// ── Arming ───────────────────────────────────────────────────────────────────

export interface ArmingMember {
    nodeId: string;
    label: string;
    fired: boolean;
}

/** One prereq line (an AND of members); the event arms when any line is satisfied. */
export interface ArmingLine {
    satisfied: boolean;
    members: ArmingMember[];
}

const AND_KINDS = new Set(['AndJunction', 'StagingAnd']);
const OR_KINDS = new Set(['OrJunction', 'StagingOr']);

/**
 * What would arm a node, read off the graph: the prereq edges into it, through its AND and OR
 * junctions, with each member's fired state from the simulation. Empty for a root.
 */
export function armingLines(
    nodeId: string,
    nodes: readonly StoryGraphNodeDto[],
    edges: readonly StoryGraphEdgeDto[],
    lifecycleOf: (id: string) => string | null | undefined,
): ArmingLine[] {
    const byId = new Map(nodes.map(n => [n.id, n]));
    const incoming = (id: string): string[] =>
        edges.filter(e => e.kind === 'Prereq' && e.toId === id).map(e => e.fromId);
    const member = (id: string): ArmingMember => ({
        nodeId: id, label: byId.get(id)?.label ?? labelFor(id, () => undefined), fired: lifecycleOf(id) === 'Fired',
    });
    const lineOf = (members: ArmingMember[]): ArmingLine => ({
        satisfied: members.length > 0 && members.every(m => m.fired), members,
    });
    const linesFrom = (sourceId: string): ArmingLine[] => {
        const source = byId.get(sourceId);
        if (!source) {
            return [lineOf([member(sourceId)])];
        }
        if (AND_KINDS.has(source.kind)) {
            return [lineOf(incoming(sourceId).filter(id => byId.get(id)?.kind === 'Event').map(member))];
        }
        if (OR_KINDS.has(source.kind)) {
            return incoming(sourceId).flatMap(linesFrom);
        }
        return [lineOf([member(sourceId)])];
    };
    return incoming(nodeId).flatMap(linesFrom);
}

// ── Pickers ──────────────────────────────────────────────────────────────────

export type PickerKind = 'planet' | 'unit' | 'name' | 'none';

export interface PickerCandidates {
    kind: PickerKind;
    /** What the facts say fits first: the event's own candidates, else the world's likely ones. */
    preferred: string[];
    /** Everything the world knows; the author may still type anything. */
    all: string[];
}

const PLANET_FACETS = new Set([
    'capturePlanet', 'enterPlanet', 'bounced', 'selectPlanet', 'corrupt', 'planetDestroyed',
    'battleStarted', 'battleWon', 'battleLost',
]);
const UNIT_FACETS = new Set(['buildUnit', 'destroyUnit', 'destroyAll', 'captureUnit', 'moveUnit']);
const NAME_FACETS = new Set(['clickGui', 'generic']);

/**
 * The candidates a decision's picker lists. Prefiltered by the facts (a capture lists planets the
 * faction does not hold; a destroy lists types someone else has), never gated by them: `all` is
 * the whole world and the author can type past it.
 */
export function pickerCandidates(
    facet: string | null | undefined,
    options: readonly string[],
    world: StorySimWorldDto,
    faction: string | null | undefined,
): PickerCandidates {
    const own = (owner: string | null | undefined): boolean =>
        !!faction && !!owner && owner.toLowerCase() === faction.toLowerCase();
    if (facet && PLANET_FACETS.has(facet)) {
        const all = world.planets.map(p => p.name);
        const likely = facet === 'capturePlanet' ? world.planets.filter(p => !own(p.owner)).map(p => p.name) : all;
        return {kind: 'planet', preferred: options.length ? [...options] : likely, all};
    }
    if (facet && UNIT_FACETS.has(facet)) {
        const all = [...new Set(world.units.map(u => u.type))];
        const likely = facet === 'destroyUnit' || facet === 'destroyAll'
            ? [...new Set(world.units.filter(u => !own(u.owner)).map(u => u.type))]
            : all;
        return {kind: 'unit', preferred: options.length ? [...options] : likely, all};
    }
    if (facet && NAME_FACETS.has(facet)) {
        return {kind: 'name', preferred: [...options], all: [...options]};
    }
    return {kind: 'none', preferred: [], all: []};
}

// ── Change text ──────────────────────────────────────────────────────────────

/** A world change as the author reads it - the label of a suggested action. */
export function describeChange(change: StorySimWorldChangeDto): string {
    const who = change.faction ?? '';
    switch (change.kind) {
        case 'capturePlanet':
            return `${who} captures ${change.planet ?? '?'}`;
        case 'buildUnit':
            return `${who} builds ${change.unitType ?? '?'}`;
        case 'destroyUnit':
            return `${change.unitType ?? '?'} destroyed`;
        case 'destroyAll':
            return `All ${change.unitType ?? 'units'} of ${who} destroyed`;
        case 'captureUnit':
            return `${who} captures ${change.unitType ?? '?'}`;
        case 'setTech':
            return `${who} tech level ${change.amount ?? 1}`;
        case 'addCredits':
            return `${who} credits +${change.amount ?? 0}`;
        case 'battleWon':
            return `${who} wins${change.planet ? ' at ' + change.planet : ''}`;
        case 'battleLost':
            return `${who} loses${change.name ? ' ' + change.name : ''}`;
        case 'battleStarted':
            return `${change.mode ?? 'a'} battle at ${change.planet ?? '?'}`;
        case 'enterPlanet':
            return `${who} enters ${change.planet ?? '?'}`;
        case 'bounced':
            return `${who} bounced at ${change.planet ?? '?'}`;
        case 'moveUnit':
            return `${change.unitType ?? '?'} to ${change.planet ?? '?'}`;
        case 'clickGui':
            return `Click ${change.name ?? '?'}`;
        case 'selectPlanet':
            return `Select ${change.planet ?? '?'}`;
        case 'corrupt':
            return `Corrupt ${change.planet ?? '?'}`;
        case 'beginEra':
            return `Era ${change.amount ?? 1}`;
        case 'planetDestroyed':
            return `${change.planet ?? '?'} destroyed`;
        case 'generic':
            return `Trigger ${change.name ?? '?'}`;
        case 'assumeMet':
            return 'Assume the trigger met';
        default:
            return change.kind;
    }
}

// ── Pace ─────────────────────────────────────────────────────────────────────

/** How the author advances ticks: a 1 s pulse, one tick per press, or a custom rate. */
/**
 * Whether play should pick itself up again after the author answered something.
 *
 * Play pauses itself when the clock has nothing left but a decision. Once the author has answered,
 * the reader who pressed play expects the story to move on without pressing it again - unless the
 * setting is off, play was never on, a breakpoint holds the run, or the story is still waiting on
 * another answer, in which case resuming would only pause again on the next state.
 */
export function shouldResumeAfterAnswer(args: {
    autoResume: boolean;
    pausedForWait: boolean;
    stillWaiting: boolean;
    halted: boolean;
}): boolean {
    return args.autoResume && args.pausedForWait && !args.stillWaiting && !args.halted;
}

export interface SimPace {
    mode: 'pulse' | 'step' | 'custom';
    ticksPerSecond: number;
}

export const DEFAULT_PACE: SimPace = {mode: 'pulse', ticksPerSecond: 4};

/** Milliseconds between tick requests for a pace; step mode never runs a timer. */
export function paceIntervalMs(pace: SimPace): number {
    return pace.mode === 'custom' ? 1000 / Math.max(0.25, Math.min(30, pace.ticksPerSecond)) : 1000;
}

/** A stored pace, or the default when the stored shape is not one. */
export function parsePace(raw: string | null): SimPace {
    if (!raw) {
        return DEFAULT_PACE;
    }
    try {
        const parsed = JSON.parse(raw) as Partial<SimPace>;
        if (parsed.mode === 'pulse' || parsed.mode === 'step' || parsed.mode === 'custom') {
            const tps = Number(parsed.ticksPerSecond);
            return {mode: parsed.mode, ticksPerSecond: tps > 0 ? Math.min(30, tps) : DEFAULT_PACE.ticksPerSecond};
        }
    } catch {
        // Not JSON: the default is fine.
    }
    return DEFAULT_PACE;
}

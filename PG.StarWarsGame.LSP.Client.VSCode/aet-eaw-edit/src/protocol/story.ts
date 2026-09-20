// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The story editor's wire contract. Mirrors PG.StarWarsGame.LSP.Server/Story/StoryProtocol.cs and
// StoryCommandProtocol.cs - see ../protocol/README.md for why this is declared exactly once.

// ── aet/getStoryPlots - campaign navigator feed ──────────────────────────────

/**
 * @param uri Resolved canonical document URI, or null when the chain is broken. Manifest entries
 *     and on-disk names differ in casing throughout vanilla data, so clients must open this rather
 *     than search for `file`.
 */
export interface StoryPlotThreadDto {
    file: string;
    suspended: boolean;
    uri?: string | null;
}

export interface StoryLuaScriptDto {
    name: string;
    uri?: string | null;
}

/**
 * A tactical battle of the faction: its own graph, opened through `GetStoryGraph`'s `scope`.
 * `rank` is the order in which the galactic story reaches it.
 */
export interface StoryBattleDto {
    key: string;
    label: string;
    entryEventIds: string[];
    rank: number;
    /**
     * The battle's plot files as its tactical manifest lists them, each with the document it
     * resolved to. The faction's own thread list never carries these.
     */
    threads: StoryPlotThreadDto[];
}

export interface StoryFactionDto {
    faction: string;
    manifestFile: string;
    threads: StoryPlotThreadDto[];
    luaScripts: StoryLuaScriptDto[];
    /** Absent from an older server; treat as none. */
    battles?: StoryBattleDto[] | null;
}

/** `set` is the Campaign_Set this campaign belongs to; null when it declares none. */
export interface StoryCampaignDto {
    name: string;
    factions: StoryFactionDto[];
    set?: string | null;
}

export interface GetStoryPlotsResult {
    campaigns: StoryCampaignDto[];
    error?: string | null;
}

// ── aet/getStoryGraph, aet/previewStoryGraph ─────────────────────────────────

export interface StoryParamValueDto {
    position: number;
    value: string;
}

export interface StoryGraphNodeDto {
    id: string;
    kind: string;
    label: string;
    threadUri?: string | null;
    line?: number | null;
    eventType?: string | null;
    rewardType?: string | null;
    branch?: string | null;
    lifecycle?: string | null;
    reachable: boolean;
    eventParams?: StoryParamValueDto[] | null;
    rewardParams?: StoryParamValueDto[] | null;
    perpetual?: boolean;
    storyDialog?: string | null;
    storyChapter?: number | null;
    /** For a GalacticPortal node: the galactic event it stands for. */
    portalTarget?: string | null;
}

export interface StoryGraphEdgeDto {
    fromId: string;
    toId: string;
    kind: string;
    label?: string | null;
}

export interface GetStoryGraphResult {
    nodes: StoryGraphNodeDto[];
    edges: StoryGraphEdgeDto[];
    error?: string | null;
    /**
     * Facets of the whole campaign, deliberately NOT of `nodes`. A filter dropdown lists what you
     * could switch to, so deriving it from the filtered result leaves it offering only the value
     * already selected. Optional so an older server degrades to the previous behaviour.
     */
    branches?: string[] | null;
    threads?: string[] | null;
}

/**
 * The graph filters the webview holds and the panel forwards.
 *
 * Client-only - the server takes these as four separate request fields - but both sides have to
 * agree on the object, so it belongs to the contract between them. Every field is optional here
 * and required in the webview's own state, which is why the two are not the same type: the panel
 * receives whatever the webview chose to send.
 */
export interface GraphFilters {
    nameFilter?: string;
    branch?: string;
    lifecycle?: string;
    reachableFrom?: string;
    /**
     * Which way `reachableFrom` reaches: `Downstream` (what the event leads to), `Upstream` (what
     * leads to it) or `Both`. Absent is Downstream, which is what the filter did before the other
     * two directions existed.
     */
    reachableDirection?: string;
    /**
     * `Active` or `Suspended` - how the faction's plot manifest registers the thread an event
     * lives in. Absent or empty keeps both, which is the whole chain.
     *
     * Not the same question as `lifecycle`: a suspended plot's events are Inactive, and so is an
     * event in a running plot whose prereq has not fired.
     */
    plotState?: string;
}

// ── aet/getStoryNodeDetail ───────────────────────────────────────────────────

export interface StoryTagDto {
    name: string;
    value: string;
}

export interface StoryNodeDetailDto {
    id: string;
    name: string;
    threadUri?: string | null;
    line: number;
    eventType?: string | null;
    eventFilter?: string | null;
    eventParams: StoryParamValueDto[];
    rewardType?: string | null;
    rewardParams: StoryParamValueDto[];
    prereqGroups: string[][];
    branch?: string | null;
    perpetual: boolean;
    storyDialog?: string | null;
    storyChapter?: number | null;
    tags: StoryTagDto[];
}

export interface GetStoryNodeDetailResult {
    node?: StoryNodeDetailDto | null;
    error?: string | null;
}

// ── aet/getStorySchema ───────────────────────────────────────────────────────

/** `enumValues` ships inline so enum params render as dropdowns without a round trip. */
export interface StoryParamSchemaDto {
    position: number;
    valueType: string;
    referenceType?: string | null;
    enumName?: string | null;
    optional: boolean;
    description?: string | null;
    enumValues?: string[] | null;
}

export interface StoryTypeSchemaDto {
    name: string;
    description?: string | null;
    untested: boolean;
    params: StoryParamSchemaDto[];
}

export interface GetStorySchemaResult {
    eventTypes: StoryTypeSchemaDto[];
    rewardTypes: StoryTypeSchemaDto[];
    error?: string | null;
}

// ── aet/getStoryParamOptions ─────────────────────────────────────────────────

export interface StoryParamOptionDto {
    value: string;
    detail?: string | null;
}

export interface GetStoryParamOptionsResult {
    options: StoryParamOptionDto[];
    error?: string | null;
}

// ── aet/resolveStoryReference ────────────────────────────────────────────────

export interface ResolveStoryReferenceResult {
    uri?: string | null;
    line: number;
    column: number;
    error?: string | null;
}

// ── aet/getStoryDiagnostics, aet/validateStoryCommandBatch ───────────────────

/**
 * @param nodeId The graph node the diagnostic belongs to; null for file-level problems.
 * @param side `event`/`reward` when the range falls inside a param slot.
 * @param position 0-based param slot, when `side` is set.
 */
export interface StoryDiagnosticDto {
    nodeId?: string | null;
    side?: string | null;
    position?: number | null;
    severity: string;
    message: string;
    uri: string;
    line: number;
    column: number;
}

export interface GetStoryDiagnosticsResult {
    diagnostics: StoryDiagnosticDto[];
    error?: string | null;
}

// ── aet/getStoryLayout / aet/setStoryLayout ──────────────────────────────────

export interface StoryLayoutEntryDto {
    /** The thread's document URI. The server keys the sidecar by a hash of it - see DocumentKey. */
    threadUri: string;
    eventName: string;
    x: number;
    y: number;
    /**
     * Set for a virtual node - a junction, a portal, a tactical stub, a script state - which the
     * sidecar names by id; the thread and event are then empty. Absent for an event.
     */
    nodeId?: string | null;
}

export interface GetStoryLayoutResult {
    entries: StoryLayoutEntryDto[];
    error?: string | null;
}

// ── aet/executeStoryCommand, aet/applyStoryCommandBatch ──────────────────────

export interface ExecuteStoryCommandResult {
    success: boolean;
    error?: string | null;
}

/** @param failedIndex 0-based index of the command that failed, when `success` is false. */
export interface ApplyStoryCommandBatchResult {
    success: boolean;
    failedIndex?: number | null;
    error?: string | null;
}

// ── aet/storySim* - the simulator's flat state document ──────────────────────
// One flat document per response rather than deltas: the graphs are small and the bookkeeping is
// not worth it. No dictionaries anywhere in here - OmniSharp camel-cases dictionary keys, which
// would corrupt flag names and node ids.

export interface StorySimFlagDto {
    name: string;
    value: number;
}

export interface StorySimNodeStateDto {
    nodeId: string;
    lifecycle: string;
    /** How often the event fired since Start (a perpetual event counts every time). */
    fireCount: number;
    /** An armed clock or flag gate: "4/10 s", "FLAG_X 2 of 3"; null when the event has none. */
    gateLabel?: string | null;
    gateProgress?: number | null;
}

/**
 * One trace transition. `from`/`to` are lifecycle names, both null for a step that is not a
 * lifecycle change (a flag write, an owed completion, an ignored reward). `sourceNodeId` is the
 * event whose firing caused this one - the edge to animate runs from it to `nodeId`. `seq` is the
 * position in the whole trace; every request carries `sinceSeq` and gets only the steps after it.
 */
export interface StorySimStepDto {
    tick: number;
    seq: number;
    nodeId: string;
    from?: string | null;
    to?: string | null;
    sourceNodeId?: string | null;
    cause: string;
    detail?: string | null;
}

export interface StorySimInterventionDto {
    kind: string;
    nodeId: string;
    eventName: string;
    eventType?: string | null;
    options: string[];
    /** The world change kind that fires this event when its type reads the world. */
    facet?: string | null;
    /** A ready change built from the event's own parameters; null when the author must pick. */
    suggested?: StorySimWorldChangeDto | null;
    /**
     * A tactical decision's battle: the one the panel is inside, or the one whose entry this
     * listener follows. Absent when no battle can be named and the answer is a plain world change.
     */
    battleKey?: string | null;
}

/** One battle of the faction as the galactic session sees it: notStarted, running, won or lost. */
export interface StorySimBattleDto {
    key: string;
    label: string;
    status: string;
    /** The battle session's own tick while it runs. */
    tick: number;
    /**
     * The flags the battle's own rewards can write, with the value each would set - offered on the
     * portal as picks when the battle is decided without being played.
     */
    writes?: StorySimFlagDto[] | null;
}

/** An author's change to the world; fields a kind does not read stay undefined. */
export interface StorySimWorldChangeDto {
    kind: string;
    planet?: string | null;
    unitType?: string | null;
    faction?: string | null;
    name?: string | null;
    mode?: string | null;
    amount?: number;
    flags?: StorySimFlagDto[] | null;
    nodeId?: string | null;
}

export interface StorySimPlanetDto {
    name: string;
    owner?: string | null;
    revealed: boolean;
    corrupted: boolean;
    destroyed: boolean;
}

export interface StorySimUnitDto {
    type: string;
    owner: string;
    planet: string;
    count: number;
}

/** The fact table: seeded from the campaign, then written only by rewards and the author. */
export interface StorySimWorldDto {
    planets: StorySimPlanetDto[];
    units: StorySimUnitDto[];
    tech: StorySimFlagDto[];
    credits: StorySimFlagDto[];
    era?: string | null;
    counters: StorySimFlagDto[];
    objectives: string[];
}

export interface StorySimStateDto {
    running: boolean;
    /** Ticks run so far; one tick is `clockStepSeconds` of story time. */
    tick: number;
    clock: number;
    clockStepSeconds: number;
    flags: StorySimFlagDto[];
    nodes: StorySimNodeStateDto[];
    interventions: StorySimInterventionDto[];
    luaNotifications: string[];
    /** The last lines of the text log; the trace in `steps` is the complete record. */
    log: string[];
    /** Steps with seq >= the request's sinceSeq. */
    steps: StorySimStepDto[];
    totalSteps: number;
    breakpoints: string[];
    breakOnGates: boolean;
    /** The event whose breakpoint halted the last run, until the next command. */
    haltedAt?: string | null;
    world: StorySimWorldDto;
    luaStates: StorySimLuaStateDto[];
    /**
     * How many things the clock alone can still change: armed timers, owed completions, script
     * work. Zero means nothing more happens until the author answers a decision. Optional so an
     * older server reads as "the clock may still run".
     */
    clockPending?: number;
    /** The battle this session runs, as the plots feed keys it; absent or null at the galactic level. */
    scope?: string | null;
    /**
     * Galactic only: the label of the battle whose session is up. The game freezes the galaxy
     * during a tactical battle, so the galactic session takes no command while this is set.
     */
    pausedFor?: string | null;
    /** Battle only: "won" or "lost" once resolved, after which the session takes no command. */
    outcome?: string | null;
    /** Galactic only: every battle of the faction in play order with its status. */
    battles?: StorySimBattleDto[] | null;
    /**
     * Whether a speech or movie a reward starts is owed its completion on the next tick, the
     * session's option at start. Off, the listener waits for the author or the engine's timeout.
     */
    assumeMediaCompletes?: boolean;
}

/** A campaign script's state machine: where it is, where it goes next, what it still owes. */
export interface StorySimLuaStateDto {
    scriptUri: string;
    scriptName: string;
    current?: string | null;
    next?: string | null;
    pending: StorySimLuaPendingDto[];
}

export interface StorySimLuaPendingDto {
    id: string;
    state: string;
    dueClock: number;
}

export interface StorySimStateResult {
    state?: StorySimStateDto | null;
    error?: string | null;
}

// ── aet/storyGraphChanged, aet/storySimChanged - server push ─────────────────

export interface StoryGraphChangedParams {
    campaigns: string[];
}

export interface StorySimChangedParams {
    campaign: string;
    faction: string;
    /** The session's scope: a battle key, or absent/null for the galactic session. */
    scope?: string | null;
}

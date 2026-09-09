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

export interface StoryFactionDto {
    faction: string;
    manifestFile: string;
    threads: StoryPlotThreadDto[];
    luaScripts: StoryLuaScriptDto[];
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
}

export interface StorySimInterventionDto {
    kind: string;
    nodeId: string;
    eventName: string;
    eventType?: string | null;
    options: string[];
}

export interface StorySimStateDto {
    running: boolean;
    clock: number;
    flags: StorySimFlagDto[];
    nodes: StorySimNodeStateDto[];
    interventions: StorySimInterventionDto[];
    luaNotifications: string[];
    log: string[];
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
}

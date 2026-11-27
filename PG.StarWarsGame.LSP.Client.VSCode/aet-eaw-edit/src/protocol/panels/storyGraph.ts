// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The story graph's messages (#208): StoryGraphPanel and storyGraph.tsx. Parameter completion is not
// here: it goes through the shared request relay as aet/getStoryParamOptions.

import type {
    GraphFilters,
    StoryDiagnosticDto,
    StoryGraphEdgeDto,
    StoryGraphNodeDto,
    StoryLayoutEntryDto,
    StoryNoteDto,
    StoryParamSchemaDto,
    StorySimStateDto,
} from "../story";

/** The editor's modes, as the rotary switch offers them. */
export type StoryEditorMode = "view" | "edit" | "simulate";

/**
 * A story command as the webview builds it: its `kind` and its fields, spread into the server's
 * request beside the campaign and faction the panel adds.
 */
export type StoryCommandPayload = Record<string, unknown>;

// ── webview -> host ─────────────────────────────────────────────────────────

/** The bundle is listening; the panel sends the modes, the schema, the settings and the graph. */
export interface StoryGraphReady {
    type: "ready";
}

/** The swimlane toggles, kept as the workspace's preference. */
export interface StoryGraphSetLanePref {
    type: "setLanePref";
    showThreadLanes: boolean;
    showChapterLanes: boolean;
}

/** Re-fetch the graph under these filters. */
export interface StoryGraphFetch {
    type: "fetch";
    filters: GraphFilters;
}

/** Open a thread file beside the graph, at a line. */
export interface StoryGraphOpenXml {
    type: "openXml";
    threadUri: string;
    line?: number;
}

/** A mutation outside Edit mode's staging; `confirm` is the modal question to ask first. */
export interface StoryGraphCommand {
    type: "command";
    payload: StoryCommandPayload;
    confirm?: string;
}

/** The node positions to store. */
export interface StoryGraphSaveLayout {
    type: "saveLayout";
    entries: StoryLayoutEntryDto[];
}

/** Commit the staged batch. */
export interface StoryGraphSaveBatch {
    type: "saveBatch";
    commands: StoryCommandPayload[];
}

/** Dry-run the staged batch for diagnostics. */
export interface StoryGraphValidateBatch {
    type: "validateBatch";
    commands: StoryCommandPayload[];
}

/** The graph as the staged batch would leave it, under these filters. */
export interface StoryGraphPreviewGraph {
    type: "previewGraph";
    commands: StoryCommandPayload[];
    filters: GraphFilters;
}

/** The staged queue, mirrored so a closed tab can still offer to save it. */
export interface StoryGraphPendingSync {
    type: "pendingSync";
    commands: StoryCommandPayload[];
}

/** A destructive staged gesture, to be confirmed by the host's modal before it is staged. */
export interface StoryGraphConfirmStage {
    type: "confirmStage";
    payload: StoryCommandPayload;
    confirm: string;
}

/** Leaving Edit with staged changes: the host asks, and answers with `dirtyExitChoice`. */
export interface StoryGraphConfirmDirtyExit {
    type: "confirmDirtyExit";
    next: StoryEditorMode;
}

/** A simulation request for this panel's scope (`start`, `tick`, `resolveBattle`, ...). */
export interface StoryGraphSim {
    type: "sim";
    method: string;
    args?: Record<string, unknown>;
}

/** Text for the clipboard, which the webview cannot reach on every host. */
export interface StoryGraphCopy {
    type: "copy";
    text: string;
}

/** Go to the definition of a reference-typed parameter value. */
export interface StoryGraphResolveRef {
    type: "resolveRef";
    value: string;
    referenceType?: string;
}

/** A battle portal: open the battle's own panel. */
export interface StoryGraphOpenScope {
    type: "openScope";
    scope: string;
    label?: string;
    simulate?: boolean;
}

/** A galactic portal on a battle graph: the galactic panel, centred on this node. */
export interface StoryGraphRevealScope {
    type: "revealScope";
    nodeId: string;
}

export type StoryGraphToHost =
    | StoryGraphReady
    | StoryGraphSetLanePref
    | StoryGraphFetch
    | StoryGraphOpenXml
    | StoryGraphCommand
    | StoryGraphSaveLayout
    | StoryGraphSaveBatch
    | StoryGraphValidateBatch
    | StoryGraphPreviewGraph
    | StoryGraphPendingSync
    | StoryGraphConfirmStage
    | StoryGraphConfirmDirtyExit
    | StoryGraphSim
    | StoryGraphCopy
    | StoryGraphResolveRef
    | StoryGraphOpenScope
    | StoryGraphRevealScope;

// ── host -> webview ─────────────────────────────────────────────────────────

/** What the schema says about one type in prose: what the type does, and its notes. */
export interface StoryTypeDoc {
    description: string | null;
    /** Ranked by the server, worst first. */
    notes: StoryNoteDto[];
}

/** The event and reward types with their names, documentation and parameters, by type name. */
export interface StoryGraphSchema {
    type: "schema";
    eventTypes: string[];
    rewardTypes: string[];
    /**
     * The display name of each type the schema has named, by technical name. Two maps and not one:
     * an event and a reward may share a technical name and are named differently.
     */
    eventTypeLabels: Record<string, string>;
    rewardTypeLabels: Record<string, string>;
    /**
     * What the schema says about each type in prose, by technical name: what the type does and
     * its notes. For the documentation a type's tile opens; the parameters travel below.
     */
    eventTypeDocs: Record<string, StoryTypeDoc>;
    rewardTypeDocs: Record<string, StoryTypeDoc>;
    typeNotes: Record<string, StoryNoteDto[]>;
    eventTypeParams: Record<string, StoryParamSchemaDto[]>;
    rewardTypeParams: Record<string, StoryParamSchemaDto[]>;
}

/** The graph, or the staged preview of it (`preview`), with the stored positions. */
export interface StoryGraphGraph {
    type: "graph";
    preview?: boolean;
    campaign: string;
    faction: string;
    scope: string | null;
    nodes: StoryGraphNodeDto[];
    edges: StoryGraphEdgeDto[];
    branches?: string[];
    threads?: string[];
    layout: StoryLayoutEntryDto[];
}

/** The simulation's state after a request, or null when there is none. */
export interface StoryGraphSimState {
    type: "simState";
    state: StorySimStateDto | null;
}

/** The server refused a simulation command; play stops and the dock says why. */
export interface StoryGraphSimError {
    type: "simError";
    message: string;
}

/** The session changed on the server; the webview asks for its state. */
export interface StoryGraphSimChanged {
    type: "simChanged";
}

/** Switch to a mode - a battle entered from the galactic simulation opens in Simulation. */
export interface StoryGraphEnterMode {
    type: "enterMode";
    mode: StoryEditorMode;
}

/** Centre on a node: a portal's landing spot. */
export interface StoryGraphCenterNode {
    type: "centerNode";
    nodeId: string;
}

/** The diagnostics of a validation run. */
export interface StoryGraphDiagnostics {
    type: "diagnostics";
    diagnostics: StoryDiagnosticDto[];
}

/** The campaign changed on the server; the webview re-fetches under its own filters. */
export interface StoryGraphInvalidate {
    type: "invalidate";
}

/** The workspace's swimlane preferences. */
export interface StoryGraphWorkspaceSettings {
    type: "workspaceSettings";
    showThreadLanes: boolean;
    showChapterLanes: boolean;
}

/** Which experimental modes the settings allow. */
export interface StoryGraphAvailableModes {
    type: "availableModes";
    edit: boolean;
    simulate: boolean;
}

/** Which of a type's two names the palette's tiles show, as the setting has it. */
export interface StoryGraphTypeNames {
    type: "typeNames";
    mode: "display" | "technical";
}

/** The answer to `confirmStage`: whether to stage the command it carried. */
export interface StoryGraphConfirmStageResult {
    type: "confirmStageResult";
    proceed: boolean;
    payload: StoryCommandPayload;
}

/** Whether the staged batch was saved. */
export interface StoryGraphSaveResult {
    type: "saveResult";
    success: boolean;
}

/** The answer to `confirmDirtyExit`, with the mode that was asked for. */
export interface StoryGraphDirtyExitChoice {
    type: "dirtyExitChoice";
    choice: "save" | "discard" | "cancel";
    next: StoryEditorMode;
}

/** Why the graph could not be loaded, shown where it would have been. */
export interface StoryGraphError {
    type: "error";
    message: string;
}

export type StoryGraphToWebview =
    | StoryGraphSchema
    | StoryGraphGraph
    | StoryGraphSimState
    | StoryGraphSimError
    | StoryGraphSimChanged
    | StoryGraphEnterMode
    | StoryGraphCenterNode
    | StoryGraphDiagnostics
    | StoryGraphInvalidate
    | StoryGraphWorkspaceSettings
    | StoryGraphAvailableModes
    | StoryGraphTypeNames
    | StoryGraphConfirmStageResult
    | StoryGraphSaveResult
    | StoryGraphDirtyExitChoice
    | StoryGraphError;

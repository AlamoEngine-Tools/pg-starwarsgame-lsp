// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Every request the client sends, with what it takes and what it answers (#324).
//
// The params and results were declared here all along, and a caller still passed a literal and
// named the result by hand: a field renamed on the server compiled on, and ten params types had no
// reader at all. A request is now made by its method, and the method decides both types - a wrong
// field, a missing one, or a method the server does not have is a compile error at the call.
//
// Mirrors the `[Method(...)]` records of PG.StarWarsGame.LSP.Server; each entry's params are the
// record carrying the attribute, its result the record's `IRequest<T>`.

import type { LaunchLayersResult } from "../luaDebug/launchLayers";
import type {
    ApplyEditBatchResult,
    EditBatchParams,
    ValidateEditBatchResult,
} from "./editBatch";
import type {
    GetEncyclopediaEntryParams,
    GetEncyclopediaEntryResult,
} from "./encyclopedia";
import type {
    ConvertLocalisationFormatResult,
    CreateLocalisationLanguageFileResult,
    ExportLocalisationToDatResult,
    GetBaselineEntriesResult,
    GetLanguagesResult,
    GetLocalisationProjectsResult,
    GetLocalisationRowsResult,
    GetRootLocalisationConfigResult,
    SetLocalisationProjectFormatResult,
} from "./localisation";
import type {
    GetSymbolNodeParams,
    GetSymbolNodeResult,
    GetSymbolOutlineParams,
    GetSymbolOutlineResult,
    GetSymbolPageParams,
    GetSymbolPageResult,
    GetSymbolPreviewResult,
} from "./modBrowser";
import type {
    GetModelDetailParams,
    GetModelDetailResult,
    GetModelGlbParams,
    GetModelGlbResult,
    GetModelTextureParams,
    GetModelTextureResult,
    GetParticleSystemParams,
    GetParticleSystemResult,
    GetPreviewSceneParams,
    GetPreviewSceneResult,
    GetProjectileResult,
    GetShaderSourceParams,
    GetShaderSourceResult,
    GetSubMeshGeometryParams,
    GetSubMeshGeometryResult,
    ListModelsResult,
} from "./modelPreview";
import type { ServerStatus } from "./serverStatus";
import type {
    ExecuteStoryCommandResult,
    GetStoryGraphResult,
    GetStoryLayoutResult,
    GetStoryParamOptionsResult,
    GetStoryPlotsResult,
    GetStorySchemaResult,
    GraphFilters,
    ResolveStoryReferenceResult,
    StoryLayoutEntryDto,
    StorySimStateResult,
} from "./story";
import type {
    AdoptModProjectParams,
    AdoptModProjectResult,
    GetEffectiveObjectResult,
    ResolveReferenceParams,
    ResolveReferenceResult,
    SetWorkspaceSettingsParams,
    WorkspaceSettingsDto,
} from "./workspace";

/** A request that takes nothing. */
export type NoParams = Record<string, never>;

/** The campaign faction a story request is about. */
export interface StoryChainParams {
    campaign: string;
    faction: string;
}

/** A story request about one graph: the chain, or one battle of it when `scope` names it. */
export interface StoryScopeParams extends StoryChainParams {
    scope?: string | null;
}

/**
 * The simulator's requests, one per action. Every one takes the scope plus the action's own
 * fields, which the story graph's page fills in, and answers the session's state.
 */
export type StorySimMethod =
    | "aet/storySimStart"
    | "aet/storySimStop"
    | "aet/storySimGetState"
    | "aet/storySimSatisfyTrigger"
    | "aet/storySimSetFlag"
    | "aet/storySimAdvanceClock"
    | "aet/storySimRuleOut"
    | "aet/storySimLuaNotify"
    | "aet/storySimTick"
    | "aet/storySimRunToDecision"
    | "aet/storySimSeek"
    | "aet/storySimWorld"
    | "aet/storySimBreakpoints"
    | "aet/storySimOptions"
    | "aet/storySimResolveBattle"
    | "aet/storySimRetryBattle";

interface Request<TParams, TResult> {
    params: TParams;
    result: TResult;
}

interface NamedRequests {
    // ── Project ──────────────────────────────────────────────────────────────
    "aet/adoptModProject": Request<
        AdoptModProjectParams,
        AdoptModProjectResult
    >;
    "aet/getServerStatus": Request<{ extended?: boolean }, ServerStatus>;
    "aet/getWatchDirectories": Request<
        NoParams,
        { directories?: readonly string[] | null }
    >;
    "aet/getWorkspaceSettings": Request<NoParams, WorkspaceSettingsDto>;
    "aet/setWorkspaceSettings": Request<SetWorkspaceSettingsParams, unknown>;
    "aet/getEffectiveObject": Request<
        { objectId: string; typeName?: string | null },
        GetEffectiveObjectResult
    >;
    "aet/resolveReference": Request<
        ResolveReferenceParams,
        ResolveReferenceResult
    >;
    "aet/getLaunchLayers": Request<NoParams, LaunchLayersResult>;

    // ── Edit batches ─────────────────────────────────────────────────────────
    "aet/validateEditBatch": Request<
        EditBatchParams<unknown>,
        ValidateEditBatchResult
    >;
    "aet/applyEditBatch": Request<
        EditBatchParams<unknown>,
        ApplyEditBatchResult
    >;

    // ── Localisation ─────────────────────────────────────────────────────────
    "aet/getLocalisationProjects": Request<
        NoParams,
        GetLocalisationProjectsResult
    >;
    "aet/getLocalisationRows": Request<
        { projectFilePath: string },
        GetLocalisationRowsResult
    >;
    "aet/getBaselineEntries": Request<
        { projectFilePath?: string | null },
        GetBaselineEntriesResult
    >;
    "aet/getLanguages": Request<NoParams, GetLanguagesResult>;
    "aet/getRootLocalisationConfig": Request<
        NoParams,
        GetRootLocalisationConfigResult
    >;
    "aet/convertLocalisationFormat": Request<
        { projectFilePath: string; targetFormat: string },
        ConvertLocalisationFormatResult
    >;
    "aet/setLocalisationProjectFormat": Request<
        { format: string },
        SetLocalisationProjectFormatResult
    >;
    "aet/exportLocalisationToDat": Request<
        { projectFilePath: string },
        ExportLocalisationToDatResult
    >;
    "aet/createLocalisationLanguageFile": Request<
        { projectFilePath: string; language: string },
        CreateLocalisationLanguageFileResult
    >;

    // ── Mod Browser ──────────────────────────────────────────────────────────
    "aet/getSymbolOutline": Request<
        GetSymbolOutlineParams,
        GetSymbolOutlineResult
    >;
    "aet/getSymbolPage": Request<GetSymbolPageParams, GetSymbolPageResult>;
    "aet/getSymbolNode": Request<GetSymbolNodeParams, GetSymbolNodeResult>;
    "aet/getSymbolPreview": Request<
        { page: string; id: string },
        GetSymbolPreviewResult
    >;
    "aet/getEncyclopediaEntry": Request<
        GetEncyclopediaEntryParams,
        GetEncyclopediaEntryResult
    >;

    // ── Model preview ────────────────────────────────────────────────────────
    "aet/listModels": Request<NoParams, ListModelsResult>;
    "aet/getPreviewScene": Request<
        GetPreviewSceneParams,
        GetPreviewSceneResult
    >;
    "aet/getModelGlb": Request<GetModelGlbParams, GetModelGlbResult>;
    "aet/getModelDetail": Request<GetModelDetailParams, GetModelDetailResult>;
    "aet/getSubMeshGeometry": Request<
        GetSubMeshGeometryParams,
        GetSubMeshGeometryResult
    >;
    "aet/getParticleSystem": Request<
        GetParticleSystemParams,
        GetParticleSystemResult
    >;
    "aet/getProjectile": Request<{ name: string }, GetProjectileResult>;
    "aet/getShaderSource": Request<
        GetShaderSourceParams,
        GetShaderSourceResult
    >;
    "aet/getModelTexture": Request<
        GetModelTextureParams,
        GetModelTextureResult
    >;
    "aet/clearPreviewCache": Request<NoParams, { removed: number }>;
    "aet/cancelPreviewCacheBuild": Request<NoParams, unknown>;

    // ── Story ────────────────────────────────────────────────────────────────
    "aet/getStoryPlots": Request<NoParams, GetStoryPlotsResult>;
    "aet/getStoryGraph": Request<
        StoryScopeParams & GraphFilters,
        GetStoryGraphResult
    >;
    "aet/previewStoryGraph": Request<
        StoryScopeParams & GraphFilters & { commands: readonly unknown[] },
        GetStoryGraphResult
    >;
    "aet/getStorySchema": Request<NoParams, GetStorySchemaResult>;
    "aet/getStoryParamOptions": Request<
        StoryChainParams & {
            side: string;
            typeName: string;
            position: number;
            query?: string | null;
            limit?: number | null;
        },
        GetStoryParamOptionsResult
    >;
    "aet/resolveStoryReference": Request<
        ResolveReferenceParams,
        ResolveStoryReferenceResult
    >;
    "aet/getStoryLayout": Request<StoryScopeParams, GetStoryLayoutResult>;
    "aet/setStoryLayout": Request<
        StoryChainParams & { entries: readonly StoryLayoutEntryDto[] },
        unknown
    >;
    /** The command's own fields travel beside the chain; its `kind` says which they are. */
    "aet/executeStoryCommand": Request<
        StoryChainParams & Record<string, unknown>,
        ExecuteStoryCommandResult
    >;

    // ── The language server protocol's own ───────────────────────────────────
    "workspace/executeCommand": Request<
        { command: string; arguments: readonly unknown[] },
        unknown
    >;
    "workspace/didChangeWatchedFiles": Request<
        { changes: readonly { uri: string; type: 1 | 2 | 3 }[] },
        unknown
    >;
}

/** Every request by its method. */
export type RequestMap = NamedRequests & {
    [M in StorySimMethod]: Request<
        StoryScopeParams & Record<string, unknown>,
        StorySimStateResult
    >;
};

export type RequestMethod = keyof RequestMap;

/** What a method takes. */
export type RequestParams<M extends RequestMethod> = RequestMap[M]["params"];

/** What a method answers. */
export type RequestResult<M extends RequestMethod> = RequestMap[M]["result"];

/** The methods that take nothing, which a caller may ask without naming params. */
export type ParamlessMethod = {
    [M in RequestMethod]: RequestParams<M> extends NoParams ? M : never;
}[RequestMethod];

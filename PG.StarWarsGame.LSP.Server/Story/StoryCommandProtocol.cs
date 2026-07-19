// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using MediatR;
using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace PG.StarWarsGame.LSP.Server.Story;

/// <summary>
///     The story editor's mutation envelope: one request, dispatched on <see cref="Kind" />.
///     Fields are a flat union - each kind reads the subset it needs and the handler validates
///     the rest. Kinds (Issue 7): node ops <c>createEvent</c>, <c>deleteEvent</c>,
///     <c>setEventType</c>, <c>setRewardType</c>, <c>clearEventType</c>, <c>clearRewardType</c>
///     (type + its params + Event_Filter, atomically - the immutable-type UI rule),
///     <c>setParams</c>, <c>setPerpetual</c>, <c>setDialog</c>; edge ops <c>addPrereq</c>, <c>addPrereqGroup</c>,
///     <c>addPrereqAlternatives</c>, <c>removePrereq</c>, <c>setBranch</c>,
///     <c>retargetControlEdge</c>; <c>renameEvent</c> (delegates to the cross-language rename);
///     manifest ops <c>createThread</c>, <c>deleteThread</c>, <c>setThreadState</c>,
///     <c>attachLuaScript</c>, <c>addPlotManifest</c>, <c>removePlotManifest</c>,
///     <c>createTacticalAttachment</c>.
/// </summary>
[Method("aet/executeStoryCommand", Direction.ClientToServer)]
public sealed record ExecuteStoryCommandParams(
    string Campaign,
    string Kind,
    string? ThreadUri = null,
    string? EventName = null,
    string? NewName = null,
    string? EventType = null,
    string? RewardType = null,
    string? Value = null,
    bool? Flag = null,
    int? GroupIndex = null,
    string? Token = null,
    // addPrereqGroup: every source token for the new AND-line, in one atomic edit.
    // addPrereqAlternatives: every source token, one new OR-line each, in one atomic edit.
    IReadOnlyList<string>? Tokens = null,
    IReadOnlyList<StoryParamValueEditDto>? Params = null,
    string? ParamKind = null,
    string? File = null,
    string? Faction = null,
    // createEvent only: initial event/reward param slots written into the new block.
    IReadOnlyList<StoryParamValueEditDto>? EventParams = null,
    IReadOnlyList<StoryParamValueEditDto>? RewardParams = null) : IRequest<ExecuteStoryCommandResult>;

/// <summary>One param-slot assignment; a null value removes the slot.</summary>
public sealed record StoryParamValueEditDto(int Position, string? Value);

public sealed record ExecuteStoryCommandResult(bool Success, string? Error = null);

/// <summary>
///     One command in a staged edit-mode batch: the same flat union as
///     <see cref="ExecuteStoryCommandParams" /> minus the campaign, which the batch supplies once.
/// </summary>
public sealed record StoryCommandDto(
    string Kind,
    string? ThreadUri = null,
    string? EventName = null,
    string? NewName = null,
    string? EventType = null,
    string? RewardType = null,
    string? Value = null,
    bool? Flag = null,
    int? GroupIndex = null,
    string? Token = null,
    IReadOnlyList<string>? Tokens = null,
    IReadOnlyList<StoryParamValueEditDto>? Params = null,
    string? ParamKind = null,
    string? File = null,
    string? Faction = null,
    IReadOnlyList<StoryParamValueEditDto>? EventParams = null,
    IReadOnlyList<StoryParamValueEditDto>? RewardParams = null)
{
    /// <summary>Rehydrates the full command envelope for the given campaign.</summary>
    public ExecuteStoryCommandParams ToParams(string campaign)
    {
        return new ExecuteStoryCommandParams(campaign, Kind, ThreadUri, EventName, NewName,
            EventType, RewardType, Value, Flag, GroupIndex, Token, Tokens, Params, ParamKind,
            File, Faction, EventParams, RewardParams);
    }
}

// ── aet/applyStoryCommandBatch - commit a staged edit-mode batch ──────────────

/// <summary>
///     Commits an edit-mode session: the client stages command envelopes locally (instant
///     optimistic feedback) and flushes the whole batch here on Save. The server composes them over
///     one in-memory working copy and, if every command validates, writes them as a single
///     <c>workspace/applyEdit</c>. The first command that fails aborts the batch - nothing is
///     written and <see cref="ApplyStoryCommandBatchResult.FailedIndex" /> names it.
/// </summary>
[Method("aet/applyStoryCommandBatch", Direction.ClientToServer)]
public sealed record ApplyStoryCommandBatchParams(
    string Campaign,
    IReadOnlyList<StoryCommandDto> Commands) : IRequest<ApplyStoryCommandBatchResult>;

/// <param name="FailedIndex">0-based index of the command that failed, when <c>Success</c> is false.</param>
public sealed record ApplyStoryCommandBatchResult(
    bool Success,
    int? FailedIndex = null,
    string? Error = null);

// ── aet/validateStoryCommandBatch - dry-run diagnostics for the staged batch ──

/// <summary>
///     Runs the on-demand Validate action: applies the staged batch to an in-memory working copy
///     (no file write) and returns diagnostics for that pending state, so problems reflect exactly
///     what the user has edited but not yet saved.
/// </summary>
[Method("aet/validateStoryCommandBatch", Direction.ClientToServer)]
public sealed record ValidateStoryCommandBatchParams(
    string Campaign,
    IReadOnlyList<StoryCommandDto> Commands) : IRequest<GetStoryDiagnosticsResult>;

// ── aet/previewStoryGraph - the graph as it would look with the staged batch applied ─────────────

/// <summary>
///     Builds the campaign graph as it would look after the staged batch - the commands are composed
///     onto an in-memory working copy (no file write) and the model is assembled from that. Lets the
///     webview show structural edits (create/delete/rename/edges) without writing to disk before Save
///     and without re-implementing the graph build client-side. Same filters and result shape as
///     <see cref="GetStoryGraphParams" />.
/// </summary>
[Method("aet/previewStoryGraph", Direction.ClientToServer)]
public sealed record PreviewStoryGraphParams(
    string Campaign,
    IReadOnlyList<StoryCommandDto> Commands,
    string? NameFilter = null,
    string? Branch = null,
    string? Lifecycle = null,
    string? ReachableFrom = null) : IRequest<GetStoryGraphResult>;

/// <summary>
///     Sends <c>workspace/applyEdit</c> to the client. A seam so command handlers are testable
///     without a live JSON-RPC connection; production wiring calls through the server facade.
/// </summary>
public interface IWorkspaceEditApplier
{
    Task<bool> ApplyAsync(WorkspaceEdit edit, string label, CancellationToken ct);
}
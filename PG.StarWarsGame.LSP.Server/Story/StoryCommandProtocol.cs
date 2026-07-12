// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using MediatR;
using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace PG.StarWarsGame.LSP.Server.Story;

/// <summary>
///     The story editor's mutation envelope: one request, dispatched on <see cref="Kind" />.
///     Fields are a flat union — each kind reads the subset it needs and the handler validates
///     the rest. Kinds (Issue 7): node ops <c>createEvent</c>, <c>deleteEvent</c>,
///     <c>setEventType</c>, <c>setRewardType</c>, <c>setParams</c>, <c>setPerpetual</c>,
///     <c>setDialog</c>; edge ops <c>addPrereq</c>, <c>removePrereq</c>, <c>setBranch</c>,
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
    IReadOnlyList<StoryParamValueEditDto>? Params = null,
    string? ParamKind = null,
    string? File = null,
    string? Faction = null) : IRequest<ExecuteStoryCommandResult>;

/// <summary>One param-slot assignment; a null value removes the slot.</summary>
public sealed record StoryParamValueEditDto(int Position, string? Value);

public sealed record ExecuteStoryCommandResult(bool Success, string? Error = null);

/// <summary>
///     Sends <c>workspace/applyEdit</c> to the client. A seam so command handlers are testable
///     without a live JSON-RPC connection; production wiring calls through the server facade.
/// </summary>
public interface IWorkspaceEditApplier
{
    Task<bool> ApplyAsync(WorkspaceEdit edit, string label, CancellationToken ct);
}

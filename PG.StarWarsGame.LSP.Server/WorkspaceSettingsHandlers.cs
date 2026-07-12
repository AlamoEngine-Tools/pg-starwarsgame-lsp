// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using MediatR;
using OmniSharp.Extensions.JsonRpc;

namespace PG.StarWarsGame.LSP.Server;

// ── aet/getWorkspaceSettings / aet/setWorkspaceSettings ──────────────────────

[Method("aet/getWorkspaceSettings", Direction.ClientToServer)]
public sealed record GetWorkspaceSettingsParams : IRequest<WorkspaceSettingsDto>;

/// <summary>Partial update — only the non-null fields are applied (the rest keep their value).</summary>
[Method("aet/setWorkspaceSettings", Direction.ClientToServer)]
public sealed record SetWorkspaceSettingsParams(
    bool? SkipStoryDeleteConfirmation = null,
    bool? ShowThreadLanes = null,
    bool? ShowChapterLanes = null) : IRequest<WorkspaceSettingsDto>;

public sealed record WorkspaceSettingsDto(
    bool SkipStoryDeleteConfirmation, bool ShowThreadLanes, bool ShowChapterLanes);

public sealed class GetWorkspaceSettingsHandler(IWorkspaceSettingsStore store)
    : IJsonRpcRequestHandler<GetWorkspaceSettingsParams, WorkspaceSettingsDto>
{
    public Task<WorkspaceSettingsDto> Handle(GetWorkspaceSettingsParams request, CancellationToken ct)
    {
        var s = store.Get();
        return Task.FromResult(new WorkspaceSettingsDto(
            s.SkipStoryDeleteConfirmation, s.ShowThreadLanes, s.ShowChapterLanes));
    }
}

public sealed class SetWorkspaceSettingsHandler(IWorkspaceSettingsStore store)
    : IJsonRpcRequestHandler<SetWorkspaceSettingsParams, WorkspaceSettingsDto>
{
    public Task<WorkspaceSettingsDto> Handle(SetWorkspaceSettingsParams request, CancellationToken ct)
    {
        var current = store.Get();
        var updated = current with
        {
            SkipStoryDeleteConfirmation = request.SkipStoryDeleteConfirmation ?? current.SkipStoryDeleteConfirmation,
            ShowThreadLanes = request.ShowThreadLanes ?? current.ShowThreadLanes,
            ShowChapterLanes = request.ShowChapterLanes ?? current.ShowChapterLanes,
        };
        store.Set(updated);
        return Task.FromResult(new WorkspaceSettingsDto(
            updated.SkipStoryDeleteConfirmation, updated.ShowThreadLanes, updated.ShowChapterLanes));
    }
}

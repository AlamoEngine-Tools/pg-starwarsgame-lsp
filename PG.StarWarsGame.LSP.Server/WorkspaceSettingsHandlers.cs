// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using MediatR;
using OmniSharp.Extensions.JsonRpc;

namespace PG.StarWarsGame.LSP.Server;

// ── aet/getWorkspaceSettings / aet/setWorkspaceSettings ──────────────────────
//
// Settings are per project, not per window: they persist in each project's own
// .aetswg/settings/workspace.settings.json. A multi-root workspace therefore has one set per open
// mod, and the request has to say which one it means - hence ContextUri. Without it a preference
// toggled while working in one mod would be written into whichever project loaded first.

/// <param name="ContextUri">
///     Any file in the project being asked about - normally the active editor.s document.
/// </param>
/// <param name="Campaign">
///     Alternative discriminator for callers that have a campaign rather than a file (the story
///     graph panel). Campaign names are a single global namespace in the engine, so a campaign
///     identifies exactly one project. Tried before <paramref name="ContextUri" />.
/// </param>
[Method("aet/getWorkspaceSettings", Direction.ClientToServer)]
public sealed record GetWorkspaceSettingsParams(string? ContextUri = null, string? Campaign = null)
    : IRequest<WorkspaceSettingsDto>;

/// <summary>Partial update - only the non-null fields are applied (the rest keep their value).</summary>
/// <param name="ContextUri">See <see cref="GetWorkspaceSettingsParams.ContextUri" />.</param>
[Method("aet/setWorkspaceSettings", Direction.ClientToServer)]
public sealed record SetWorkspaceSettingsParams(
    bool? SkipStoryDeleteConfirmation = null,
    bool? ShowThreadLanes = null,
    bool? ShowChapterLanes = null,
    string? ContextUri = null,
    string? Campaign = null) : IRequest<WorkspaceSettingsDto>;

public sealed record WorkspaceSettingsDto(
    bool SkipStoryDeleteConfirmation,
    bool ShowThreadLanes,
    bool ShowChapterLanes);

public sealed class GetWorkspaceSettingsHandler(IProjectScopedWorkspaceSettings store)
    : IJsonRpcRequestHandler<GetWorkspaceSettingsParams, WorkspaceSettingsDto>
{
    public Task<WorkspaceSettingsDto> Handle(GetWorkspaceSettingsParams request, CancellationToken ct)
    {
        var s = store.For(request.ContextUri, request.Campaign).Get();
        return Task.FromResult(new WorkspaceSettingsDto(
            s.SkipStoryDeleteConfirmation, s.ShowThreadLanes, s.ShowChapterLanes));
    }
}

public sealed class SetWorkspaceSettingsHandler(IProjectScopedWorkspaceSettings store)
    : IJsonRpcRequestHandler<SetWorkspaceSettingsParams, WorkspaceSettingsDto>
{
    public Task<WorkspaceSettingsDto> Handle(SetWorkspaceSettingsParams request, CancellationToken ct)
    {
        // Read and write the SAME project's store, so a partial update cannot merge one project's
        // current values into another project's file.
        var target = store.For(request.ContextUri, request.Campaign);

        var current = target.Get();
        var updated = current with
        {
            SkipStoryDeleteConfirmation = request.SkipStoryDeleteConfirmation ?? current.SkipStoryDeleteConfirmation,
            ShowThreadLanes = request.ShowThreadLanes ?? current.ShowThreadLanes,
            ShowChapterLanes = request.ShowChapterLanes ?? current.ShowChapterLanes
        };
        target.Set(updated);
        return Task.FromResult(new WorkspaceSettingsDto(
            updated.SkipStoryDeleteConfirmation, updated.ShowThreadLanes, updated.ShowChapterLanes));
    }
}

/// <summary>
///     Resolves the settings store of the project a request is about. Separate from
///     <see cref="IWorkspaceSettingsStore" /> so the per-project stores keep their simple,
///     project-less shape and only the protocol edge has to know about routing.
/// </summary>
public interface IProjectScopedWorkspaceSettings
{
    IWorkspaceSettingsStore For(string? contextUri, string? campaign = null);
}

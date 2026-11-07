// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Project;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Server.Project;

namespace PG.StarWarsGame.LSP.Server.Tests;

/// <summary>A loaded project that targets <paramref name="game" /> and does nothing else.</summary>
internal sealed class ProjectOfGame(TargetGame game = TargetGame.FOC) : IModProjectReloadService
{
    public IReadOnlyList<string>? LastAssetRoots => null;
    public WorkspaceConfiguration LastWorkspaceConfig { get; } = WorkspaceConfiguration.Empty with { Game = game };
    public IReadOnlyList<string>? LastWorkspaceRoots => null;

    public Task LoadAsync(IEnumerable<string> workspaceRoots, CancellationToken ct)
    {
        return Task.CompletedTask;
    }

    public Task ReloadAsync(CancellationToken ct)
    {
        return Task.CompletedTask;
    }

    public Task ReloadLocalisationAsync(CancellationToken ct)
    {
        return Task.CompletedTask;
    }
}

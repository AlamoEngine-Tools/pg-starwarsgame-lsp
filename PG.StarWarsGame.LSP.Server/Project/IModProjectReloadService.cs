// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Workspace;

namespace PG.StarWarsGame.LSP.Server.Project;

public interface IModProjectReloadService
{
    // The asset roots resolved by the most recent successful load. Consumed by the watched-files
    // handler to re-glob loose asset files when one changes on disk. Null until the first load.
    IReadOnlyList<string>? LastAssetRoots { get; }

    // The workspace configuration resolved by the most recent successful load. Null until the
    // first successful load that finds a .pgproj.
    WorkspaceConfiguration? LastWorkspaceConfig { get; }

    // The workspace roots passed to the most recent LoadAsync call. Null until the first load.
    IReadOnlyList<string>? LastWorkspaceRoots { get; }

    // Initial load - called from the startup pipeline with the workspace roots from the LSP
    // initialize request. Resolves the .pgproj and indexes the declared directories. A no-op
    // (logged) when no .pgproj is found, since the project file is the only directory source.
    Task LoadAsync(IEnumerable<string> workspaceRoots, CancellationToken ct);

    // Re-load using the workspace roots from the last LoadAsync call. Called on .pgproj file change.
    Task ReloadAsync(CancellationToken ct);

    // Re-runs only the localisation load against the last resolved WorkspaceConfiguration - skips
    // the full XML/Lua/asset/bone/enum rescan. For frequent, localisation-only triggers (grid
    // edits, watched text-file changes) where a full ReloadAsync would be needlessly expensive.
    // No-op (logged) if LoadAsync hasn't run yet.
    Task ReloadLocalisationAsync(CancellationToken ct);
}
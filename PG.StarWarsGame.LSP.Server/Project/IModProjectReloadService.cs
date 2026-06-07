// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Server.Project;

public interface IModProjectReloadService
{
    // Initial load — called from OnInitialized with the workspace roots from the LSP initialize request.
    Task LoadAsync(IEnumerable<string> workspaceRoots, CancellationToken ct);

    // Re-load using the workspace roots from the last LoadAsync call. Called on .pgproj file change.
    Task ReloadAsync(CancellationToken ct);
}

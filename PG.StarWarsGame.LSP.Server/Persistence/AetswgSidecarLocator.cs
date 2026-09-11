// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Caching;
using PG.StarWarsGame.LSP.Core.Persistence;
using PG.StarWarsGame.LSP.Server.Project;

namespace PG.StarWarsGame.LSP.Server.Persistence;

/// <summary>
///     Puts a sidecar under the root project's <c>.aetswg/</c> directory.
///     <para>
///         Every sidecar store used to carry its own copy of this - find the highest-ranked layer,
///         take its <c>.pgproj</c>, join the file name - and its own copy of what to do when there
///         is no project: return null, and let the store keep its value for the session only.
///     </para>
/// </summary>
/// <param name="fileName">
///     Relative to <c>.aetswg/</c>, so it may carry a subdirectory (<c>settings/...</c>).
/// </param>
public sealed class AetswgSidecarLocator(IModProjectReloadService reloadService) : ISidecarLocator
{
    public string? TryLocate(string fileName)
    {
        var rootLayer = reloadService.LastWorkspaceConfig?.Layers
            .OrderByDescending(l => l.Rank)
            .FirstOrDefault();
        if (rootLayer?.ProjectPath is not { } pgprojPath) return null;
        return ProjectIndexLocator.GetAetswgDirectory(pgprojPath) + "/" + fileName;
    }
}

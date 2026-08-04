// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Server.Project;

public interface IModProjectDetector
{
    /// <summary>
    ///     Every distinct <c>.pgproj</c> reachable from the given roots - one per root, since a root
    ///     containing more than one project is an error (see <see cref="ModProjectLoadException" />).
    ///     A project reachable from several overlapping roots is returned once.
    /// </summary>
    IReadOnlyList<string> FindAll(IEnumerable<string> workspaceRoots);

    /// <summary>
    ///     The first project found, for the single-project call sites. Prefer
    ///     <see cref="FindAll" /> - this discards every project but one in a multi-root workspace.
    /// </summary>
    bool TryFind(IEnumerable<string> workspaceRoots, out string? projectFilePath)
    {
        projectFilePath = FindAll(workspaceRoots).FirstOrDefault();
        return projectFilePath is not null;
    }
}

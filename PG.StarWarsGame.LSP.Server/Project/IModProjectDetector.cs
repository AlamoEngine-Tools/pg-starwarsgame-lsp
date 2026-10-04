// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Server.Project;

public interface IModProjectDetector
{
    /// <summary>The project file to serve under the first root that has one; the shallowest when there are several.</summary>
    bool TryFind(IEnumerable<string> workspaceRoots, out string? projectFilePath);

    /// <summary>
    ///     As <see cref="TryFind(IEnumerable{string}, out string?)" />, and also the project files
    ///     under the same root that were NOT chosen - a workspace holding a mod and the library it
    ///     extends has two, and the user should hear that one of them was not loaded.
    /// </summary>
    bool TryFind(IEnumerable<string> workspaceRoots, out string? projectFilePath,
        out IReadOnlyList<string> otherProjectFiles);
}
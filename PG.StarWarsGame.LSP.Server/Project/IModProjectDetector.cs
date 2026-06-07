// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Server.Project;

public interface IModProjectDetector
{
    bool TryFind(IEnumerable<string> workspaceRoots, out string? projectFilePath);
}

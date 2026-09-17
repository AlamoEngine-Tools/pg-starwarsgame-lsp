// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Lua.Debug.Sources;

public interface IScriptSourceMapFactory
{
    /// <param name="sourceRoots">Script root directories in priority order; each is a path, not a URI.</param>
    IScriptSourceMap Create(IReadOnlyList<string> sourceRoots);
}

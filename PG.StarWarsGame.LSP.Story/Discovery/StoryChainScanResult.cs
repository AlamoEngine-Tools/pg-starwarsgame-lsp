// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Story.Discovery;

/// <summary>
///     Everything the campaign story chain reaches: plot manifest files (to be typed
///     <c>StoryPlotManifest</c>), story thread files (to be typed <c>StoryParser</c>), attached
///     Lua script names (extensionless, as written), and the problems found on the way. File
///     lists are xml-directory-relative paths, de-duplicated case-insensitively.
/// </summary>
public sealed record StoryChainScanResult(
    IReadOnlyList<string> ManifestFiles,
    IReadOnlyList<string> ThreadFiles,
    IReadOnlyList<string> LuaScripts,
    IReadOnlyList<StoryChainProblem> Problems)
{
    public static readonly StoryChainScanResult Empty = new([], [], [], []);
}

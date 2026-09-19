// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Lua.Debug.Sources;

/// <summary>
///     Maps between the paths the game reports for its scripts and the workspace files behind
///     them. Workspace files are named by their normalized document URI, the same key the index
///     uses, and compared the way the index compares them; a resolved URI carries the spelling the
///     file has on disk, so it opens on a case-sensitive host too.
/// </summary>
public interface IScriptSourceMap
{
    /// <summary>The script roots searched, in priority order: the mod first, its dependencies and the game after.</summary>
    IReadOnlyList<string> Roots { get; }

    /// <summary>
    ///     The document URI for a game-reported path, or null when no root holds it. Matching is
    ///     case-insensitive and separator-agnostic and tries the path's <c>Data/Scripts/...</c>
    ///     tail before its bare file name. The spelling the game used is remembered for
    ///     <see cref="ToGamePath" />.
    /// </summary>
    string? ResolveToDocumentUri(string gamePath);

    /// <summary>
    ///     The spelling to send the game for a workspace file: the one the game itself reported
    ///     for that file when it has, else the path under its root in the game's own
    ///     <c>Data\Scripts\...</c> form. The game compares breakpoint sources case-insensitively,
    ///     so the derived form is a best effort until the live run has measured the wire spelling.
    /// </summary>
    string? ToGamePath(string documentUri);
}
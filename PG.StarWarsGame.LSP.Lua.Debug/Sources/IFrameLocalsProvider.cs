// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Lua.Analysis;

namespace PG.StarWarsGame.LSP.Lua.Debug.Sources;

/// <summary>
///     The names a frame's Locals scope should ask the game for. The game never sends local
///     names, but it resolves a name against the selected frame's locals before the globals, so a
///     static parse of the source at the frame's line supplies the list.
/// </summary>
public interface IFrameLocalsProvider
{
    /// <param name="text">The source text of the frame's file.</param>
    /// <param name="line">The frame's one-based current line.</param>
    /// <returns>Parameters and locals in scope at the start of that line, each name once, innermost last.</returns>
    IReadOnlyList<LuaFrameLocal> LocalsAt(string text, int line);
}

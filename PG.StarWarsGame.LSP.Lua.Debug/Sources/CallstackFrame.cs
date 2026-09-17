// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Lua.Debug.Sources;

/// <summary>
///     One frame of a suspended script's call stack.
/// </summary>
/// <param name="Level">The frame's index in the order the game sent the stack, which is what frame selection expects.</param>
/// <param name="Source">The chunk name the game reports for the frame's source, as spelled on the wire.</param>
/// <param name="Line">The one-based current line.</param>
/// <param name="What">The Lua "what" of the function: main, Lua, C or tail.</param>
/// <param name="NameWhat">How the function was reached: global, local, method, field, or empty.</param>
/// <param name="Name">The function's name where the game knows one, else empty.</param>
/// <param name="Raw">The wire entry as received.</param>
public sealed record CallstackFrame(
    int Level,
    string Source,
    int Line,
    string What,
    string NameWhat,
    string Name,
    string Raw)
{
    /// <summary>A display name: the function name, or its kind when it has none.</summary>
    public string DisplayName => Name.Length > 0 ? Name : What.Length > 0 ? $"<{What}>" : "<unknown>";
}
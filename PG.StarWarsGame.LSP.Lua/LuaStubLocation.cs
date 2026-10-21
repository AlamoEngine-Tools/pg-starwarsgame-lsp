// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Lua;

/// <summary>
///     The directory holding the engine's <c>---@meta</c> stub files on disk: the schema's own
///     <c>lua/</c> directory, or the cache a downloaded schema release is kept in. Set once the
///     schema is loaded; the Lua analyzer reads the stubs from there as a library.
/// </summary>
public sealed class LuaStubLocation
{
    public string? Directory { get; set; }

    /// <summary>The stub files the manifest lists, in load order, relative to <see cref="Directory" />.</summary>
    public IReadOnlyList<string> Files { get; set; } = [];

    /// <summary>
    ///     The stub standing in for each script library, by module name (case-insensitive). The other
    ///     files describe the engine.
    /// </summary>
    public IReadOnlyDictionary<string, string> Libraries { get; set; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}
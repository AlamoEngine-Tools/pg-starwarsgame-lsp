// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Testing;

/// <summary>
///     The cases of the game's texture lookup (#420), shared by the tests of every place that resolves a
///     texture name - the server's asset resolver and the XML checks - so they cannot drift apart.
/// </summary>
/// <remarks>
///     Measured in the game (FoC x64, 2024 release): the name is opened exactly as written, whatever its
///     extension; when that file does not exist, the name up to its FIRST <c>.</c> with <c>.dds</c> appended
///     is opened instead. Nothing else is tried. The file found is decoded by its content.
/// </remarks>
public static class TextureLookupCases
{
    /// <summary>The name written, the texture files that exist, and the one the game opens (null: none).</summary>
    public static IEnumerable<object?[]> All =>
    [
        // A PNG named as one is found.
        ["Hull.png", new[] { "Hull.png" }, "Hull.png"],
        // A missing .png falls back to the .dds.
        ["Hull.png", new[] { "Hull.dds" }, "Hull.dds"],
        // A missing .tga falls back to the .dds.
        ["Hull.tga", new[] { "Hull.dds" }, "Hull.dds"],
        // The fallback is the .dds and nothing else: a missing .tga does not reach a .png.
        ["Hull.tga", new[] { "Hull.png" }, null],
        // A missing .dds does not reach the .tga.
        ["Hull.dds", new[] { "Hull.tga" }, null],
        // The name is cut at its FIRST dot, so the .dds of the last-dot stem is never opened.
        ["Hull.two.tga", new[] { "Hull.dds", "Hull.two.dds" }, "Hull.dds"],
        ["Hull.two.tga", new[] { "Hull.two.dds" }, null],
        // The name as written comes first, whatever else exists.
        ["Hull.tga", new[] { "Hull.tga", "Hull.dds" }, "Hull.tga"],
        ["Hull.dds", new[] { "Hull.dds", "Hull.tga" }, "Hull.dds"],
        // Case does not matter.
        ["HULL.TGA", new[] { "hull.dds" }, "hull.dds"]
    ];
}
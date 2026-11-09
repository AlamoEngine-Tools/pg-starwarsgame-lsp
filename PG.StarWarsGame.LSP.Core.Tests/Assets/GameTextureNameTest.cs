// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Assets;

namespace PG.StarWarsGame.LSP.Core.Tests.Assets;

/// <summary>
///     How the game turns a texture name into a file (#292). MEASURED on the 2024 64-bit build's
///     texture loader: the name is tried as written, then the part after its LAST BACKSLASH is tried
///     under each texture directory; when nothing is found the same is repeated with <c>.dds</c> in
///     place of the extension. The shipped art names textures by the art machine's absolute path in 37
///     particle files per game, three names in all, and they render in game because of this.
/// </summary>
public sealed class GameTextureNameTest
{
    [Theory]
    [InlineData(@"C:\Projects\Starwars\Art\Textures\Game\p_particle_master.tga", "p_particle_master.tga")]
    [InlineData(@"C:\Art\Textures\Game\p_particle_depth_master.tga", "p_particle_depth_master.tga")]
    [InlineData("hull.tga", "hull.tga")]
    [InlineData("  hull.dds  ", "hull.dds")]
    public void FileName_IsThePartAfterTheLastBackslash(string named, string expected)
    {
        Assert.Equal(expected, GameTextureName.FileName(named));
    }

    // The loader cuts at a backslash only; a forward slash stays part of the name it looks for.
    [Fact]
    public void FileName_KeepsAForwardSlash_AsTheGameDoes()
    {
        Assert.Equal("Game/hull.tga", GameTextureName.FileName("Game/hull.tga"));
    }
}
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

    // #420, measured in the game (FoC x64, 2024 release): the name as written, then the name up to its FIRST
    // dot with .dds appended - and nothing else, whatever the extension.
    [Theory]
    [InlineData("hull.png", "hull.png|hull.dds")]
    [InlineData("hull.tga", "hull.tga|hull.dds")]
    [InlineData("hull.bmp", "hull.bmp|hull.dds")]
    [InlineData("hull.dds", "hull.dds")]
    [InlineData("hull.DDS", "hull.DDS")]
    [InlineData("hull.two.tga", "hull.two.tga|hull.dds")]
    [InlineData("hull.two.dds", "hull.two.dds|hull.dds")]
    [InlineData("Data/Art/Textures/hull.two.tga", "Data/Art/Textures/hull.two.tga|Data/Art/Textures/hull.dds")]
    [InlineData(@"Art.Old\hull.tga", @"Art.Old\hull.tga|Art.Old\hull.dds")]
    public void Candidates_AreTheNameAsWritten_ThenItsFirstDotStemAsDds(string named, string expected)
    {
        Assert.Equal(expected.Split('|'), GameTextureName.Candidates(named));
    }

    [Theory]
    [InlineData("hull.dds", true)]
    [InlineData("hull.TGA", true)]
    [InlineData("hull.png", true)]
    [InlineData("hull.alo", false)]
    [InlineData("hull.bmp", false)]
    [InlineData("hull", false)]
    public void IsTextureFile_KnowsTheFormatsTheCatalogRecords(string name, bool expected)
    {
        Assert.Equal(expected, GameTextureName.IsTextureFile(name));
    }

    [Theory]
    [InlineData("hull.png", true)]
    [InlineData("data/art/textures/HULL.PNG", true)]
    [InlineData("hull.tga", false)]
    [InlineData("hull.png.dds", false)]
    public void IsPng_IsByTheFileFound(string path, bool expected)
    {
        Assert.Equal(expected, GameTextureName.IsPng(path));
    }

    // FR-5: a .dds name whose file is missing is NOT satisfied by the .tga of that stem; this is the file
    // a hint names.
    [Theory]
    [InlineData("hull.dds", "hull.tga")]
    [InlineData("Data/Art/Textures/Hull.DDS", "Data/Art/Textures/Hull.tga")]
    [InlineData("hull.tga", null)]
    [InlineData("hull.png", null)]
    public void UnsearchedTga_IsTheTgaADdsNameNeverReaches(string named, string? expected)
    {
        Assert.Equal(expected, GameTextureName.UnsearchedTga(named));
    }
}
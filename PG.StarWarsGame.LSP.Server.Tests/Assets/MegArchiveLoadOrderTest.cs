// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions.TestingHelpers;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Server.Assets;

namespace PG.StarWarsGame.LSP.Server.Tests.Assets;

/// <summary>
///     Which archives the game loads and the order it searches them in (#219, #220). Measured: only
///     <c>Data\MegaFiles.xml</c> and the fixed names <c>Data\Patch.meg</c>, <c>Data\patch2.meg</c>,
///     <c>Data\64Patch.meg</c> are read, there is no directory glob; the last archive registered is
///     searched first; a mod's own list replaces the game's; a name under <c>Data\</c> is looked for
///     in the mod folders first, then the game folder, then the base game folder; the base game's own
///     list and archives are read from the base game folder only and searched after the expansion's.
/// </summary>
public sealed class MegArchiveLoadOrderTest
{
    private const string Game = @"C:\game\corruption";
    private const string BaseGame = @"C:\game\GameData";
    private const string Mod = @"C:\mods\leaf";
    private const string Dependency = @"C:\mods\dependency";

    private static string List(params string[] entries)
    {
        return "<?xml version=\"1.0\" ?>\n<Mega_Files>\n" +
               string.Concat(entries.Select(e => $"\t<File> {e} </File>\n")) + "</Mega_Files>";
    }

    private static IReadOnlyList<MegArchive> Order(MockFileSystem fs, string[] modRoots, string? game,
        string? baseGame = null)
    {
        return new MegArchiveLoadOrder(new FileHelper(fs))
            .SearchOrder(new MegArchiveRoots(modRoots, game, baseGame));
    }

    private static string[] Paths(IReadOnlyList<MegArchive> archives)
    {
        return [.. archives.Select(a => a.Path.ToLowerInvariant())];
    }

    [Fact]
    public void ListedArchives_AreSearchedLastLineFirst_ThePatchArchivesBeforeThem()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [$@"{Game}\Data\MegaFiles.xml"] = new(List(@"Data\Models.meg", @"Data\Textures.meg")),
            [$@"{Game}\Data\Models.meg"] = new(""),
            [$@"{Game}\Data\Textures.meg"] = new(""),
            [$@"{Game}\Data\Patch.meg"] = new(""),
            [$@"{Game}\Data\Patch2.meg"] = new(""),
            [$@"{Game}\Data\64Patch.meg"] = new("")
        });

        var order = Order(fs, [], Game);

        Assert.Equal(
        [
            Lower($@"{Game}\Data\64Patch.meg"), Lower($@"{Game}\Data\Patch2.meg"),
            Lower($@"{Game}\Data\Patch.meg"), Lower($@"{Game}\Data\Textures.meg"),
            Lower($@"{Game}\Data\Models.meg")
        ], Paths(order).Select(Lower));
        Assert.All(order, a => Assert.Equal(MegArchiveSource.Game, a.Source));
    }

    [Fact]
    public void AnArchiveNoListNames_IsNotLoaded()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [$@"{Game}\Data\MegaFiles.xml"] = new(List(@"Data\Models.meg")),
            [$@"{Game}\Data\Models.meg"] = new(""),
            [$@"{Mod}\Data\Unlisted.meg"] = new("")
        });

        var order = Order(fs, [Mod], Game);

        Assert.Equal([Lower($@"{Game}\Data\Models.meg")], Paths(order));
    }

    [Fact]
    public void AModsOwnList_ReplacesTheGamesList()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [$@"{Game}\Data\MegaFiles.xml"] = new(List(@"Data\Models.meg")),
            [$@"{Game}\Data\Models.meg"] = new(""),
            [$@"{Mod}\Data\MegaFiles.xml"] = new(List(@"Data\ModTextures.meg")),
            [$@"{Mod}\Data\ModTextures.meg"] = new("")
        });

        var order = Order(fs, [Mod], Game);

        var only = Assert.Single(order);
        Assert.Equal(Lower($@"{Mod}\Data\ModTextures.meg"), Lower(only.Path));
        Assert.Equal(MegArchiveSource.Mod, only.Source);
    }

    [Fact]
    public void WithNoListOfItsOwn_AModsArchiveOfAListedName_ReplacesTheGames()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [$@"{Game}\Data\MegaFiles.xml"] = new(List(@"Data\Models.meg")),
            [$@"{Game}\Data\Models.meg"] = new(""),
            [$@"{Mod}\Data\models.meg"] = new("")
        });

        var only = Assert.Single(Order(fs, [Mod], Game));

        Assert.Equal(Lower($@"{Mod}\Data\models.meg"), Lower(only.Path));
    }

    [Fact]
    public void TheFirstListAcrossTheLayers_LeafFirst_IsTheOneRead()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [$@"{Game}\Data\MegaFiles.xml"] = new(List(@"Data\Models.meg")),
            [$@"{Dependency}\Data\MegaFiles.xml"] = new(List(@"Data\Shared.meg", @"Data\Leaf.meg")),
            [$@"{Dependency}\Data\Shared.meg"] = new(""),
            [$@"{Dependency}\Data\Leaf.meg"] = new(""),
            [$@"{Mod}\Data\Leaf.meg"] = new("")
        });

        var order = Order(fs, [Mod, Dependency], Game);

        Assert.Equal([Lower($@"{Mod}\Data\Leaf.meg"), Lower($@"{Dependency}\Data\Shared.meg")], Paths(order));
    }

    [Fact]
    public void TheBaseGamesArchives_AreSearchedAfterTheExpansions_AndNeverResolveThroughAMod()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [$@"{Game}\Data\MegaFiles.xml"] = new(List(@"Data\Models.meg")),
            [$@"{Game}\Data\Models.meg"] = new(""),
            [$@"{BaseGame}\Data\MegaFiles.xml"] = new(List(@"Data\Models.meg")),
            [$@"{BaseGame}\Data\Models.meg"] = new(""),
            [$@"{BaseGame}\Data\Patch.meg"] = new(""),
            [$@"{Game}\Data\Patch.meg"] = new(""),
            [$@"{Mod}\Data\MegaFiles.xml"] = new(List(@"Data\Models.meg")),
            [$@"{Mod}\Data\Models.meg"] = new("")
        });

        var order = Order(fs, [Mod], Game, BaseGame);

        Assert.Equal(
        [
            Lower($@"{Game}\Data\Patch.meg"), Lower($@"{Mod}\Data\Models.meg"),
            Lower($@"{BaseGame}\Data\Patch.meg"), Lower($@"{BaseGame}\Data\Models.meg")
        ], Paths(order));
        Assert.Equal(
            [MegArchiveSource.Game, MegArchiveSource.Mod, MegArchiveSource.BaseGame, MegArchiveSource.BaseGame],
            order.Select(a => a.Source));
    }

    [Fact]
    public void AListedNameMissingFromTheGameFolder_ResolvesToTheBaseGameFolder()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [$@"{Game}\Data\MegaFiles.xml"] = new(List(@"Data\Movies.meg")),
            [$@"{BaseGame}\Data\Movies.meg"] = new("")
        });

        var only = Assert.Single(Order(fs, [], Game, BaseGame));

        Assert.Equal(Lower($@"{BaseGame}\Data\Movies.meg"), Lower(only.Path));
    }

    [Fact]
    public void TheSoundArchives_AreSearchedFirst_3DThenLocalisedThenNonLocalised()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [$@"{Game}\Data\MegaFiles.xml"] = new(List(@"Data\Models.meg")),
            [$@"{Game}\Data\Models.meg"] = new(""),
            [$@"{Game}\Data\Audio\SFX\sfx2d_non_localized.meg"] = new(""),
            [$@"{Game}\Data\Audio\SFX\sfx2d_english.meg"] = new(""),
            [$@"{Game}\Data\Audio\SFX\sfx2d_german.meg"] = new(""),
            [$@"{Game}\Data\Audio\SFX\sfx3d_non_localized.meg"] = new("")
        });

        var order = Order(fs, [], Game);

        Assert.Equal(
        [
            Lower($@"{Game}\Data\Audio\SFX\sfx3d_non_localized.meg"),
            Lower($@"{Game}\Data\Audio\SFX\sfx2d_english.meg"),
            Lower($@"{Game}\Data\Audio\SFX\sfx2d_non_localized.meg"),
            Lower($@"{Game}\Data\Models.meg")
        ], Paths(order));
    }

    [Fact]
    public void AListedArchiveThatDoesNotExist_IsDropped()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [$@"{Game}\Data\MegaFiles.xml"] = new(List(@"Data\Missing.meg", @"Data\Models.meg")),
            [$@"{Game}\Data\Models.meg"] = new("")
        });

        Assert.Equal([Lower($@"{Game}\Data\Models.meg")], Paths(Order(fs, [], Game)));
    }

    [Fact]
    public void AListedNameOutsideData_IsNotLookedForInAMod()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [$@"{Game}\Data\MegaFiles.xml"] = new(List("Extra.meg")),
            [$@"{Game}\Extra.meg"] = new(""),
            [$@"{Mod}\Extra.meg"] = new("")
        });

        var only = Assert.Single(Order(fs, [Mod], Game));

        Assert.Equal(Lower($@"{Game}\Extra.meg"), Lower(only.Path));
    }

    [Fact]
    public void WithNoGameFolderConfigured_TheModsListStillResolvesInTheMod()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [$@"{Mod}\Data\MegaFiles.xml"] = new(List(@"Data\Mod.meg")),
            [$@"{Mod}\Data\Mod.meg"] = new("")
        });

        var only = Assert.Single(Order(fs, [Mod], null));

        Assert.Equal(Lower($@"{Mod}\Data\Mod.meg"), Lower(only.Path));
    }

    private static string Lower(string path)
    {
        return path.ToLowerInvariant();
    }
}
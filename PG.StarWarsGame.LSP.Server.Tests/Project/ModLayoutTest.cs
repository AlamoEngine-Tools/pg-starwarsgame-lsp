// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions.TestingHelpers;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Server.Project;

namespace PG.StarWarsGame.LSP.Server.Tests.Project;

/// <summary>
///     What a folder is to the game, read once and in any case (#317): the one rule New Mod Project,
///     Adopt Project and the client's offer share. Every name is matched as the game matches it, and
///     every path comes back spelled as it is on disk.
/// </summary>
public sealed class ModLayoutTest
{
    private const string Root = "/mods/legacy";

    private static ModFolder Inspect(params string[] files)
    {
        var fs = new MockFileSystem();
        foreach (var file in files)
            fs.AddFile($"{Root}/{file}", new MockFileData(string.Empty));
        return new ModLayout(new FileHelper(fs)).Inspect(Root);
    }

    [Fact]
    public void AnOrdinaryMod_IsAMod_AndNotTheGame()
    {
        var folder = Inspect("Data/XML/GameObjectFiles.xml", "Data/Scripts/Library/lib.lua");

        Assert.True(folder.IsMod);
        Assert.True(folder.HoldsModData);
        Assert.False(folder.IsGame);
        Assert.Null(folder.ProjectFile);
        Assert.Null(folder.Modinfo);
        Assert.EndsWith("Data", folder.Data);
    }

    [Theory]
    [InlineData("swfoc.exe")]
    [InlineData("SWFOC.EXE")]
    [InlineData("StarWarsG.exe")]
    public void TheGamesFolder_IsRecognised_InAnyCase(string executable)
    {
        Assert.True(Inspect("Data/XML/a.xml", executable).IsGame);
    }

    [Theory]
    [InlineData("Data/xml/a.xml")]
    [InlineData("DATA/Scripts/a.lua")]
    [InlineData("Data/Art/Models/a.alo")]
    [InlineData("Data/Audio/a.wav")]
    [InlineData("Data/Text/a.csv")]
    [InlineData("Data/Config.MEG")]
    public void ADataFolderWithAGameFolderOrArchive_HoldsModData(string file)
    {
        Assert.True(Inspect(file).HoldsModData);
    }

    [Fact]
    public void ADataFolderWithoutTheGamesFolders_HoldsNoModData()
    {
        var folder = Inspect("Data/Maps/a.ted", "Data/readme.txt");

        Assert.False(folder.HoldsModData);
        Assert.False(folder.IsMod);
    }

    [Theory]
    [InlineData("modinfo.json")]
    [InlineData("MODINFO.JSON")]
    [InlineData("ModInfo.json")]
    public void Modinfo_IsFoundInAnyCase_SpelledAsOnDisk(string spelling)
    {
        var folder = Inspect(spelling);

        Assert.NotNull(folder.Modinfo);
        Assert.EndsWith(spelling, folder.Modinfo);
        Assert.True(folder.IsMod);
    }

    [Theory]
    [InlineData("Other.pgproj")]
    [InlineData("legacy.PGPROJ")]
    public void AnyProjectFile_IsFound_WhateverItsName(string projectFile)
    {
        var folder = Inspect("Data/XML/a.xml", projectFile);

        Assert.NotNull(folder.ProjectFile);
        Assert.EndsWith(projectFile, folder.ProjectFile);
    }

    [Fact]
    public void AFolderThatDoesNotExist_IsNothing()
    {
        var folder = new ModLayout(new FileHelper(new MockFileSystem())).Inspect(Root);

        Assert.False(folder.IsMod);
        Assert.False(folder.IsGame);
        Assert.Null(folder.ProjectFile);
        Assert.Null(folder.Data);
        Assert.Null(folder.Modinfo);
    }

    [Fact]
    public void ADataChild_IsFoundInAnyCase_SpelledAsOnDisk()
    {
        var folder = Inspect("data/Scripts/a.lua");

        Assert.EndsWith("Scripts", folder.DataChild("scripts"));
        Assert.Null(folder.DataChild("Art"));
    }
}
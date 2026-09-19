// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.DependencyInjection;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Lua.Debug.Sources;

namespace PG.StarWarsGame.LSP.Lua.Debug.Tests.Sources;

public sealed class ScriptSourceMapTest
{
    private const string ModRoot = @"D:\Mods\MyMod\Data\Scripts";
    private const string GameRoot = @"D:\Games\FoC\Data\Scripts";

    private readonly MockFileSystem _fs = new();
    private readonly IScriptSourceMapFactory _factory;

    public ScriptSourceMapTest()
    {
        _fs.AddFile(@"D:\Mods\MyMod\Data\Scripts\GameObject\Hero.lua", new MockFileData("-- hero"));
        _fs.AddFile(@"D:\Mods\MyMod\Data\Scripts\Library\PGBase.lua", new MockFileData("-- base"));
        _fs.AddFile(@"D:\Games\FoC\Data\Scripts\GameObject\Hero.lua", new MockFileData("-- vanilla hero"));
        _fs.AddFile(@"D:\Games\FoC\Data\Scripts\AI\SpaceMode\Attack.lua", new MockFileData("-- ai"));

        var provider = TestServices.Build(services => services.AddSingleton<IFileSystem>(_fs));
        _factory = provider.GetRequiredService<IScriptSourceMapFactory>();
    }

    private IScriptSourceMap Map()
    {
        return _factory.Create([ModRoot, GameRoot]);
    }

    // -- game path to document -----------------------------------------------------------------

    [Fact]
    public void ResolveToDocumentUri_GameRelativePath_FindsTheModCopyFirstWithOnDiskSpelling()
    {
        var uri = Map().ResolveToDocumentUri(@".\DATA\SCRIPTS\gameobject\hero.LUA");

        Assert.Equal("file:///D:/Mods/MyMod/Data/Scripts/GameObject/Hero.lua", uri);
    }

    [Fact]
    public void ResolveToDocumentUri_OnlyInTheGame_FallsThroughToTheNextRoot()
    {
        var uri = Map().ResolveToDocumentUri(@"Data\Scripts\AI\SpaceMode\Attack.lua");

        Assert.Equal("file:///D:/Games/FoC/Data/Scripts/AI/SpaceMode/Attack.lua", uri);
    }

    [Fact]
    public void ResolveToDocumentUri_ForwardSlashesAndNoDataPrefix_StillResolves()
    {
        var uri = Map().ResolveToDocumentUri("Library/PGBase.lua");

        Assert.Equal("file:///D:/Mods/MyMod/Data/Scripts/Library/PGBase.lua", uri);
    }

    [Fact]
    public void ResolveToDocumentUri_AbsolutePathThatExists_IsUsedDirectly()
    {
        var uri = Map().ResolveToDocumentUri(@"D:\Games\FoC\Data\Scripts\AI\SpaceMode\Attack.lua");

        Assert.Equal("file:///D:/Games/FoC/Data/Scripts/AI/SpaceMode/Attack.lua", uri);
    }

    [Fact]
    public void ResolveToDocumentUri_BareFileName_MatchesDirectlyUnderARootOnly()
    {
        // The last-resort tail is the file name at a root, never a recursive search: the game's
        // script roots hold thousands of files and a name alone is not enough to pick one.
        _fs.AddFile(@"D:\Mods\MyMod\Data\Scripts\Loose.lua", new MockFileData("-- loose"));

        Assert.Equal("file:///D:/Mods/MyMod/Data/Scripts/Loose.lua", Map().ResolveToDocumentUri("Loose.lua"));
        Assert.Null(Map().ResolveToDocumentUri("Attack.lua"));
    }

    [Fact]
    public void ResolveToDocumentUri_UnknownFile_IsNull()
    {
        Assert.Null(Map().ResolveToDocumentUri(@"Data\Scripts\Nowhere.lua"));
    }

    [Fact]
    public void ResolveToDocumentUri_SamePathTwice_IsCachedByFoldedKey()
    {
        var map = Map();

        var first = map.ResolveToDocumentUri(@"Data\Scripts\GameObject\Hero.lua");
        _fs.RemoveFile(@"D:\Mods\MyMod\Data\Scripts\GameObject\Hero.lua");
        var second = map.ResolveToDocumentUri("data/scripts/gameobject/HERO.lua");

        Assert.Equal(first, second);
    }

    // -- document to game path -----------------------------------------------------------------

    [Fact]
    public void ToGamePath_ReportedByTheGameBefore_ReusesThatSpelling()
    {
        var map = Map();
        map.ResolveToDocumentUri(@".\Data\Scripts\GameObject\Hero.lua");

        var gamePath = map.ToGamePath("file:///d:/mods/mymod/data/scripts/gameobject/hero.lua");

        Assert.Equal(@".\Data\Scripts\GameObject\Hero.lua", gamePath);
    }

    [Fact]
    public void ToGamePath_NeverReported_DerivesDataScriptsFormUnderItsRoot()
    {
        var gamePath = Map().ToGamePath("file:///D:/Mods/MyMod/Data/Scripts/Library/PGBase.lua");

        Assert.Equal(@"Data\Scripts\Library\PGBase.lua", gamePath);
    }

    [Fact]
    public void ToGamePath_RootWithoutDataSegment_PrependsBoth()
    {
        _fs.AddFile(@"D:\Loose\Story.lua", new MockFileData("-- loose"));
        var map = _factory.Create([@"D:\Loose"]);

        Assert.Equal(@"Data\Scripts\Story.lua", map.ToGamePath(@"D:\Loose\Story.lua"));
    }

    [Fact]
    public void ToGamePath_OutsideEveryRoot_IsNull()
    {
        Assert.Null(Map().ToGamePath("file:///D:/Elsewhere/Thing.lua"));
    }

    [Fact]
    public void Roots_KeepTheOrderGiven()
    {
        Assert.Equal([ModRoot, GameRoot], Map().Roots);
    }

    [Fact]
    public void ResolveToDocumentUri_ReturnsTheIndexKeyShape()
    {
        var helper = new FileHelper(_fs);

        var uri = Map().ResolveToDocumentUri(@"Data\Scripts\GameObject\Hero.lua");

        Assert.NotNull(uri);
        Assert.Equal(helper.NormalizeUri(uri), uri);
        Assert.True(DocumentUris.Same(uri, "file:///d:/mods/mymod/data/scripts/gameobject/hero.lua"));
    }
}
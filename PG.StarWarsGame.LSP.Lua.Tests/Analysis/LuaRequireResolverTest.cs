// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions.TestingHelpers;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Lua.Analysis;

namespace PG.StarWarsGame.LSP.Lua.Tests.Analysis;

public sealed class LuaRequireResolverTest
{
    private static readonly IFileHelper s_fileHelper = new FileHelper(new MockFileSystem());

    private static readonly IReadOnlyDictionary<string, DocumentIndex> WorkspaceDocs = MakeDocs(
        "file:///data/scripts/library/pgstatemachine.lua",
        "file:///data/scripts/library/eawx-std/modcontentloader.lua",
        "file:///data/scripts/customfactionname.lua",
        "file:///data/scripts/miscellaneous/fleetevents.lua",
        "file:///data/scripts/library/spawn-sets/independent_forces.lua"
    );

    private static Dictionary<string, DocumentIndex> MakeDocs(params string[] uris)
        => MakeDocs(uris.Select((u, i) => (u, i)));

    private static Dictionary<string, DocumentIndex> MakeDocs(IEnumerable<(string Uri, int Rank)> entries)
    {
        var dict = new Dictionary<string, DocumentIndex>(StringComparer.OrdinalIgnoreCase);
        foreach (var (uri, rank) in entries)
            dict[uri] = new DocumentIndex(uri, 1, [], [], LayerRank: rank);
        return dict;
    }

    [Fact]
    public void Resolve_BareNameInLibrary_ReturnsUri()
    {
        var result = LuaRequireResolver.Resolve("PGStateMachine", WorkspaceDocs, s_fileHelper);
        Assert.NotNull(result);
    }

    [Fact]
    public void Resolve_BareNameInOtherDirectory_ReturnsUri()
    {
        var result = LuaRequireResolver.Resolve("FleetEvents", WorkspaceDocs, s_fileHelper);
        Assert.NotNull(result);
    }

    [Fact]
    public void Resolve_SlashPath_ReturnsUri()
    {
        var result = LuaRequireResolver.Resolve("eawx-std/ModContentLoader", WorkspaceDocs, s_fileHelper);
        Assert.NotNull(result);
    }

    [Fact]
    public void Resolve_SlashPathSubdirectory_ReturnsUri()
    {
        var result = LuaRequireResolver.Resolve("spawn-sets/INDEPENDENT_FORCES", WorkspaceDocs, s_fileHelper);
        Assert.NotNull(result);
    }

    [Fact]
    public void Resolve_CaseInsensitive_ReturnsUri()
    {
        var result = LuaRequireResolver.Resolve("pgstatemachine", WorkspaceDocs, s_fileHelper);
        Assert.NotNull(result);
    }

    [Fact]
    public void Resolve_NoMatch_ReturnsNull()
    {
        var result = LuaRequireResolver.Resolve("NonExistentModule", WorkspaceDocs, s_fileHelper);
        Assert.Null(result);
    }

    [Fact]
    public void Resolve_RelativePathWithDots_ReturnsSkip()
    {
        // Relative traversal (../../X) cannot be reliably resolved — should return null
        // without triggering a false-positive diagnostic (caller must distinguish).
        var result = LuaRequireResolver.Resolve("../../CustomFactionName", WorkspaceDocs, s_fileHelper);
        Assert.Null(result);
    }

    [Fact]
    public void IsRelative_RelativeArg_ReturnsTrue()
    {
        Assert.True(LuaRequireResolver.IsRelative("../../CustomFactionName"));
        Assert.True(LuaRequireResolver.IsRelative("./LocalLib"));
        Assert.True(LuaRequireResolver.IsRelative("../Sibling"));
    }

    [Fact]
    public void IsRelative_AbsoluteOrBareArg_ReturnsFalse()
    {
        Assert.False(LuaRequireResolver.IsRelative("PGStateMachine"));
        Assert.False(LuaRequireResolver.IsRelative("eawx-std/ModContentLoader"));
    }

    [Fact]
    public void Resolve_BackslashPath_ReturnsUri()
    {
        // Engine scripts on Windows may use backslash separators in require args
        var result = LuaRequireResolver.Resolve("eawx-std\\ModContentLoader", WorkspaceDocs, s_fileHelper);
        Assert.NotNull(result);
    }

    [Fact]
    public void Resolve_MultipleLayerMatches_ReturnsHighestRankUri()
    {
        // Two files with the same logical name in different layers — highest rank wins.
        const string basePath  = "file:///base/scripts/foo.lua";
        const string addonPath = "file:///addon/scripts/foo.lua";
        var docs = MakeDocs([(basePath, 1), (addonPath, 2)]);

        var result = LuaRequireResolver.Resolve("foo", docs, s_fileHelper);

        Assert.Equal(addonPath, result);
    }

    [Fact]
    public void Resolve_SingleMatch_ReturnsItRegardlessOfRank()
    {
        const string uri = "file:///scripts/bar.lua";
        var docs = MakeDocs([(uri, 99)]);

        var result = LuaRequireResolver.Resolve("bar", docs, s_fileHelper);

        Assert.Equal(uri, result);
    }
}
// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions.TestingHelpers;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;

namespace PG.StarWarsGame.LSP.Core.Tests.Workspace;

public sealed class ProjectLayerMapTest
{
    private static readonly FileHelper Helper = new(new MockFileSystem());

    private static string Root(string sub)
    {
        return Path.Combine(Path.GetPathRoot(Path.GetFullPath("."))!, sub);
    }

    private static string Uri(params string[] segments)
    {
        return Helper.PathToFileUri(Path.Combine(segments));
    }

    private static ProjectLayer Layer(int rank, string name, params string[] xmlDirs)
    {
        return new ProjectLayer(rank, name, xmlDirs, [], [], [], null);
    }

    [Fact]
    public void GetRank_NoLayersSet_ReturnsZero()
    {
        var map = new ProjectLayerMap(Helper);

        Assert.Equal(0, map.GetRank(Uri(Root("eawx"), "data", "xml", "foo.xml")));
    }

    [Fact]
    public void GetRank_FileUnderLayerDirectory_ReturnsThatLayersRank()
    {
        var coreDir = Path.Combine(Root("eawx"), "data", "xml");
        var revDir = Path.Combine(Root("eawx"), "rev", "data", "xml");
        var map = new ProjectLayerMap(Helper);
        map.SetLayers([Layer(0, "Core", coreDir), Layer(1, "Rev", revDir)]);

        Assert.Equal(0, map.GetRank(Uri(coreDir, "units.xml")));
        Assert.Equal(1, map.GetRank(Uri(revDir, "units.xml")));
    }

    [Fact]
    public void GetRank_NestedLayerDirectories_LongestPrefixWins()
    {
        // A higher layer nested inside a lower layer's tree must win by longest prefix.
        var outer = Path.Combine(Root("eawx"), "data");
        var inner = Path.Combine(Root("eawx"), "data", "addon", "xml");
        var map = new ProjectLayerMap(Helper);
        map.SetLayers([Layer(0, "Outer", outer), Layer(2, "Inner", inner)]);

        Assert.Equal(2, map.GetRank(Uri(inner, "thing.xml")));
        Assert.Equal(0, map.GetRank(Uri(outer, "xml", "thing.xml")));
    }

    [Fact]
    public void GetRank_FileOutsideAllLayers_DefaultsToHighestRank()
    {
        var coreDir = Path.Combine(Root("eawx"), "data", "xml");
        var revDir = Path.Combine(Root("eawx"), "rev", "data", "xml");
        var map = new ProjectLayerMap(Helper);
        map.SetLayers([Layer(0, "Core", coreDir), Layer(3, "Rev", revDir)]);

        // An ad-hoc file opened outside any project layer should still win, so it takes the top rank.
        Assert.Equal(3, map.GetRank(Uri(Root("somewhere"), "else", "loose.xml")));
    }

    [Fact]
    public void GetLayerName_ReturnsNameForRank_NullForUnknown()
    {
        var map = new ProjectLayerMap(Helper);
        map.SetLayers([Layer(0, "Core", Path.Combine(Root("eawx"), "data", "xml"))]);

        Assert.Equal("Core", map.GetLayerName(0));
        Assert.Null(map.GetLayerName(5));
    }
}

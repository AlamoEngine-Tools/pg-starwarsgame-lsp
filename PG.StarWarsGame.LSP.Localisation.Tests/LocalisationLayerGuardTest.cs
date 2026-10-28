// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions.TestingHelpers;
using PG.StarWarsGame.LSP.Core.Messages;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Localisation;
using PG.StarWarsGame.LSP.Testing;

namespace PG.StarWarsGame.LSP.Localisation.Tests;

/// <summary>A dependency's text files are read-only to the project built on it (#276).</summary>
public sealed class LocalisationLayerGuardTest
{
    private static readonly IMessageCatalog Messages = ResxMessageCatalog.Server("en");

    public static TheoryData<string, int> SetupsAndRanks()
    {
        var data = new TheoryData<string, int>();
        foreach (var (name, rank) in ProjectSetup.NamesAndRanks) data.Add(name, rank);
        return data;
    }

    // Every shape, a text file in every layer (#280): the leaf's is writable, any lower layer's is
    // refused naming that layer.
    [Theory]
    [MemberData(nameof(SetupsAndRanks))]
    public void ALowerLayersFile_IsRefused_NamingItsLayer_TheLeafsIsWritable(string name, int rank)
    {
        var setup = ProjectSetup.Named(name);
        var helper = new FileHelper(setup.Disk());
        var map = new ProjectLayerMap(helper);
        map.SetLayers(setup.Layers);

        var objection = LocalisationLayerGuard.ReadOnlyObjection(map, helper,
            setup.PathOf(rank, "Text/MasterTextFile_English.csv"), Messages);

        if (rank == setup.LeafRank)
        {
            Assert.Null(objection);
            return;
        }

        Assert.NotNull(objection);
        Assert.Contains(setup.Projects[rank], objection);
        Assert.Contains("read-only", objection);
    }

    [Fact]
    public void BeforeAnyProjectIsLoaded_NothingIsRefused()
    {
        var helper = new FileHelper(new MockFileSystem());

        Assert.Null(LocalisationLayerGuard.ReadOnlyObjection(
            new ProjectLayerMap(helper), helper, "/core/data/text/MasterTextFile.csv", Messages));
    }
}
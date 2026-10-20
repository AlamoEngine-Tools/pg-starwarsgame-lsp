// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions.TestingHelpers;
using PG.StarWarsGame.LSP.Core.Messages;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Server.Localisation;

namespace PG.StarWarsGame.LSP.Server.Tests.Localisation;

/// <summary>A dependency's text files are read-only to the project built on it (#276).</summary>
public sealed class LocalisationLayerGuardTest
{
    private static readonly IMessageCatalog Messages = ResxMessageCatalog.Server("en");

    internal static ProjectLayerMap TwoLayers(FileHelper helper)
    {
        var map = new ProjectLayerMap(helper);
        map.SetLayers([
            new ProjectLayer(0, "Core", [], [], ["/core/data/text"], [], null),
            new ProjectLayer(1, "Rev", [], [], ["/mod/data/text"], [], null)
        ]);
        return map;
    }

    [Fact]
    public void ADependencysFile_IsRefused_NamingTheDependency()
    {
        var helper = new FileHelper(new MockFileSystem());

        var objection = LocalisationLayerGuard.ReadOnlyObjection(
            TwoLayers(helper), helper, "/core/data/text/MasterTextFile.csv", Messages);

        Assert.NotNull(objection);
        Assert.Contains("Core", objection);
        Assert.Contains("read-only", objection);
    }

    [Fact]
    public void TheProjectsOwnFile_IsWritable()
    {
        var helper = new FileHelper(new MockFileSystem());

        Assert.Null(LocalisationLayerGuard.ReadOnlyObjection(
            TwoLayers(helper), helper, "/mod/data/text/MasterTextFile.csv", Messages));
    }

    [Fact]
    public void BeforeAnyProjectIsLoaded_NothingIsRefused()
    {
        var helper = new FileHelper(new MockFileSystem());

        Assert.Null(LocalisationLayerGuard.ReadOnlyObjection(
            new ProjectLayerMap(helper), helper, "/core/data/text/MasterTextFile.csv", Messages));
    }
}
// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Server.Project;
using PG.StarWarsGame.LSP.Testing;

namespace PG.StarWarsGame.LSP.Server.Tests.Project;

/// <summary>
///     The shared setups are the layers the server builds from the same project files, never an
///     approximation of them: lower-cased roots, ranks dependencies first (#280).
/// </summary>
public sealed class ProjectSetupTest
{
    public static TheoryData<string> Setups => new(ProjectSetup.Names);

    [Theory]
    [MemberData(nameof(Setups))]
    public void TheLayers_AreWhatTheResolverBuildsFromTheSetupsProjectFiles(string name)
    {
        var setup = ProjectSetup.Named(name);
        var fileHelper = new FileHelper(setup.Disk());
        var loader = new ModProjectLoader(fileHelper, NullLogger<ModProjectLoader>.Instance);
        var resolver = new ModProjectResolver(fileHelper, loader,
            new ProjectDependencyGraph(NullLogger<ProjectDependencyGraph>.Instance),
            NullLogger<ModProjectResolver>.Instance);
        var leaf = setup.ProjectFileOf(setup.LeafRank);

        var configuration = resolver.Resolve(leaf, loader.Load(leaf));
        var resolved = configuration.Layers;
        var expectedConfiguration = setup.Configuration();

        Assert.Equal(expectedConfiguration.XmlDirectories, configuration.XmlDirectories);
        Assert.Equal(expectedConfiguration.ScriptRoots, configuration.ScriptRoots);
        Assert.Equal(expectedConfiguration.TextRoots, configuration.TextRoots);
        Assert.Equal(expectedConfiguration.AssetRoots, configuration.AssetRoots);
        Assert.Equal(expectedConfiguration.StoryDialogRoots, configuration.StoryDialogRoots);
        Assert.Equal(expectedConfiguration.TextResourceType, configuration.TextResourceType);
        Assert.Equal(setup.Layers.Count, resolved.Count);
        for (var rank = 0; rank < resolved.Count; rank++)
        {
            var (expected, actual) = (setup.Layers[rank], resolved[rank]);
            Assert.Equal(expected.Rank, actual.Rank);
            Assert.Equal(expected.Name, actual.Name);
            Assert.Equal(expected.XmlDirectories, actual.XmlDirectories);
            Assert.Equal(expected.ScriptRoots, actual.ScriptRoots);
            Assert.Equal(expected.TextRoots, actual.TextRoots);
            Assert.Equal(expected.AssetRoots, actual.AssetRoots);
            Assert.Equal(expected.TextResourceType, actual.TextResourceType);
            Assert.Equal(expected.ProjectPath, actual.ProjectPath);
            Assert.Equal(expected.StoryDialogRoots, actual.StoryDialogRoots);
        }
    }

    [Fact]
    public void TheShapes_AreAModAlone_OverVanilla_AndOverAModOverVanilla()
    {
        Assert.Equal([1, 2, 3], ProjectSetup.All.Select(s => s.Layers.Count));
        Assert.All(ProjectSetup.All, s => Assert.Equal("Mod", s.Leaf.Name));
    }

    // A document URI as the editor sends it and the layer roots both keep the case on disk; every
    // URI the server builds under a root is built from it (#291).
    [Theory]
    [MemberData(nameof(Setups))]
    public void AUri_AndTheLayerRoots_KeepTheCaseOnDisk(string name)
    {
        var setup = ProjectSetup.Named(name);

        var uri = setup.Uri(setup.LeafRank, "Scripts/GameObject/Interdictor.lua");

        Assert.Contains("/Work/Mod/Data/Scripts/GameObject/Interdictor.lua", uri, StringComparison.Ordinal);
        Assert.All(setup.Leaf.ScriptRoots, r => Assert.EndsWith("/Work/Mod/Data/Scripts", r, StringComparison.Ordinal));
    }
}
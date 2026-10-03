// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions.TestingHelpers;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Server.Debug;

namespace PG.StarWarsGame.LSP.Server.Tests.Debug;

public sealed class GetLaunchLayersHandlerTest
{
    private static ProjectLayer Layer(string name, int rank, string projectDir, params string[] directories)
    {
        return new ProjectLayer(rank, name,
            directories.Where(d => d.Contains("/XML", StringComparison.OrdinalIgnoreCase)).ToList(),
            directories.Where(d => d.Contains("/Scripts", StringComparison.OrdinalIgnoreCase)).ToList(),
            [], directories.Where(d => d.Contains("/Art", StringComparison.OrdinalIgnoreCase)).ToList(),
            null, $"{projectDir}/{name}.pgproj");
    }

    private static ProjectLayerMap Layers(params ProjectLayer[] layers)
    {
        var map = new ProjectLayerMap(new FileHelper(new MockFileSystem()));
        map.SetLayers(layers);
        return map;
    }

    private static GetLaunchLayersHandler Handler(ProjectLayerMap layers, ILspConfigurationProvider? config = null)
    {
        return new GetLaunchLayersHandler(layers, config ?? new FakeLspConfigurationProvider());
    }

    [Fact]
    public async Task Handle_DebuggerFlagOff_DisabledWithNoLayers()
    {
        var config = FakeLspConfigurationProvider.WithFeatures(
            new FeatureFlags { Lua = new LuaFeatureFlags { Debugger = false } });
        var layers = Layers(Layer("Mod", 1, "D:/Mods/Mod", "D:/Mods/Mod/Data/Scripts"));

        var result = await Handler(layers, config).Handle(new GetLaunchLayersParams(), CancellationToken.None);

        Assert.False(result.Enabled);
        Assert.Empty(result.Layers);
    }

    [Fact]
    public async Task Handle_LayersHighestRankFirst_WithScriptRootsAndProjectDirectory()
    {
        var layers = Layers(
            Layer("Dependency", 0, "D:/Mods/Dep", "D:/Mods/Dep/Data/Scripts"),
            Layer("Mod", 1, "D:/Mods/Mod", "D:/Mods/Mod/Data/Scripts", "D:/Mods/Mod/Data/Scripts/Story"));

        var result = await Handler(layers).Handle(new GetLaunchLayersParams(), CancellationToken.None);

        Assert.True(result.Enabled);
        Assert.Equal(["Mod", "Dependency"], result.Layers.Select(l => l.Name));
        Assert.Equal(["D:/Mods/Mod/Data/Scripts", "D:/Mods/Mod/Data/Scripts/Story"], result.Layers[0].ScriptRoots);
        Assert.Equal("D:/Mods/Mod/Mod.pgproj", result.Layers[0].ProjectPath);
        Assert.Equal("D:/Mods/Mod", result.Layers[0].ProjectDirectory);
    }

    [Fact]
    public async Task Handle_EveryDirectoryUnderData_IsRunnableFromTheProjectDirectory()
    {
        var layers = Layers(Layer("Mod", 0, "D:/Mods/Mod",
            "D:/Mods/Mod/Data/XML", "D:/Mods/Mod/Data/Scripts", "D:/Mods/Mod/Data/Art"));

        var layer = (await Handler(layers).Handle(new GetLaunchLayersParams(), CancellationToken.None)).Layers.Single();

        Assert.Equal("D:/Mods/Mod", layer.ModPath);
        Assert.Null(layer.NotRunnableReason);
    }

    [Fact]
    public async Task Handle_DataFolderSpelledInAnyCase_StillRunnable()
    {
        var layers = Layers(Layer("Mod", 0, "D:/Mods/Mod", "D:/Mods/Mod/data/scripts"));

        var layer = (await Handler(layers).Handle(new GetLaunchLayersParams(), CancellationToken.None)).Layers.Single();

        Assert.Equal("D:/Mods/Mod", layer.ModPath);
    }

    [Fact]
    public async Task Handle_DirectoryOutsideData_NotRunnableNamingTheDirectory()
    {
        var layers = Layers(Layer("Mod", 0, "D:/Mods/Mod", "D:/Mods/Mod/Data/Scripts", "D:/Mods/Mod/src/XML"));

        var layer = (await Handler(layers).Handle(new GetLaunchLayersParams(), CancellationToken.None)).Layers.Single();

        Assert.Null(layer.ModPath);
        Assert.Contains("D:/Mods/Mod/src/XML", layer.NotRunnableReason);
        Assert.Contains("Data", layer.NotRunnableReason);
    }

    [Fact]
    public async Task Handle_ProjectDirectoryWithASpace_NotRunnable()
    {
        var layers = Layers(Layer("Mod", 0, "D:/My Mods/Mod", "D:/My Mods/Mod/Data/Scripts"));

        var layer = (await Handler(layers).Handle(new GetLaunchLayersParams(), CancellationToken.None)).Layers.Single();

        Assert.Null(layer.ModPath);
        Assert.Contains("space", layer.NotRunnableReason);
    }

    [Fact]
    public async Task Handle_LayerWithoutProjectFile_NotRunnable()
    {
        var map = Layers(new ProjectLayer(0, "workspace", [], ["D:/Loose/Scripts"], [], [], null));

        var layer = (await Handler(map).Handle(new GetLaunchLayersParams(), CancellationToken.None)).Layers.Single();

        Assert.Null(layer.ModPath);
        Assert.Null(layer.ProjectDirectory);
        Assert.Contains("no project file", layer.NotRunnableReason);
        Assert.Equal(["D:/Loose/Scripts"], layer.ScriptRoots);
    }

    [Fact]
    public async Task Handle_BeforeAnyProjectLoaded_EnabledWithNoLayers()
    {
        var result = await Handler(Layers()).Handle(new GetLaunchLayersParams(), CancellationToken.None);

        Assert.True(result.Enabled);
        Assert.Empty(result.Layers);
    }
}
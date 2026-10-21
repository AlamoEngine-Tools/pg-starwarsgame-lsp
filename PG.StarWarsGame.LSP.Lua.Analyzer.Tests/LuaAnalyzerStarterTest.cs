// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Lua.Analysis.Instances;

namespace PG.StarWarsGame.LSP.Lua.Analyzer.Tests;

/// <summary>Whether the analyzer gets the project's layers once they are known.</summary>
public sealed class LuaAnalyzerStarterTest
{
    private static readonly IReadOnlyList<ProjectLayer> Layers =
        [new(0, "Mod", [], ["D:/Mods/MyMod/Data/Scripts"], [], [], null)];

    private readonly RecordingLayers _analyzer = new();

    private LuaAnalyzerStarter Starter(bool flag, bool executableFound = true)
    {
        var config = new FixedConfiguration(new LspConfiguration
        {
            Features = new FeatureFlags { Lua = new LuaFeatureFlags { Analyzer = flag } }
        });
        return new LuaAnalyzerStarter(_analyzer, config, executableFound, NullLogger<LuaAnalyzerStarter>.Instance);
    }

    [Fact]
    public async Task FlagOn_TheAnalyzerGetsTheLayers()
    {
        await Starter(true).ConfigureAsync(Layers);

        Assert.Same(Layers, Assert.Single(_analyzer.Configured));
    }

    [Fact]
    public async Task FlagOff_TheAnalyzerDoesNotStart()
    {
        await Starter(false).ConfigureAsync(Layers);

        Assert.Empty(_analyzer.Configured);
    }

    [Fact]
    public async Task NoExecutable_TheAnalyzerDoesNotStart()
    {
        await Starter(true, false).ConfigureAsync(Layers);

        Assert.Empty(_analyzer.Configured);
    }

    [Theory]
    [InlineData("C:/bin/emmylua_ls.exe", true, "C:/bin/emmylua_ls.exe")] // the variable wins
    [InlineData(null, true, "beside")] // then the folder beside the server
    [InlineData(null, false, null)] // then nothing
    public void TheExecutable_IsTheVariable_ThenTheFolderBesideTheServer(string? variable, bool besideExists,
        string? expected)
    {
        var beside = Path.Combine("C:/server", "emmylua",
            OperatingSystem.IsWindows() ? "emmylua_ls.exe" : "emmylua_ls");
        var resolved = LuaAnalyzerExecutable.Resolve("C:/server", _ => variable,
            path => path == variable || (besideExists && path == beside));

        Assert.Equal<string?>(expected == "beside" ? beside : expected, resolved);
    }

    private sealed class RecordingLayers : ILuaAnalyzerLayers
    {
        public List<IReadOnlyList<ProjectLayer>> Configured { get; } = [];

        public void Configure(IReadOnlyList<ProjectLayer> layers)
        {
            Configured.Add(layers);
        }
    }

    private sealed class FixedConfiguration(LspConfiguration current) : ILspConfigurationProvider
    {
        public LspConfiguration Current { get; } = current;

        public void LoadFrom(object? initializationOptions)
        {
        }
    }
}
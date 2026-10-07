// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Workspace;

namespace PG.StarWarsGame.LSP.Lua.Analyzer.Tests;

/// <summary>Whether and where the analyzer starts once the project's layers are known.</summary>
public sealed class LuaAnalyzerStarterTest : IAsyncDisposable
{
    private static readonly IReadOnlyList<ProjectLayer> Layers =
        [new ProjectLayer(0, "Mod", [], ["D:/Mods/MyMod/Data/Scripts"], [], [], null)];

    private readonly LuaAnalyzerHost _host = new(new LuaAnalyzerOptions
    {
        Command = "dotnet",
        LeadingArguments = [Path.Combine(AppContext.BaseDirectory, "PG.StarWarsGame.LSP.Lua.Analyzer.Fake.dll")],
        ConfigDirectory = Path.Combine(Path.GetTempPath(), "aet-analyzer-starter-" + Guid.NewGuid().ToString("N"))
    }, NullLogger<LuaAnalyzerHost>.Instance);

    public async ValueTask DisposeAsync()
    {
        await _host.DisposeAsync();
    }

    private LuaAnalyzerStarter Starter(bool flag, bool executableFound = true)
    {
        var config = new FixedConfiguration(new LspConfiguration
        {
            Features = new FeatureFlags { Lua = new LuaFeatureFlags { Analyzer = flag } }
        });
        return new LuaAnalyzerStarter(_host, config, new LuaStubLocation { Directory = "D:/schema/lua" },
            executableFound, NullLogger<LuaAnalyzerStarter>.Instance);
    }

    [Fact]
    public async Task FlagOn_TheAnalyzerStarts()
    {
        await Starter(true).ConfigureAsync(Layers);

        Assert.True(_host.IsRunning);
    }

    [Fact]
    public async Task FlagOff_TheAnalyzerDoesNotStart()
    {
        await Starter(false).ConfigureAsync(Layers);

        Assert.Equal(0, _host.Starts);
    }

    [Fact]
    public async Task NoExecutable_TheAnalyzerDoesNotStart()
    {
        await Starter(true, executableFound: false).ConfigureAsync(Layers);

        Assert.Equal(0, _host.Starts);
    }

    [Theory]
    [InlineData("C:/bin/emmylua_ls.exe", true, "C:/bin/emmylua_ls.exe")] // the variable wins
    [InlineData(null, true, "beside")] // then the folder beside the server
    [InlineData(null, false, null)] // then nothing
    public void TheExecutable_IsTheVariable_ThenTheFolderBesideTheServer(string? variable, bool besideExists,
        string? expected)
    {
        var beside = Path.Combine("C:/server", "emmylua", OperatingSystem.IsWindows() ? "emmylua_ls.exe" : "emmylua_ls");
        var resolved = LuaAnalyzerExecutable.Resolve("C:/server", _ => variable,
            path => path == variable || (besideExists && path == beside));

        Assert.Equal<string?>(expected == "beside" ? beside : expected, resolved);
    }

    private sealed class FixedConfiguration(LspConfiguration current) : ILspConfigurationProvider
    {
        public LspConfiguration Current { get; } = current;

        public void LoadFrom(object? initializationOptions)
        {
        }
    }
}

// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Lua.Analysis.Instances;

namespace PG.StarWarsGame.LSP.Lua.Analyzer.Tests;

/// <summary>
///     Whether the analyzers start once the project's layers are known: the per-script ones, and the
///     whole-project one that answers renames and where a library is used.
/// </summary>
public sealed class LuaAnalyzerStarterTest : IAsyncDisposable
{
    private static readonly IReadOnlyList<ProjectLayer> Layers =
        [new(0, "Mod", [], ["D:/Mods/MyMod/Data/Scripts"], [], [], null)];

    private readonly RecordingLayers _instances = new();

    private readonly LuaAnalyzerHost _project = new(new LuaAnalyzerOptions
    {
        Command = "dotnet",
        LeadingArguments = [Path.Combine(AppContext.BaseDirectory, "PG.StarWarsGame.LSP.Lua.Analyzer.Fake.dll")],
        ConfigDirectory = Path.Combine(Path.GetTempPath(), "aet-analyzer-starter-" + Guid.NewGuid().ToString("N"))
    }, NullLogger<LuaAnalyzerHost>.Instance);

    public async ValueTask DisposeAsync()
    {
        await _project.DisposeAsync();
    }

    private LuaAnalyzerStarter Starter(bool flag, bool executableFound = true)
    {
        var config = new FixedConfiguration(new LspConfiguration
        {
            Features = new FeatureFlags { Lua = new LuaFeatureFlags { Analyzer = flag } }
        });
        return new LuaAnalyzerStarter(_instances, _project, config, new LuaStubLocation { Directory = "D:/schema/lua" },
            executableFound, NullLogger<LuaAnalyzerStarter>.Instance);
    }

    [Fact]
    public async Task FlagOn_BothStart()
    {
        await Starter(true).ConfigureAsync(Layers);

        Assert.Same(Layers, Assert.Single(_instances.Configured));
        Assert.True(_project.IsRunning);
    }

    [Fact]
    public async Task FlagOff_NeitherStarts()
    {
        await Starter(false).ConfigureAsync(Layers);

        Assert.Empty(_instances.Configured);
        Assert.Equal(0, _project.Starts);
    }

    [Fact]
    public async Task NoExecutable_NeitherStarts()
    {
        await Starter(true, false).ConfigureAsync(Layers);

        Assert.Empty(_instances.Configured);
        Assert.Equal(0, _project.Starts);
    }

    /// <summary>
    ///     The layers keep their roots lower-cased; the analyzer compares paths as written, so a root
    ///     in the wrong case leaves every document the editor opens outside its workspace (measured
    ///     live: no findings at all).
    /// </summary>
    [Fact]
    public void ARootIsHandedOverInItsCaseOnDisk()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Case-insensitive file systems only.");
        var root = Path.Combine(Path.GetTempPath(), "AetCaseProbe" + Guid.NewGuid().ToString("N")[..6], "Data",
            "Scripts");
        Directory.CreateDirectory(root);

        var resolved = PathCasing.OnDisk(root.ToLowerInvariant());

        Assert.Equal(Path.GetFullPath(root), Path.GetFullPath(resolved));
        Assert.Equal(root.TrimEnd(Path.DirectorySeparatorChar), resolved.TrimEnd(Path.DirectorySeparatorChar),
            StringComparer.Ordinal);
    }

    // A project may declare a scripts folder it does not have yet (measured: the E2E workspace).
    // The analyzer then still compares the editor's URIs against the root, so the part that exists
    // must be in its case on disk.
    [Fact]
    public void ARootNotYetOnDisk_KeepsTheCaseOfThePartThatIs()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Case-insensitive file systems only.");
        var existing = Path.Combine(Path.GetTempPath(), "AetCaseProbe" + Guid.NewGuid().ToString("N")[..6], "Mod");
        Directory.CreateDirectory(existing);

        var resolved = PathCasing.OnDisk(Path.Combine(existing, "data", "scripts").ToLowerInvariant());

        Assert.Equal(Path.Combine(Path.GetFullPath(existing), "data", "scripts"), resolved, StringComparer.Ordinal);
    }

    [Fact]
    public void ARootNotOnDisk_IsHandedOverAsGiven()
    {
        Assert.Equal<string>("D:/no/such/root", PathCasing.OnDisk("D:/no/such/root"));
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
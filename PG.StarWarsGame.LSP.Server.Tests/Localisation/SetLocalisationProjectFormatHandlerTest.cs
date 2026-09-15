// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Server.Localisation;
using PG.StarWarsGame.LSP.Server.Project;

namespace PG.StarWarsGame.LSP.Server.Tests.Localisation;

/// <summary>
///     #121: an existing project's localisation format could only be changed by editing the
///     <c>.pgproj</c> - the reporter switched CSV to DAT by hand, and the editor setting that looked like
///     the answer only seeds new projects. This repoints the declared format and reloads, converting
///     nothing.
/// </summary>
public sealed class SetLocalisationProjectFormatHandlerTest
{
    private const string PgprojPath = "/mod/mymod.pgproj";

    [Fact]
    public async Task Switching_csv_to_dat_repoints_the_project_and_keeps_its_directory()
    {
        var (handler, reload, writer) = Build(declaredFormat: "CSV");

        var result = await handler.Handle(new SetLocalisationProjectFormatParams("Dat"), CancellationToken.None);

        Assert.Null(result.Error);
        Assert.True(result.Changed);
        Assert.Equal("CSV", result.PreviousFormat);
        Assert.Equal("DAT", result.Format);
        Assert.Equal((PgprojPath, "DAT", "data/text"), writer.LastCall);
        Assert.True(reload.Reloaded);
    }

    // After the reload, the registry says how many files the project can now actually load.
    [Fact]
    public async Task The_result_counts_the_files_in_the_new_format()
    {
        var registry = new LocalisationProjectRegistry();
        registry.Set(
        [
            new LocProjectInfo("MasterTextFile.csv", "/mod/data/text/MasterTextFile.csv", "Csv", "Root", 1),
            new LocProjectInfo("mastertextfile_english.dat", "/mod/data/text/mastertextfile_english.dat", "Dat", "Root", 1),
            new LocProjectInfo("mastertextfile_german.dat", "/mod/data/text/mastertextfile_german.dat", "Dat", "Root", 1)
        ]);
        var (handler, _, _) = Build(registry: registry);

        var result = await handler.Handle(new SetLocalisationProjectFormatParams("DAT"), CancellationToken.None);

        Assert.Equal(2, result.FilesInFormat);
    }

    [Theory]
    [InlineData("csv")]
    [InlineData("CSV")]
    public async Task The_format_it_already_loads_writes_nothing(string format)
    {
        var (handler, reload, writer) = Build(declaredFormat: "CSV");

        var result = await handler.Handle(new SetLocalisationProjectFormatParams(format), CancellationToken.None);

        Assert.Null(result.Error);
        Assert.False(result.Changed);
        Assert.Null(writer.LastCall);
        Assert.False(reload.Reloaded);
    }

    [Fact]
    public async Task An_unknown_format_is_refused()
    {
        var (handler, _, writer) = Build();

        var result = await handler.Handle(new SetLocalisationProjectFormatParams("Yaml"), CancellationToken.None);

        Assert.NotNull(result.Error);
        Assert.Null(writer.LastCall);
    }

    // The format lives in the project's localisation node; with none there is no directory to keep.
    [Fact]
    public async Task A_project_without_a_localisation_node_is_refused_and_pointed_at_creating_one()
    {
        var layer = new ProjectLayer(0, "Root", [], [], [], [], null, PgprojPath);
        var (handler, _, writer) = Build(layer: layer);

        var result = await handler.Handle(new SetLocalisationProjectFormatParams("Dat"), CancellationToken.None);

        Assert.Contains("localisation", result.Error!, StringComparison.OrdinalIgnoreCase);
        Assert.Null(writer.LastCall);
    }

    [Fact]
    public async Task No_project_file_is_refused()
    {
        var layer = new ProjectLayer(0, "Root", [], [], ["/mod/data/text"], [], "CSV");
        var (handler, _, writer) = Build(layer: layer);

        var result = await handler.Handle(new SetLocalisationProjectFormatParams("Dat"), CancellationToken.None);

        Assert.NotNull(result.Error);
        Assert.Null(writer.LastCall);
    }

    [Fact]
    public async Task The_feature_flag_off_is_refused()
    {
        var config = FakeLspConfigurationProvider.WithFeatures(
            new FeatureFlags { Tools = new ToolsFeatureFlags { Localisation = false } });
        var (handler, _, writer) = Build(config: config);

        var result = await handler.Handle(new SetLocalisationProjectFormatParams("Dat"), CancellationToken.None);

        Assert.NotNull(result.Error);
        Assert.Null(writer.LastCall);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static (SetLocalisationProjectFormatHandler Handler, SpyReloadService Reload, SpyFileWriter Writer) Build(
        string declaredFormat = "CSV", ProjectLayer? layer = null, LocalisationProjectRegistry? registry = null,
        ILspConfigurationProvider? config = null)
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData> { [PgprojPath] = new("{}") });
        var reload = new SpyReloadService
        {
            LastWorkspaceConfig = WorkspaceConfiguration.Empty with
            {
                Layers = [layer ?? new ProjectLayer(0, "Root", [], [], ["/mod/data/text"], [], declaredFormat, PgprojPath)]
            }
        };
        var writer = new SpyFileWriter();

        var handler = new SetLocalisationProjectFormatHandler(
            new FileHelper(fs),
            registry ?? new LocalisationProjectRegistry(),
            reload,
            writer,
            NullLogger<SetLocalisationProjectFormatHandler>.Instance,
            config ?? new FakeLspConfigurationProvider());

        return (handler, reload, writer);
    }

    private sealed class SpyReloadService : IModProjectReloadService
    {
        public bool Reloaded { get; private set; }
        public IReadOnlyList<string>? LastAssetRoots => null;
        public WorkspaceConfiguration? LastWorkspaceConfig { get; init; }
        public IReadOnlyList<string>? LastWorkspaceRoots => null;

        public Task LoadAsync(IEnumerable<string> workspaceRoots, CancellationToken ct)
        {
            return Task.CompletedTask;
        }

        public Task ReloadAsync(CancellationToken ct)
        {
            Reloaded = true;
            return Task.CompletedTask;
        }

        public Task ReloadLocalisationAsync(CancellationToken ct)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class SpyFileWriter : IModProjectFileWriter
    {
        public (string PgprojPath, string Type, string Directory)? LastCall { get; private set; }

        public Task SetLocalisationAsync(string pgprojPath, string type, string directory, CancellationToken ct)
        {
            LastCall = (pgprojPath, type, directory);
            return Task.CompletedTask;
        }
    }
}

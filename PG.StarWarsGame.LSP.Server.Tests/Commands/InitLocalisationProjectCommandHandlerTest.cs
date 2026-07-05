// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Xml.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.Localisation.Baseline;
using PG.StarWarsGame.Localisation.Data;
using PG.StarWarsGame.Localisation.IO.Csv;
using PG.StarWarsGame.Localisation.IO.Properties;
using PG.StarWarsGame.Localisation.IO.Xml;
using PG.StarWarsGame.Localisation.Services;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Server.Commands;
using PG.StarWarsGame.LSP.Server.Localisation;
using PG.StarWarsGame.LSP.Server.Project;

namespace PG.StarWarsGame.LSP.Server.Tests.Commands;

public sealed class InitLocalisationProjectCommandHandlerTest
{
    private const string PgprojPath = "/mod/mymod.pgproj";

    // ── feature flag ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_LocalisationFlagOff_NoSeedFileNoReload()
    {
        var config = FakeLspConfigurationProvider.WithFeatures(
            new FeatureFlags { Tools = new ToolsFeatureFlags { Localisation = false } });
        var (handler, fs, reload, writer) = BuildHandler(
            ConfiguredLayer("Csv", "/mod/data/text"), lspConfig: config);

        await handler.Handle(NoArgsRequest(), CancellationToken.None);

        Assert.False(fs.File.Exists("/mod/data/text/MasterTextFile.csv"));
        Assert.False(reload.LocalisationOnlyReloaded);
        Assert.Null(writer.LastCall);
    }

    // ── already-configured root layer: format/directory come from the project, not the client ──

    [Fact]
    public async Task Handle_RootLayerConfigured_CreatesFileAtItsOwnDirectoryAndFormat()
    {
        var (handler, fs, _, _) = BuildHandler(ConfiguredLayer("Csv", "/mod/data/text"));

        await handler.Handle(NoArgsRequest(), CancellationToken.None);

        Assert.True(fs.File.Exists("/mod/data/text/MasterTextFile.csv"));
    }

    [Fact]
    public async Task Handle_RootLayerConfigured_ClientSuppliedFormatIsIgnored()
    {
        // Even if the client (mistakenly, or from stale state) sends a different format, the
        // project's own declared format wins.
        var (handler, fs, _, _) = BuildHandler(ConfiguredLayer("Xml", "/mod/data/text"));

        await handler.Handle(BootstrapRequest("Nls", "some/other/dir"), CancellationToken.None);

        Assert.True(fs.File.Exists("/mod/data/text/MasterTextFile.xml"));
        Assert.False(fs.File.Exists("/mod/some/other/dir/MasterTextFile.properties"));
    }

    [Fact]
    public async Task Handle_RootLayerConfigured_DoesNotWriteToPgproj()
    {
        var (handler, _, _, writer) = BuildHandler(ConfiguredLayer("Csv", "/mod/data/text"));

        await handler.Handle(NoArgsRequest(), CancellationToken.None);

        Assert.Null(writer.LastCall);
    }

    [Fact]
    public async Task Handle_RootLayerConfigured_TriggersLocalisationOnlyReload_NotFullReload()
    {
        var (handler, _, reload, _) = BuildHandler(ConfiguredLayer("Csv", "/mod/data/text"));

        await handler.Handle(NoArgsRequest(), CancellationToken.None);

        Assert.True(reload.LocalisationOnlyReloaded);
        Assert.False(reload.FullyReloaded);
    }

    [Fact]
    public async Task Handle_RootLayerConfigured_FileAlreadyExists_DoesNotOverwrite()
    {
        var mockFs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/mod/data/text/MasterTextFile.csv"] = new("EXISTING")
        });
        var (handler, fs, reload, _) = BuildHandler(ConfiguredLayer("Csv", "/mod/data/text"), mockFs);

        await handler.Handle(NoArgsRequest(), CancellationToken.None);

        Assert.Equal("EXISTING", fs.File.ReadAllText("/mod/data/text/MasterTextFile.csv"));
        Assert.False(reload.LocalisationOnlyReloaded);
        Assert.False(reload.FullyReloaded);
    }

    [Theory]
    [InlineData("Csv", "MasterTextFile.csv")]
    [InlineData("Xml", "MasterTextFile.xml")]
    [InlineData("Nls", "MasterTextFile.properties")]
    public async Task Handle_RootLayerConfigured_EachFormat_CreatesExpectedFile(string format, string fileName)
    {
        var (handler, fs, _, _) = BuildHandler(ConfiguredLayer(format, "/mod/data/text"));

        await handler.Handle(NoArgsRequest(), CancellationToken.None);

        Assert.True(fs.File.Exists($"/mod/data/text/{fileName}"));
    }

    [Fact]
    public async Task Handle_RootLayerConfigured_CsvContent_ContainsBaselineKeys()
    {
        var (handler, fs, _, _) = BuildHandler(ConfiguredLayer("Csv", "/mod/data/text"));

        await handler.Handle(NoArgsRequest(), CancellationToken.None);

        var content = fs.File.ReadAllText("/mod/data/text/MasterTextFile.csv");
        Assert.StartsWith("key,", content.Split('\n')[0]);
        Assert.Contains("TEXT_", content);
    }

    [Fact]
    public async Task Handle_RootLayerConfigured_XmlContent_IsWellFormedWithEntries()
    {
        var (handler, fs, _, _) = BuildHandler(ConfiguredLayer("Xml", "/mod/data/text"));

        await handler.Handle(NoArgsRequest(), CancellationToken.None);

        var xdoc = XDocument.Parse(fs.File.ReadAllText("/mod/data/text/MasterTextFile.xml"));
        Assert.NotNull(xdoc.Root);
        Assert.NotEmpty(xdoc.Root.Elements());
    }

    [Fact]
    public async Task Handle_RootLayerConfigured_UnsupportedFormat_NoFileCreated()
    {
        var (handler, fs, reload, writer) = BuildHandler(ConfiguredLayer("Dat", "/mod/data/text"));

        await handler.Handle(NoArgsRequest(), CancellationToken.None);

        Assert.Empty(fs.AllFiles);
        Assert.False(reload.LocalisationOnlyReloaded);
        Assert.False(reload.FullyReloaded);
        Assert.Null(writer.LastCall);
    }

    // ── bootstrap: no existing config, format+directory required from the client ────────────

    [Fact]
    public async Task Handle_NoExistingConfig_MissingDirectory_NoFileCreated()
    {
        var (handler, fs, reload, writer) = BuildHandler(BootstrapLayer());

        await handler.Handle(FormatOnlyRequest("Csv"), CancellationToken.None);

        Assert.Empty(fs.AllFiles);
        Assert.False(reload.FullyReloaded);
        Assert.Null(writer.LastCall);
    }

    [Fact]
    public async Task Handle_NoExistingConfig_MissingFormat_NoFileCreated()
    {
        var (handler, fs, reload, writer) = BuildHandler(BootstrapLayer());

        await handler.Handle(DirectoryOnlyRequest("data/text"), CancellationToken.None);

        Assert.Empty(fs.AllFiles);
        Assert.False(reload.FullyReloaded);
        Assert.Null(writer.LastCall);
    }

    [Fact]
    public async Task Handle_NoExistingConfig_NoPgprojAtAll_NoFileCreated()
    {
        // No resolved project at all (heuristic/no-.pgproj mode) — nothing to write a
        // localisation node into, so bootstrap is impossible.
        var (handler, fs, reload, writer) = BuildHandler(null);

        await handler.Handle(BootstrapRequest("Csv", "data/text"), CancellationToken.None);

        Assert.Empty(fs.AllFiles);
        Assert.False(reload.FullyReloaded);
        Assert.Null(writer.LastCall);
    }

    [Fact]
    public async Task Handle_NoExistingConfig_ValidFormatAndDirectory_CreatesFileRelativeToPgproj()
    {
        var (handler, fs, _, _) = BuildHandler(BootstrapLayer());

        await handler.Handle(BootstrapRequest("Csv", "data/text"), CancellationToken.None);

        Assert.True(fs.File.Exists("/mod/data/text/MasterTextFile.csv"));
    }

    [Fact]
    public async Task Handle_NoExistingConfig_ValidFormatAndDirectory_WritesLocalisationNodeToPgproj()
    {
        var (handler, _, _, writer) = BuildHandler(BootstrapLayer());

        await handler.Handle(BootstrapRequest("Csv", "data/text"), CancellationToken.None);

        Assert.NotNull(writer.LastCall);
        Assert.Equal(PgprojPath, writer.LastCall!.Value.PgprojPath);
        Assert.Equal("CSV", writer.LastCall!.Value.Type);
        Assert.Equal("data/text", writer.LastCall!.Value.Directory);
    }

    [Fact]
    public async Task Handle_NoExistingConfig_ValidFormatAndDirectory_TriggersFullReload_NotLocalisationOnly()
    {
        var (handler, _, reload, _) = BuildHandler(BootstrapLayer());

        await handler.Handle(BootstrapRequest("Csv", "data/text"), CancellationToken.None);

        Assert.True(reload.FullyReloaded);
        Assert.False(reload.LocalisationOnlyReloaded);
    }

    [Fact]
    public async Task Handle_NoExistingConfig_TargetFileAlreadyExists_DoesNotOverwrite_DoesNotWritePgproj()
    {
        var mockFs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/mod/data/text/MasterTextFile.csv"] = new("EXISTING")
        });
        var (handler, fs, reload, writer) = BuildHandler(BootstrapLayer(), mockFs);

        await handler.Handle(BootstrapRequest("Csv", "data/text"), CancellationToken.None);

        Assert.Equal("EXISTING", fs.File.ReadAllText("/mod/data/text/MasterTextFile.csv"));
        Assert.Null(writer.LastCall);
        Assert.False(reload.FullyReloaded);
    }

    [Fact]
    public async Task Handle_NoExistingConfig_UnsupportedFormat_NoFileCreated_NoPgprojWrite()
    {
        var (handler, fs, reload, writer) = BuildHandler(BootstrapLayer());

        await handler.Handle(BootstrapRequest("Dat", "data/text"), CancellationToken.None);

        Assert.Empty(fs.AllFiles);
        Assert.False(reload.FullyReloaded);
        Assert.Null(writer.LastCall);
    }

    [Fact]
    public async Task Handle_NullArguments_NoExistingConfig_NoFileCreated()
    {
        var (handler, fs, reload, writer) = BuildHandler(BootstrapLayer());

        await handler.Handle(new ExecuteCommandParams
        {
            Command = InitLocalisationProjectCommandHandler.CommandName
        }, CancellationToken.None);

        Assert.Empty(fs.AllFiles);
        Assert.False(reload.FullyReloaded);
        Assert.Null(writer.LastCall);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static ProjectLayer ConfiguredLayer(string resourceType, string textRoot)
    {
        return new ProjectLayer(0, "Root", [], [], [textRoot], [], resourceType, PgprojPath);
    }

    private static ProjectLayer BootstrapLayer()
    {
        return new ProjectLayer(0, "Root", [], [], [], [], null, PgprojPath);
    }

    private static ExecuteCommandParams NoArgsRequest()
    {
        return new ExecuteCommandParams
        {
            Command = InitLocalisationProjectCommandHandler.CommandName,
            Arguments = new JArray(JObject.FromObject(new { }))
        };
    }

    private static ExecuteCommandParams BootstrapRequest(string format, string directory)
    {
        return new ExecuteCommandParams
        {
            Command = InitLocalisationProjectCommandHandler.CommandName,
            Arguments = new JArray(JObject.FromObject(new { format, directory }))
        };
    }

    private static ExecuteCommandParams FormatOnlyRequest(string format)
    {
        return new ExecuteCommandParams
        {
            Command = InitLocalisationProjectCommandHandler.CommandName,
            Arguments = new JArray(JObject.FromObject(new { format }))
        };
    }

    private static ExecuteCommandParams DirectoryOnlyRequest(string directory)
    {
        return new ExecuteCommandParams
        {
            Command = InitLocalisationProjectCommandHandler.CommandName,
            Arguments = new JArray(JObject.FromObject(new { directory }))
        };
    }

    private static (InitLocalisationProjectCommandHandler handler, MockFileSystem fs,
        SpyReloadService reload, SpyFileWriter writer) BuildHandler(
            ProjectLayer? rootLayer, MockFileSystem? initialFs = null,
            ILspConfigurationProvider? lspConfig = null)
    {
        var mockFs = initialFs ?? new MockFileSystem();

        var services = new ServiceCollection();
        // Register MockFileSystem BEFORE SupportLocalisationBaseline so TryAddSingleton skips RealFileSystem.
        services.AddSingleton<IFileSystem>(mockFs);
        services.SupportLocalisationBaseline();
        var sp = services.BuildServiceProvider();

        var config = rootLayer is null
            ? null
            : WorkspaceConfiguration.Empty with { Layers = [rootLayer] };

        var reload = new SpyReloadService { LastWorkspaceConfig = config };
        var writer = new SpyFileWriter();
        var seedWriter = new LocalisationSeedFileWriter(
            sp.GetRequiredService<ICsvTranslationExporter>(),
            sp.GetRequiredService<IXmlTranslationExporter>(),
            sp.GetRequiredService<IPropertiesTranslationExporter>(),
            sp.GetRequiredService<ILanguageService>(),
            new FileHelper(mockFs));
        var handler = new InitLocalisationProjectCommandHandler(
            sp.GetRequiredService<IBaselineTranslationProvider>(),
            sp.GetRequiredService<ITranslationDatabaseFactory>(),
            sp.GetRequiredService<ILanguageService>(),
            new FileHelper(mockFs),
            reload,
            writer,
            seedWriter,
            NullLogger<InitLocalisationProjectCommandHandler>.Instance,
            lspConfig ?? new FakeLspConfigurationProvider());

        return (handler, mockFs, reload, writer);
    }

    private sealed class SpyReloadService : IModProjectReloadService
    {
        public bool LocalisationOnlyReloaded { get; private set; }
        public bool FullyReloaded { get; private set; }
        public IReadOnlyList<string>? LastAssetRoots => null;
        public WorkspaceConfiguration? LastWorkspaceConfig { get; init; }
        public IReadOnlyList<string>? LastWorkspaceRoots { get; init; }

        public Task LoadAsync(IEnumerable<string> workspaceRoots, CancellationToken ct)
        {
            return Task.CompletedTask;
        }

        public Task ReloadAsync(CancellationToken ct)
        {
            FullyReloaded = true;
            return Task.CompletedTask;
        }

        public Task ReloadLocalisationAsync(CancellationToken ct)
        {
            LocalisationOnlyReloaded = true;
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

// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Xml.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.Files.DAT.Services;
using PG.StarWarsGame.Localisation.Baseline;
using PG.StarWarsGame.Localisation.Data;
using PG.StarWarsGame.Localisation.IO.Csv;
using PG.StarWarsGame.Localisation.IO.Dat;
using PG.StarWarsGame.Localisation.IO.Properties;
using PG.StarWarsGame.Localisation.IO.Xml;
using PG.StarWarsGame.Localisation.Languages;
using PG.StarWarsGame.Localisation.Services;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Server.Commands;
using PG.StarWarsGame.LSP.Server.Localisation;
using PG.StarWarsGame.LSP.Server.Project;

namespace PG.StarWarsGame.LSP.Server.Tests.Commands;

public sealed class ImportLocalisationProjectCommandHandlerTest
{
    private const string PgprojPath = "/mod/mymod.pgproj";

    // ── feature flag ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_LocalisationFlagOff_NoOp()
    {
        var mockFs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/mod/data/text/MasterTextFile.csv"] = new("key,ENGLISH\nTEXT_A,Hello\n")
        });
        var config = FakeLspConfigurationProvider.WithFeatures(
            new FeatureFlags { Tools = new ToolsFeatureFlags { Localisation = false } });
        var (handler, reload, writer) = BuildHandler(mockFs, lspConfig: config);

        await handler.Handle(Request("Csv", "/mod/data/text", "Csv"), CancellationToken.None);

        Assert.False(reload.FullyReloaded);
        Assert.Null(writer.LastCall);
    }

    // ── validation / guard clauses ───────────────────────────────────────────

    [Theory]
    [InlineData(null, "/mod/data/text", "Csv")]
    [InlineData("Csv", null, "Csv")]
    [InlineData("Csv", "/mod/data/text", null)]
    public async Task Handle_MissingRequiredArguments_NoOp(string? sourceFormat, string? sourceDirectory,
        string? targetFormat)
    {
        var (handler, reload, writer) = BuildHandler();

        await handler.Handle(Request(sourceFormat, sourceDirectory, targetFormat), CancellationToken.None);

        Assert.False(reload.FullyReloaded);
        Assert.Null(writer.LastCall);
    }

    [Fact]
    public async Task Handle_NoPgproj_NoOp()
    {
        var (handler, reload, writer) = BuildHandler(noPgproj: true);

        await handler.Handle(Request("Csv", "/mod/data/text", "Csv"), CancellationToken.None);

        Assert.False(reload.FullyReloaded);
        Assert.Null(writer.LastCall);
    }

    [Fact]
    public async Task Handle_SourceDirectoryDoesNotExist_NoOp()
    {
        var (handler, reload, writer) = BuildHandler();

        await handler.Handle(Request("Csv", "/mod/does-not-exist", "Csv"), CancellationToken.None);

        Assert.False(reload.FullyReloaded);
        Assert.Null(writer.LastCall);
    }

    [Fact]
    public async Task Handle_SourceDirectoryHasNoMatchingExtensionFiles_NoOp()
    {
        var mockFs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/mod/data/text/readme.txt"] = new("not a csv")
        });
        var (handler, reload, writer) = BuildHandler(mockFs);

        await handler.Handle(Request("Csv", "/mod/data/text", "Csv"), CancellationToken.None);

        Assert.False(reload.FullyReloaded);
        Assert.Null(writer.LastCall);
    }

    // ── same format: pure registration ───────────────────────────────────────

    [Fact]
    public async Task Handle_SameFormat_DoesNotModifySourceFiles()
    {
        const string original = "key,ENGLISH\nTEXT_A,Hello\n";
        var mockFs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/mod/data/text/MasterTextFile.csv"] = new(original)
        });
        var (handler, _, _) = BuildHandler(mockFs);

        await handler.Handle(Request("Csv", "/mod/data/text", "Csv"), CancellationToken.None);

        Assert.Equal(original, mockFs.File.ReadAllText("/mod/data/text/MasterTextFile.csv"));
        Assert.Single(mockFs.AllFiles); // no new file created
    }

    [Fact]
    public async Task Handle_SameFormat_RegistersRelativeDirectoryWithPgproj()
    {
        var mockFs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/mod/data/text/MasterTextFile.csv"] = new("key,ENGLISH\nTEXT_A,Hello\n")
        });
        var (handler, _, writer) = BuildHandler(mockFs);

        await handler.Handle(Request("Csv", "/mod/data/text", "Csv"), CancellationToken.None);

        Assert.NotNull(writer.LastCall);
        Assert.Equal(PgprojPath, writer.LastCall!.Value.PgprojPath);
        Assert.Equal("CSV", writer.LastCall!.Value.Type);
        Assert.Equal("data/text", writer.LastCall!.Value.Directory);
    }

    [Fact]
    public async Task Handle_SameFormat_TriggersFullReload()
    {
        var mockFs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/mod/data/text/MasterTextFile.csv"] = new("key,ENGLISH\nTEXT_A,Hello\n")
        });
        var (handler, reload, _) = BuildHandler(mockFs);

        await handler.Handle(Request("Csv", "/mod/data/text", "Csv"), CancellationToken.None);

        Assert.True(reload.FullyReloaded);
    }

    [Fact]
    public async Task Handle_SameFormat_SourceOutsidePgprojTree_StillComputesRelativePath()
    {
        var mockFs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/elsewhere/text/MasterTextFile.csv"] = new("key,ENGLISH\nTEXT_A,Hello\n")
        });
        var (handler, _, writer) = BuildHandler(mockFs);

        await handler.Handle(Request("Csv", "/elsewhere/text", "Csv"), CancellationToken.None);

        Assert.NotNull(writer.LastCall);
        Assert.Equal("../elsewhere/text", writer.LastCall!.Value.Directory);
    }

    // ── different format: conversion ─────────────────────────────────────────

    [Fact]
    public async Task Handle_DifferentFormat_ConvertsSourceKeysIntoTargetFile()
    {
        var mockFs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/mod/data/text/MasterTextFile.csv"] = new("key,ENGLISH\nTEXT_CONVERTED,Hello World\n")
        });
        var (handler, _, _) = BuildHandler(mockFs);

        await handler.Handle(
            Request("Csv", "/mod/data/text", "Xml", "data/text2"), CancellationToken.None);

        var xdoc = XDocument.Parse(mockFs.File.ReadAllText("/mod/data/text2/MasterTextFile.xml"));
        Assert.Contains(xdoc.Root!.Elements(), e =>
            e.Attribute("key")?.Value == "TEXT_CONVERTED");
    }

    [Fact]
    public async Task Handle_DifferentFormat_DoesNotModifySourceFiles()
    {
        const string original = "key,ENGLISH\nTEXT_A,Hello\n";
        var mockFs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/mod/data/text/MasterTextFile.csv"] = new(original)
        });
        var (handler, _, _) = BuildHandler(mockFs);

        await handler.Handle(
            Request("Csv", "/mod/data/text", "Xml", "data/text2"), CancellationToken.None);

        Assert.Equal(original, mockFs.File.ReadAllText("/mod/data/text/MasterTextFile.csv"));
    }

    [Fact]
    public async Task Handle_DifferentFormat_RegistersTargetDirectoryAsGiven()
    {
        var mockFs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/mod/data/text/MasterTextFile.csv"] = new("key,ENGLISH\nTEXT_A,Hello\n")
        });
        var (handler, _, writer) = BuildHandler(mockFs);

        await handler.Handle(
            Request("Csv", "/mod/data/text", "Xml", "data/text2"), CancellationToken.None);

        Assert.NotNull(writer.LastCall);
        Assert.Equal("XML", writer.LastCall!.Value.Type);
        Assert.Equal("data/text2", writer.LastCall!.Value.Directory);
    }

    [Fact]
    public async Task Handle_DifferentFormat_MissingTargetDirectory_NoOp()
    {
        var mockFs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/mod/data/text/MasterTextFile.csv"] = new("key,ENGLISH\nTEXT_A,Hello\n")
        });
        var (handler, reload, writer) = BuildHandler(mockFs);

        await handler.Handle(Request("Csv", "/mod/data/text", "Xml"), CancellationToken.None);

        Assert.False(reload.FullyReloaded);
        Assert.Null(writer.LastCall);
    }

    [Fact]
    public async Task Handle_DifferentFormat_TargetAlreadyExists_DoesNotOverwrite()
    {
        var mockFs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/mod/data/text/MasterTextFile.csv"] = new("key,ENGLISH\nTEXT_A,Hello\n"),
            ["/mod/data/text2/MasterTextFile.xml"] = new("EXISTING")
        });
        var (handler, reload, writer) = BuildHandler(mockFs);

        await handler.Handle(
            Request("Csv", "/mod/data/text", "Xml", "data/text2"), CancellationToken.None);

        Assert.Equal("EXISTING", mockFs.File.ReadAllText("/mod/data/text2/MasterTextFile.xml"));
        Assert.False(reload.FullyReloaded);
        Assert.Null(writer.LastCall);
    }

    [Fact]
    public async Task Handle_DifferentFormat_UnsupportedTargetFormat_NoOp()
    {
        var mockFs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/mod/data/text/MasterTextFile.csv"] = new("key,ENGLISH\nTEXT_A,Hello\n")
        });
        var (handler, reload, writer) = BuildHandler(mockFs);

        await handler.Handle(
            Request("Csv", "/mod/data/text", "Dat", "data/text2"), CancellationToken.None);

        Assert.False(reload.FullyReloaded);
        Assert.Null(writer.LastCall);
        Assert.False(mockFs.Directory.Exists("/mod/data/text2"));
    }

    [Fact]
    public async Task Handle_DifferentFormat_MultipleSourceFiles_AllKeysMerged()
    {
        var mockFs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/mod/data/text/a.csv"] = new("key,ENGLISH\nTEXT_A,From A\n"),
            ["/mod/data/text/b.csv"] = new("key,ENGLISH\nTEXT_B,From B\n")
        });
        var (handler, _, _) = BuildHandler(mockFs);

        await handler.Handle(
            Request("Csv", "/mod/data/text", "Xml", "data/text2"), CancellationToken.None);

        var xdoc = XDocument.Parse(mockFs.File.ReadAllText("/mod/data/text2/MasterTextFile.xml"));
        var keys = xdoc.Root!.Elements().Select(e => e.Attribute("key")?.Value).ToList();
        Assert.Contains("TEXT_A", keys);
        Assert.Contains("TEXT_B", keys);
    }

    // ── DAT source ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_DatSource_SameFormat_RegistersWithoutReadingContent()
    {
        // Same-format registration never parses file content, so a placeholder byte sequence
        // (not a real DAT binary) proves this path doesn't even try to read it.
        var mockFs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/mod/data/text/MasterTextFile_ENGLISH.dat"] = new(new byte[] { 1, 2, 3 })
        });
        var (handler, _, writer) = BuildHandler(mockFs);

        await handler.Handle(Request("Dat", "/mod/data/text", "Dat"), CancellationToken.None);

        Assert.NotNull(writer.LastCall);
        Assert.Equal("DAT", writer.LastCall!.Value.Type);
        Assert.Equal("data/text", writer.LastCall!.Value.Directory);
    }

    [Fact]
    public async Task Handle_DatSource_ConvertToCsv_ImportsRealDatContent()
    {
        var mockFs = new MockFileSystem();
        var sp = BuildDatFixtureServiceProvider(mockFs);
        var english = sp.GetRequiredService<ILanguageService>().Default;
        WriteDatFixture(mockFs, sp, "/mod/data/text/MasterTextFile_ENGLISH.dat", english,
            "TEXT_FROM_DAT", "Hello From DAT");

        var (handler, _, _) = BuildHandler(mockFs);

        await handler.Handle(
            Request("Dat", "/mod/data/text", "Csv", "data/text2"), CancellationToken.None);

        var csv = mockFs.File.ReadAllText("/mod/data/text2/MasterTextFile.csv");
        Assert.Contains("TEXT_FROM_DAT", csv);
        Assert.Contains("Hello From DAT", csv);
    }

    [Fact]
    public async Task Handle_DatSource_UnresolvableLanguageFileName_SkippedWithoutFailingOthers()
    {
        var mockFs = new MockFileSystem();
        var sp = BuildDatFixtureServiceProvider(mockFs);
        var english = sp.GetRequiredService<ILanguageService>().Default;
        WriteDatFixture(mockFs, sp, "/mod/data/text/MasterTextFile_ENGLISH.dat", english,
            "TEXT_GOOD", "Good Value");
        // No language matches this suffix - must be skipped, not fatal to the whole import.
        mockFs.AddFile("/mod/data/text/MasterTextFile_NOTALANGUAGE.dat", new MockFileData(new byte[] { 9, 9, 9 }));

        var (handler, reload, _) = BuildHandler(mockFs);

        await handler.Handle(
            Request("Dat", "/mod/data/text", "Csv", "data/text2"), CancellationToken.None);

        var csv = mockFs.File.ReadAllText("/mod/data/text2/MasterTextFile.csv");
        Assert.Contains("TEXT_GOOD", csv);
        Assert.True(reload.FullyReloaded);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static IServiceProvider BuildDatFixtureServiceProvider(MockFileSystem mockFs)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IFileSystem>(mockFs);
        services.SupportLocalisationBaseline();
        return services.BuildServiceProvider();
    }

    private static void WriteDatFixture(
        MockFileSystem mockFs, IServiceProvider sp, string path,
        IAlamoLanguageDefinition language, string key, string value)
    {
        var factory = sp.GetRequiredService<ITranslationDatabaseFactory>();
        var exporter = sp.GetRequiredService<IDatTranslationExporter>();
        var datFileService = sp.GetRequiredService<IDatFileService>();

        var db = factory.CreateKeyed([language]);
        db.SetTranslation(key, language, value);
        var model = exporter.Export(db, language);

        mockFs.Directory.CreateDirectory(mockFs.Path.GetDirectoryName(path)!);
        using var stream = mockFs.File.Create(path);
        datFileService.CreateDatFile(stream, model, model.KeySortOrder);
    }

    private static ExecuteCommandParams Request(
        string? sourceFormat, string? sourceDirectory, string? targetFormat, string? targetDirectory = null)
    {
        var obj = new JObject();
        if (sourceFormat is not null) obj["sourceFormat"] = sourceFormat;
        if (sourceDirectory is not null) obj["sourceDirectory"] = sourceDirectory;
        if (targetFormat is not null) obj["targetFormat"] = targetFormat;
        if (targetDirectory is not null) obj["targetDirectory"] = targetDirectory;

        return new ExecuteCommandParams
        {
            Command = ImportLocalisationProjectCommandHandler.CommandName,
            Arguments = new JArray(obj)
        };
    }

    private static ProjectLayer ConfiguredRootLayer()
    {
        return new ProjectLayer(0, "Root", [], [], [], [], null, PgprojPath);
    }

    private static (ImportLocalisationProjectCommandHandler handler, SpyReloadService reload, SpyFileWriter writer)
        BuildHandler(MockFileSystem? fs = null, bool noPgproj = false, ILspConfigurationProvider? lspConfig = null)
    {
        var mockFs = fs ?? new MockFileSystem();

        var services = new ServiceCollection();
        services.AddSingleton<IFileSystem>(mockFs);
        services.SupportLocalisationBaseline();
        var sp = services.BuildServiceProvider();

        var layer = noPgproj ? null : ConfiguredRootLayer();
        var config = layer is null ? null : WorkspaceConfiguration.Empty with { Layers = [layer] };

        var reload = new SpyReloadService { LastWorkspaceConfig = config };
        var writer = new SpyFileWriter();
        var seedWriter = new LocalisationSeedFileWriter(
            sp.GetRequiredService<ICsvTranslationExporter>(),
            sp.GetRequiredService<IXmlTranslationExporter>(),
            sp.GetRequiredService<IPropertiesTranslationExporter>(),
            sp.GetRequiredService<ILanguageService>(),
            new FileHelper(mockFs));

        var handler = new ImportLocalisationProjectCommandHandler(
            sp.GetRequiredService<ICsvTranslationImporter>(),
            sp.GetRequiredService<IXmlTranslationImporter>(),
            sp.GetRequiredService<IPropertiesTranslationImporter>(),
            sp.GetRequiredService<IDatTranslationImporter>(),
            sp.GetRequiredService<IDatFileService>(),
            sp.GetRequiredService<ITranslationDatabaseFactory>(),
            sp.GetRequiredService<ILanguageService>(),
            seedWriter,
            new FileHelper(mockFs),
            reload,
            writer,
            NullLogger<ImportLocalisationProjectCommandHandler>.Instance,
            lspConfig ?? new FakeLspConfigurationProvider());

        return (handler, reload, writer);
    }

    private sealed class SpyReloadService : IModProjectReloadService
    {
        public bool FullyReloaded { get; private set; }
        public IReadOnlyList<string>? LastAssetRoots => null;
        public WorkspaceConfiguration? LastWorkspaceConfig { get; init; }
        public IReadOnlyList<string>? LastWorkspaceRoots => null;

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
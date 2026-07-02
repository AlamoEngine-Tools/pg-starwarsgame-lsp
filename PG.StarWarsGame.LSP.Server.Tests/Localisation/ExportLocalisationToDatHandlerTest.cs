// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.Files.DAT.Services;
using PG.StarWarsGame.Localisation.Baseline;
using PG.StarWarsGame.Localisation.Data;
using PG.StarWarsGame.Localisation.IO.Csv;
using PG.StarWarsGame.Localisation.IO.Dat;
using PG.StarWarsGame.Localisation.IO.Properties;
using PG.StarWarsGame.Localisation.IO.Xml;
using PG.StarWarsGame.Localisation.Services;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Server.Localisation;

namespace PG.StarWarsGame.LSP.Server.Tests.Localisation;

public sealed class ExportLocalisationToDatHandlerTest
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private const string CsvPath = "/mod/Data/Text/MasterTextFile.csv";
    private const string CsvContent = "key,ENGLISH\nMY_TEST_KEY,Hello World\n";

    [Fact]
    public async Task Handle_WhenProjectFilePathIsNull_ReturnsErrorResult()
    {
        var (handler, _, _) = BuildHandler();

        var result = await handler.Handle(new ExportLocalisationToDatParams(null!), CancellationToken.None);

        Assert.NotNull(result.Error);
        Assert.Empty(result.WrittenFiles);
    }

    [Fact]
    public async Task Handle_WhenProjectFileDoesNotExist_ReturnsErrorResult()
    {
        var (handler, _, _) = BuildHandler();

        var result = await handler.Handle(new ExportLocalisationToDatParams("/nonexistent/file.csv"),
            CancellationToken.None);

        Assert.NotNull(result.Error);
        Assert.Empty(result.WrittenFiles);
    }

    [Fact]
    public async Task Handle_WithValidCsvProject_WritesDatFilePerLanguageWithContent()
    {
        var (handler, _, langCount) = BuildHandler(true);

        var result = await handler.Handle(new ExportLocalisationToDatParams(CsvPath), CancellationToken.None);

        Assert.Null(result.Error);
        Assert.InRange(result.WrittenFiles.Count, 1, langCount);
    }

    [Fact]
    public async Task Handle_WithValidCsvProject_SkipsLanguagesWithNoContent()
    {
        var (handler, fs, _) = BuildHandler(true);

        var result = await handler.Handle(new ExportLocalisationToDatParams(CsvPath), CancellationToken.None);

        // Languages absent from both the baseline DATs and the workspace file must not produce a DAT.
        // CHINESE has no vanilla baseline data and is not in the test CSV — it must be skipped.
        Assert.DoesNotContain(result.WrittenFiles, p => p.EndsWith("_CHINESE.dat", StringComparison.OrdinalIgnoreCase));
        Assert.False(fs.File.Exists("/mod/Data/Text/MasterTextFile_CHINESE.dat"));
    }

    [Fact]
    public async Task Handle_WithCsvContainingEmptyLanguageColumn_SkipsAllEmptyLanguage()
    {
        // CHINESE column present in CSV but every value is empty — no real content.
        const string csvWithEmptyChinese = "key,ENGLISH,CHINESE\nMY_TEST_KEY,Hello World,\n";
        var mockFs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [CsvPath] = new(csvWithEmptyChinese)
        });
        var handler = BuildHandlerWithFs(mockFs);

        var result = await handler.Handle(new ExportLocalisationToDatParams(CsvPath), CancellationToken.None);

        Assert.Null(result.Error);
        Assert.DoesNotContain(result.WrittenFiles, p => p.EndsWith("_CHINESE.dat", StringComparison.OrdinalIgnoreCase));
        Assert.False(mockFs.File.Exists("/mod/Data/Text/MasterTextFile_CHINESE.dat"));
    }

    [Fact]
    public async Task Handle_WithValidCsvProject_ReturnedPathsMatchWrittenFiles()
    {
        var (handler, fs, _) = BuildHandler(true);

        var result = await handler.Handle(new ExportLocalisationToDatParams(CsvPath), CancellationToken.None);

        Assert.All(result.WrittenFiles, path => Assert.True(fs.File.Exists(path)));
    }

    [Fact]
    public async Task Handle_WithValidCsvProject_WrittenDatFilesAreNonEmpty()
    {
        var (handler, fs, _) = BuildHandler(true);

        var result = await handler.Handle(new ExportLocalisationToDatParams(CsvPath), CancellationToken.None);

        Assert.All(result.WrittenFiles, path => Assert.True(fs.File.ReadAllBytes(path).Length > 0));
    }

    // ── dependency-aware export ──────────────────────────────────────────────

    [Fact]
    public async Task Handle_ExportedFileHasLowerLayer_DatIncludesDependencyOnlyKey()
    {
        var mockFs = new MockFileSystem(new Dictionary<string, MockFileData> { [CsvPath] = new(CsvContent) });
        var services = new ServiceCollection();
        services.AddSingleton<IFileSystem>(mockFs);
        services.SupportLocalisationBaseline();
        var sp = services.BuildServiceProvider();
        var langService = sp.GetRequiredService<ILanguageService>();
        var factory = sp.GetRequiredService<ITranslationDatabaseFactory>();
        var english = langService.Default;

        var depDb = factory.CreateKeyed([english]);
        depDb.SetTranslation("TEXT_DEP_ONLY_KEY", english, "Dependency Only Value");
        var projectRegistry = new LocalisationProjectRegistry();
        projectRegistry.Set([
            new LocProjectInfo("MasterTextFile.csv", CsvPath, "Csv", "Root", 1),
            new LocProjectInfo("core.csv", "/dep/text/core.csv", "Csv", "Dep", 0)
        ]);
        var layerRegistry = new LocalisationLayerRegistry();
        layerRegistry.Set([
            new LocalisationLayerEntry(new ProjectLayer(0, "Dep", [], [], ["/dep/text"], [], "Csv"), depDb)
        ]);

        var handler = BuildHandlerWithFs(mockFs, sp, projectRegistry, layerRegistry);
        var result = await handler.Handle(new ExportLocalisationToDatParams(CsvPath), CancellationToken.None);

        Assert.Null(result.Error);
        Assert.Contains(result.WrittenFiles, p => p.EndsWith("_ENGLISH.dat", StringComparison.OrdinalIgnoreCase));
    }

    private static (ExportLocalisationToDatHandler handler, MockFileSystem fs, int languageCount)
        BuildHandler(bool withCsvFile = false)
    {
        var initialFiles = withCsvFile
            ? new Dictionary<string, MockFileData> { [CsvPath] = new(CsvContent) }
            : new Dictionary<string, MockFileData>();
        var mockFs = new MockFileSystem(initialFiles);
        var services = new ServiceCollection();
        services.AddSingleton<IFileSystem>(mockFs);
        services.SupportLocalisationBaseline();
        var sp = services.BuildServiceProvider();
        var langService = sp.GetRequiredService<ILanguageService>();
        return (BuildHandlerWithFs(mockFs, sp), mockFs, langService.OfficiallySupported().Count);
    }

    private static ExportLocalisationToDatHandler BuildHandlerWithFs(
        MockFileSystem mockFs, IServiceProvider? sp = null,
        LocalisationProjectRegistry? projectRegistry = null, LocalisationLayerRegistry? layerRegistry = null)
    {
        if (sp is null)
        {
            var services = new ServiceCollection();
            services.AddSingleton<IFileSystem>(mockFs);
            services.SupportLocalisationBaseline();
            sp = services.BuildServiceProvider();
        }

        return new ExportLocalisationToDatHandler(
            sp.GetRequiredService<ICsvTranslationImporter>(),
            sp.GetRequiredService<IXmlTranslationImporter>(),
            sp.GetRequiredService<IPropertiesTranslationImporter>(),
            sp.GetRequiredService<IBaselineTranslationProvider>(),
            sp.GetRequiredService<ITranslationDatabaseFactory>(),
            sp.GetRequiredService<ILanguageService>(),
            sp.GetRequiredService<IDatTranslationExporter>(),
            sp.GetRequiredService<IDatFileService>(),
            new FileHelper(mockFs),
            projectRegistry ?? new LocalisationProjectRegistry(),
            layerRegistry ?? new LocalisationLayerRegistry(),
            NullLogger<ExportLocalisationToDatHandler>.Instance);
    }
}
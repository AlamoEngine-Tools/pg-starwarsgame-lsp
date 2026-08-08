// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Xml.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.Localisation.Baseline;
using PG.StarWarsGame.Localisation.Data;
using PG.StarWarsGame.Localisation.IO.Csv;
using PG.StarWarsGame.Localisation.IO.Dat;
using PG.StarWarsGame.Localisation.IO.Properties;
using PG.StarWarsGame.Localisation.IO.Xml;
using PG.StarWarsGame.Localisation.Services;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Server.Localisation;
using PG.StarWarsGame.LSP.Server.Project;

namespace PG.StarWarsGame.LSP.Server.Tests.Localisation;

public sealed class ConvertLocalisationFormatHandlerTest
{
    private const string PgprojPath = "/mod/mymod.pgproj";
    private const string CsvPath = "/mod/data/text/MasterTextFile.csv";
    private const string CsvContent = "key,ENGLISH\nTEXT_A,Hello\nTEXT_B,World\n";

    // ── the conversion itself ────────────────────────────────────────────────

    [Fact]
    public async Task Convert_CsvToXml_WritesBesideTheOriginalWithTheSameStem()
    {
        var (handler, fs, _, _) = Build();

        var result = await handler.Handle(Request(CsvPath, "Xml"), CancellationToken.None);

        Assert.Null(result.Error);
        Assert.Equal("/mod/data/text/MasterTextFile.xml", result.WrittenPath);
        Assert.True(fs.File.Exists("/mod/data/text/MasterTextFile.xml"));
    }

    // ── converting to a single-language format ───────────────────────────────

    /// <summary>
    ///     NLS holds one language per file, so a multi-language source becomes one file per language,
    ///     each named for the language it holds.
    ///     <para>
    ///         It used to write a single <c>MasterTextFile.properties</c> containing whichever language
    ///         happened to be the service default, silently discarding the rest - and the success
    ///         message said only "wrote X. The original file was kept."
    ///     </para>
    /// </summary>
    [Fact]
    public async Task Convert_MultiLanguageCsvToNls_WritesOneFilePerLanguage()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [CsvPath] = new("key,ENGLISH,GERMAN\nTEXT_A,Hello,Hallo\nTEXT_B,World,Welt\n"),
            [PgprojPath] = new("{}")
        });
        var (handler, _, _, _) = Build(fs);

        var result = await handler.Handle(Request(CsvPath, "Nls"), CancellationToken.None);

        Assert.Null(result.Error);
        Assert.Equal(
            ["/mod/data/text/mastertextfile_english.properties", "/mod/data/text/mastertextfile_german.properties"],
            result.WrittenPaths.OrderBy(p => p, StringComparer.Ordinal));
        Assert.Contains("Hallo", fs.File.ReadAllText("/mod/data/text/mastertextfile_german.properties"));
        Assert.Contains("Hello", fs.File.ReadAllText("/mod/data/text/mastertextfile_english.properties"));
    }

    /// <summary>A language with no content anywhere produces no file at all.</summary>
    [Fact]
    public async Task Convert_SingleLanguageCsvToNls_WritesOnlyThatLanguage()
    {
        var (handler, _, _, _) = Build();

        var result = await handler.Handle(Request(CsvPath, "Nls"), CancellationToken.None);

        Assert.Null(result.Error);
        Assert.Equal("/mod/data/text/mastertextfile_english.properties", Assert.Single(result.WrittenPaths));
    }

    // The point of "convert, keep the old file": the original is a fallback until the user is happy
    // with the result, so it has to survive untouched.
    [Fact]
    public async Task Convert_LeavesTheOriginalFileExactlyAsItWas()
    {
        var (handler, fs, _, _) = Build();

        await handler.Handle(Request(CsvPath, "Xml"), CancellationToken.None);

        Assert.Equal(CsvContent, fs.File.ReadAllText(CsvPath));
    }

    [Fact]
    public async Task Convert_CarriesEveryEntryAcross()
    {
        var (handler, fs, _, _) = Build();

        await handler.Handle(Request(CsvPath, "Xml"), CancellationToken.None);

        var written = fs.File.ReadAllText("/mod/data/text/MasterTextFile.xml");
        Assert.Contains("TEXT_A", written, StringComparison.Ordinal);
        Assert.Contains("TEXT_B", written, StringComparison.Ordinal);
        Assert.Contains("Hello", written, StringComparison.Ordinal);
    }

    // A conversion is a re-encoding of one file, not a merge. Seeding the game's 19k-row baseline in
    // (which the DAT export deliberately does, because the engine needs a complete file) would turn
    // a fifty-row mod override into something unreviewable.
    [Fact]
    public async Task Convert_DoesNotSeedTheGameBaselineIntoTheResult()
    {
        var (handler, fs, _, _) = Build();

        await handler.Handle(Request(CsvPath, "Xml"), CancellationToken.None);

        var xdoc = XDocument.Parse(fs.File.ReadAllText("/mod/data/text/MasterTextFile.xml"));
        Assert.Equal(2, xdoc.Root!.Elements().Count());
    }

    // Credits repeat their key by design; a keyed database would collapse them into one row and
    // silently shorten the crawl.
    [Fact]
    public async Task Convert_CreditsFile_KeepsDuplicateKeysAndOrder()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/mod/data/text/creditstext_english.csv"] =
                new("key,ENGLISH\nHEADER,Lead\nCENTER,Alice\nHEADER,Art\nCENTER,Bob\n"),
            [PgprojPath] = new("{}")
        });
        var registry = new LocalisationProjectRegistry();
        registry.Set([
            new LocProjectInfo("creditstext_english.csv", "/mod/data/text/creditstext_english.csv",
                "Csv", "Root", 1, LocCategory.Credits)
        ]);
        var (handler, _, _, _) = Build(fs, registry);

        var result = await handler.Handle(
            Request("/mod/data/text/creditstext_english.csv", "Xml"), CancellationToken.None);

        Assert.Null(result.Error);
        var xdoc = XDocument.Parse(fs.File.ReadAllText("/mod/data/text/creditstext_english.xml"));
        Assert.Equal(4, xdoc.Root!.Elements().Count());
    }

    // ── the project's declared format ────────────────────────────────────────

    [Fact]
    public async Task Convert_RepointsThePgprojAtTheNewFormat()
    {
        var (handler, _, _, writer) = Build();

        var result = await handler.Handle(Request(CsvPath, "Xml"), CancellationToken.None);

        Assert.True(result.ProjectFormatChanged);
        Assert.NotNull(writer.LastCall);
        Assert.Equal("XML", writer.LastCall!.Value.Type);
        Assert.Equal(PgprojPath, writer.LastCall!.Value.PgprojPath);
    }

    [Fact]
    public async Task Convert_KeepsTheDeclaredDirectory()
    {
        var (handler, _, _, writer) = Build();

        await handler.Handle(Request(CsvPath, "Xml"), CancellationToken.None);

        Assert.Equal("data/text", writer.LastCall!.Value.Directory);
    }

    [Fact]
    public async Task Convert_ReloadsSoTheNewFormatIsPickedUp()
    {
        var (handler, _, reload, _) = Build();

        await handler.Handle(Request(CsvPath, "Xml"), CancellationToken.None);

        Assert.True(reload.FullyReloaded);
    }

    // Converting a file that is not in the project's declared format - a credits DAT sitting in a
    // CSV project - must not repoint the whole project at the target.
    [Fact]
    public async Task Convert_FileNotInTheProjectsFormat_LeavesThePgprojAlone()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/mod/data/text/other.properties"] = new("TEXT_A=Hello\n"),
            [PgprojPath] = new("{}")
        });
        var (handler, _, _, writer) = Build(fs);

        var result = await handler.Handle(
            Request("/mod/data/text/other.properties", "Xml"), CancellationToken.None);

        Assert.Null(result.Error);
        Assert.False(result.ProjectFormatChanged);
        Assert.Null(writer.LastCall);
    }

    // Changing the project format orphans everything still in the old one. Saying nothing is how a
    // user discovers half their text is gone only after a reload.
    [Fact]
    public async Task Convert_ReportsHowManyFilesAreLeftBehindInTheOldFormat()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [CsvPath] = new(CsvContent),
            ["/mod/data/text/creditstext_english.csv"] = new("key,ENGLISH\nHEADER,Lead\n"),
            [PgprojPath] = new("{}")
        });
        var registry = new LocalisationProjectRegistry();
        registry.Set([
            new LocProjectInfo("MasterTextFile.csv", CsvPath, "Csv", "Root", 1),
            new LocProjectInfo("creditstext_english.csv", "/mod/data/text/creditstext_english.csv",
                "Csv", "Root", 1, LocCategory.Credits)
        ]);
        var (handler, _, _, _) = Build(fs, registry);

        var result = await handler.Handle(Request(CsvPath, "Xml"), CancellationToken.None);

        Assert.True(result.ProjectFormatChanged);
        Assert.Equal(1, result.OtherFilesInOldFormat);
    }

    // ── refusals ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Convert_FlagOff_Refuses()
    {
        var config = FakeLspConfigurationProvider.WithFeatures(
            new FeatureFlags { Tools = new ToolsFeatureFlags { Localisation = false } });
        var (handler, fs, _, _) = Build(config: config);

        var result = await handler.Handle(Request(CsvPath, "Xml"), CancellationToken.None);

        Assert.NotNull(result.Error);
        Assert.False(fs.File.Exists("/mod/data/text/MasterTextFile.xml"));
    }

    [Fact]
    public async Task Convert_MissingFile_Refuses()
    {
        var (handler, _, _, _) = Build();

        var result = await handler.Handle(
            Request("/mod/data/text/nope.csv", "Xml"), CancellationToken.None);

        Assert.Contains("nope.csv", result.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Convert_ToTheFormatItIsAlreadyIn_Refuses()
    {
        var (handler, _, _, writer) = Build();

        var result = await handler.Handle(Request(CsvPath, "Csv"), CancellationToken.None);

        Assert.NotNull(result.Error);
        Assert.Null(writer.LastCall);
    }

    // DAT stays with the export action - one code path writes DAT files, so the crawl's sort order
    // is decided in exactly one place.
    [Fact]
    public async Task Convert_ToDat_RefusesAndPointsAtTheExportAction()
    {
        var (handler, _, _, _) = Build();

        var result = await handler.Handle(Request(CsvPath, "Dat"), CancellationToken.None);

        Assert.Contains("export", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Convert_UnknownTargetFormat_Refuses()
    {
        var (handler, _, _, _) = Build();

        var result = await handler.Handle(Request(CsvPath, "Yaml"), CancellationToken.None);

        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task Convert_TargetAlreadyExists_RefusesWithoutOverwriting()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [CsvPath] = new(CsvContent),
            ["/mod/data/text/MasterTextFile.xml"] = new("<EXISTING/>"),
            [PgprojPath] = new("{}")
        });
        var (handler, _, _, _) = Build(fs);

        var result = await handler.Handle(Request(CsvPath, "Xml"), CancellationToken.None);

        Assert.NotNull(result.Error);
        Assert.Equal("<EXISTING/>", fs.File.ReadAllText("/mod/data/text/MasterTextFile.xml"));
    }

    [Fact]
    public async Task Convert_UnreadableSource_RefusesWithoutWriting()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/mod/data/text/MasterTextFile.xml"] = new("<not well formed"),
            [PgprojPath] = new("{}")
        });
        var (handler, _, _, _) = Build(fs);

        var result = await handler.Handle(
            Request("/mod/data/text/MasterTextFile.xml", "Csv"), CancellationToken.None);

        Assert.NotNull(result.Error);
        Assert.False(fs.File.Exists("/mod/data/text/MasterTextFile.csv"));
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static ConvertLocalisationFormatParams Request(string path, string targetFormat)
    {
        return new ConvertLocalisationFormatParams(path, targetFormat);
    }

    private static (ConvertLocalisationFormatHandler Handler, MockFileSystem Fs,
        SpyReloadService Reload, SpyFileWriter Writer) Build(
            MockFileSystem? initialFs = null, LocalisationProjectRegistry? registry = null,
            ILspConfigurationProvider? config = null, string declaredFormat = "Csv")
    {
        var fs = initialFs ?? new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [CsvPath] = new(CsvContent),
            [PgprojPath] = new("{}")
        });

        var services = new ServiceCollection();
        services.AddSingleton<IFileSystem>(fs);
        services.SupportLocalisationBaseline();
        var sp = services.BuildServiceProvider();

        var layer = new ProjectLayer(0, "Root", [], [], ["/mod/data/text"], [], declaredFormat, PgprojPath);
        var reload = new SpyReloadService
        {
            LastWorkspaceConfig = WorkspaceConfiguration.Empty with { Layers = [layer] }
        };
        var writer = new SpyFileWriter();

        var handler = new ConvertLocalisationFormatHandler(
            sp.GetRequiredService<ICsvTranslationImporter>(),
            sp.GetRequiredService<IXmlTranslationImporter>(),
            sp.GetRequiredService<IPropertiesTranslationImporter>(),
            sp.GetRequiredService<IDatTranslationImporter>(),
            sp.GetRequiredService<PG.StarWarsGame.Files.DAT.Services.IDatFileService>(),
            sp.GetRequiredService<ITranslationDatabaseFactory>(),
            sp.GetRequiredService<ILanguageService>(),
            new LocalisationFormatConverter(
                sp.GetRequiredService<ICsvTranslationExporter>(),
                sp.GetRequiredService<IXmlTranslationExporter>(),
                sp.GetRequiredService<IPropertiesTranslationExporter>(),
                sp.GetRequiredService<ILanguageService>(),
                new FileHelper(fs),
                config ?? new FakeLspConfigurationProvider()),
            new FileHelper(fs),
            registry ?? DefaultRegistry(),
            reload,
            writer,
            NullLogger<ConvertLocalisationFormatHandler>.Instance,
            config ?? new FakeLspConfigurationProvider());

        return (handler, fs, reload, writer);
    }

    private static LocalisationProjectRegistry DefaultRegistry()
    {
        var registry = new LocalisationProjectRegistry();
        registry.Set([new LocProjectInfo("MasterTextFile.csv", CsvPath, "Csv", "Root", 1)]);
        return registry;
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

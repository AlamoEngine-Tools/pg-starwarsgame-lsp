// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.Localisation.Baseline;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Server.Localisation;
using PG.StarWarsGame.LSP.Server.Localisation.Rows;
using PG.StarWarsGame.LSP.Server.Project;
using PG.StarWarsGame.LSP.Core.Workspace;

namespace PG.StarWarsGame.LSP.Server.Tests.Localisation;

/// <summary>
///     Adding a language to a single-language project means creating a sibling file, since the format
///     cannot take another column. Before this the editor greyed the action out and nothing in the
///     extension could create a localisation file at all.
/// </summary>
public sealed class CreateLocalisationLanguageFileHandlerTest
{
    private const string EnglishNls = "/mod/data/text/mastertextfile_english.properties";
    private const string NlsContent = "TEXT_A=Hello\nTEXT_B=World\n";

    [Fact]
    public async Task Create_NamesTheSiblingForTheNewLanguage()
    {
        var (handler, fs) = Build();

        var result = await handler.Handle(Request(EnglishNls, "GERMAN"), CancellationToken.None);

        Assert.Null(result.Error);
        Assert.Equal("/mod/data/text/mastertextfile_german.properties", result.WrittenPath);
        Assert.True(fs.File.Exists("/mod/data/text/mastertextfile_german.properties"));
    }

    /// <summary>
    ///     The new file is a worklist: every key from the source, no values. Copying the source's text
    ///     across would make an untranslated key indistinguishable from a finished one.
    /// </summary>
    [Fact]
    public async Task Create_SeedsEveryKeyWithAnEmptyValue()
    {
        var (handler, fs) = Build();

        await handler.Handle(Request(EnglishNls, "GERMAN"), CancellationToken.None);

        var written = fs.File.ReadAllText("/mod/data/text/mastertextfile_german.properties");
        Assert.Contains("TEXT_A=", written, StringComparison.Ordinal);
        Assert.Contains("TEXT_B=", written, StringComparison.Ordinal);
        Assert.DoesNotContain("Hello", written, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Create_LeavesTheSourceFileUntouched()
    {
        var (handler, fs) = Build();

        await handler.Handle(Request(EnglishNls, "GERMAN"), CancellationToken.None);

        Assert.Equal(NlsContent, fs.File.ReadAllText(EnglishNls));
    }

    [Fact]
    public async Task Create_LanguageAlreadyHasAFile_IsRefusedWithoutOverwriting()
    {
        var (handler, fs) = Build(new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [EnglishNls] = new(NlsContent),
            ["/mod/data/text/mastertextfile_german.properties"] = new("TEXT_A=Vorhanden\n")
        }));

        var result = await handler.Handle(Request(EnglishNls, "GERMAN"), CancellationToken.None);

        Assert.NotNull(result.Error);
        Assert.Equal(
            "TEXT_A=Vorhanden\n",
            fs.File.ReadAllText("/mod/data/text/mastertextfile_german.properties"));
    }

    /// <summary>A CSV takes a column, so it must not be answered with a file.</summary>
    [Fact]
    public async Task Create_MultiLanguageFormat_IsRefused()
    {
        var (handler, _) = Build(new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/mod/data/text/mastertextfile.csv"] = new("key,ENGLISH\nTEXT_A,Hello\n")
        }));

        var result = await handler.Handle(
            Request("/mod/data/text/mastertextfile.csv", "GERMAN"), CancellationToken.None);

        Assert.NotNull(result.Error);
        Assert.Contains("column", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Create_UnknownLanguage_IsRefused()
    {
        var (handler, _) = Build();

        var result = await handler.Handle(Request(EnglishNls, "KLINGON"), CancellationToken.None);

        Assert.NotNull(result.Error);
        Assert.Null(result.WrittenPath);
    }

    /// <summary>A source that names no language keeps its whole stem, gaining a suffix.</summary>
    [Fact]
    public async Task Create_SourceNamesNoLanguage_AppendsTheSuffixToTheWholeName()
    {
        var (handler, _) = Build(new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/mod/data/text/mastertextfile.properties"] = new(NlsContent)
        }));

        var result = await handler.Handle(
            Request("/mod/data/text/mastertextfile.properties", "GERMAN"), CancellationToken.None);

        Assert.Null(result.Error);
        Assert.Equal("/mod/data/text/mastertextfile_german.properties", result.WrittenPath);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static CreateLocalisationLanguageFileParams Request(string path, string language)
    {
        return new CreateLocalisationLanguageFileParams(path, language);
    }

    private static (CreateLocalisationLanguageFileHandler Handler, MockFileSystem Fs) Build(
        MockFileSystem? initialFs = null)
    {
        var fs = initialFs ?? new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [EnglishNls] = new(NlsContent)
        });

        var services = new ServiceCollection();
        services.AddSingleton<IFileSystem>(fs);
        services.SupportLocalisationBaseline();
        services.AddSingleton<IFileHelper>(sp => new FileHelper(sp.GetRequiredService<IFileSystem>()));
        services.TryAddSingleton<ILspConfigurationProvider>(new FakeLspConfigurationProvider());
        services.AddSingleton<ILocalisationRowReader, LocalisationRowReader>();
        services.AddSingleton<ILocalisationFormatConverter, LocalisationFormatConverter>();
        var sp2 = services.BuildServiceProvider();

        var registry = new LocalisationProjectRegistry();
        registry.Set([new LocProjectInfo(
            "mastertextfile_english.properties", EnglishNls, "Nls", "Root", 1)]);

        var handler = new CreateLocalisationLanguageFileHandler(
            sp2.GetRequiredService<ILocalisationRowReader>(),
            sp2.GetRequiredService<ILocalisationFormatConverter>(),
            sp2.GetRequiredService<PG.StarWarsGame.Localisation.Data.ITranslationDatabaseFactory>(),
            sp2.GetRequiredService<PG.StarWarsGame.Localisation.IO.Dat.IDatTranslationExporter>(),
            sp2.GetRequiredService<PG.StarWarsGame.Files.DAT.Services.IDatFileService>(),
            sp2.GetRequiredService<PG.StarWarsGame.Localisation.Services.ILanguageService>(),
            new FileHelper(fs),
            registry,
            new NoopReloadService(),
            NullLogger<CreateLocalisationLanguageFileHandler>.Instance,
            new FakeLspConfigurationProvider());

        return (handler, fs);
    }

    /// <summary>The handler asks for a reload so the tree sees the new file; nothing here needs it.</summary>
    private sealed class NoopReloadService : IModProjectReloadService
    {
        public IReadOnlyList<string>? LastAssetRoots => null;
        public WorkspaceConfiguration? LastWorkspaceConfig => null;
        public IReadOnlyList<string>? LastWorkspaceRoots => null;

        public Task LoadAsync(IEnumerable<string> workspaceRoots, CancellationToken ct)
        {
            return Task.CompletedTask;
        }

        public Task ReloadAsync(CancellationToken ct)
        {
            return Task.CompletedTask;
        }

        public Task ReloadLocalisationAsync(CancellationToken ct)
        {
            return Task.CompletedTask;
        }
    }
}

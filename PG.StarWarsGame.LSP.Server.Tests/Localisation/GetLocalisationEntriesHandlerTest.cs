// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.Localisation.Baseline;
using PG.StarWarsGame.Localisation.Data;
using PG.StarWarsGame.Localisation.IO.Csv;
using PG.StarWarsGame.Localisation.IO.Properties;
using PG.StarWarsGame.Localisation.IO.Xml;
using PG.StarWarsGame.Localisation.Services;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Server.Localisation;

namespace PG.StarWarsGame.LSP.Server.Tests.Localisation;

public sealed class GetLocalisationEntriesHandlerTest
{
    [Fact]
    public async Task Handle_MissingPath_ReturnsError()
    {
        var handler = BuildHandler(new MockFileSystem());

        var result = await handler.Handle(new GetLocalisationEntriesParams(""), CancellationToken.None);

        Assert.NotNull(result.Error);
        Assert.Empty(result.Entries);
    }

    [Fact]
    public async Task Handle_FileDoesNotExist_ReturnsError()
    {
        var handler = BuildHandler(new MockFileSystem());

        var result = await handler.Handle(
            new GetLocalisationEntriesParams("/nonexistent.csv"), CancellationToken.None);

        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task Handle_Csv_ReturnsParsedEntriesAndLanguages()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/mod/f.csv"] = new("key,ENGLISH,GERMAN\nTEXT_A,Hello,Hallo\n")
        });
        var handler = BuildHandler(fs);

        var result = await handler.Handle(new GetLocalisationEntriesParams("/mod/f.csv"), CancellationToken.None);

        Assert.Null(result.Error);
        var entry = Assert.Single(result.Entries);
        Assert.Equal("TEXT_A", entry.Key);
        Assert.Equal("Hello", entry.Translations["ENGLISH"]);
        Assert.Equal("Hallo", entry.Translations["GERMAN"]);
        Assert.Equal(["ENGLISH", "GERMAN"], result.Languages);
    }

    [Fact]
    public async Task Handle_Csv_EmptyLanguageColumn_StillReportedInLanguages()
    {
        // The header is the source of truth for "which languages exist" — not whether any row
        // has a non-empty value for that language.
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/mod/f.csv"] = new("key,ENGLISH,GERMAN\nTEXT_A,Hello,\n")
        });
        var handler = BuildHandler(fs);

        var result = await handler.Handle(new GetLocalisationEntriesParams("/mod/f.csv"), CancellationToken.None);

        Assert.Contains("GERMAN", result.Languages);
    }

    [Fact]
    public async Task Handle_Xml_ReturnsParsedEntriesAndDeclaredLanguages()
    {
        const string xml = """
                           <LocalisationData xmlns="urn:alamoenginetools:localisation:v1">
                             <Localisation key="TEXT_A">
                               <TranslationData>
                                 <Translation Language="ENGLISH">Hello</Translation>
                                 <Translation Language="GERMAN"></Translation>
                               </TranslationData>
                             </Localisation>
                           </LocalisationData>
                           """;
        var fs = new MockFileSystem(new Dictionary<string, MockFileData> { ["/mod/f.xml"] = new(xml) });
        var handler = BuildHandler(fs);

        var result = await handler.Handle(new GetLocalisationEntriesParams("/mod/f.xml"), CancellationToken.None);

        Assert.Null(result.Error);
        var entry = Assert.Single(result.Entries);
        Assert.Equal("TEXT_A", entry.Key);
        Assert.Contains("ENGLISH", result.Languages);
        Assert.Contains("GERMAN", result.Languages);
    }

    [Fact]
    public async Task Handle_Nls_ReturnsSingleEnglishLanguage()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/mod/f.properties"] = new("TEXT_A=Hello\n")
        });
        var handler = BuildHandler(fs);

        var result = await handler.Handle(
            new GetLocalisationEntriesParams("/mod/f.properties"), CancellationToken.None);

        Assert.Null(result.Error);
        Assert.Equal(["ENGLISH"], result.Languages);
    }

    [Fact]
    public async Task Handle_UnsupportedExtension_ReturnsError()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData> { ["/mod/f.dat"] = new("") });
        var handler = BuildHandler(fs);

        var result = await handler.Handle(new GetLocalisationEntriesParams("/mod/f.dat"), CancellationToken.None);

        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task Handle_SameFile_ReturnsStableHash()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/mod/f.csv"] = new("key,ENGLISH\nTEXT_A,Hello\n")
        });
        var handler = BuildHandler(fs);

        var result1 = await handler.Handle(new GetLocalisationEntriesParams("/mod/f.csv"), CancellationToken.None);
        var result2 = await handler.Handle(new GetLocalisationEntriesParams("/mod/f.csv"), CancellationToken.None);

        Assert.Equal(result1.ContentHash, result2.ContentHash);
        Assert.NotEmpty(result1.ContentHash);
    }

    [Fact]
    public async Task Handle_DifferentContent_ReturnsDifferentHash()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/mod/f.csv"] = new("key,ENGLISH\nTEXT_A,Hello\n")
        });
        var handler = BuildHandler(fs);
        var before = await handler.Handle(new GetLocalisationEntriesParams("/mod/f.csv"), CancellationToken.None);

        fs.File.WriteAllText("/mod/f.csv", "key,ENGLISH\nTEXT_A,Changed\n");
        var after = await handler.Handle(new GetLocalisationEntriesParams("/mod/f.csv"), CancellationToken.None);

        Assert.NotEqual(before.ContentHash, after.ContentHash);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static GetLocalisationEntriesHandler BuildHandler(MockFileSystem fs)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IFileSystem>(fs);
        services.SupportLocalisationBaseline();
        var sp = services.BuildServiceProvider();

        return new GetLocalisationEntriesHandler(
            sp.GetRequiredService<ICsvTranslationImporter>(),
            sp.GetRequiredService<IXmlTranslationImporter>(),
            sp.GetRequiredService<IPropertiesTranslationImporter>(),
            sp.GetRequiredService<ITranslationDatabaseFactory>(),
            sp.GetRequiredService<ILanguageService>(),
            new FileHelper(fs),
            NullLogger<GetLocalisationEntriesHandler>.Instance);
    }
}

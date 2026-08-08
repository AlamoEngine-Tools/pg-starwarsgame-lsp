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

namespace PG.StarWarsGame.LSP.Server.Tests.Localisation;

public sealed class GetLocalisationRowsHandlerTest
{
    private const string CsvPath = "/mod/text/MasterTextFile.csv";
    private const string CreditsPath = "/mod/text/creditstext.csv";

    private static MockFileSystem Files()
    {
        return new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [CsvPath] = new("key,ENGLISH,GERMAN\nTEXT_A,Alpha,Alfa\n"),
            [CreditsPath] = new("key,ENGLISH\nCREDIT_ROLE,Director\nCREDIT_ROLE,Producer\n")
        });
    }

    // ── the payload ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_ReturnsRowsInFileOrderWithLanguages()
    {
        var handler = BuildHandler(Files());

        var result = await handler.Handle(new GetLocalisationRowsParams(CsvPath), CancellationToken.None);

        Assert.Null(result.Error);
        Assert.Equal(["ENGLISH", "GERMAN"], result.Languages);
        var row = Assert.Single(result.Rows);
        Assert.Equal(0, row.Index);
        Assert.Equal("TEXT_A", row.Key);
        Assert.Equal("Alfa", row.Values.Single(v => v.Language == "GERMAN").Value);
    }

    [Fact]
    public async Task Handle_ContentHashMatchesTheFileContents()
    {
        var fs = Files();
        var handler = BuildHandler(fs);

        var result = await handler.Handle(new GetLocalisationRowsParams(CsvPath), CancellationToken.None);

        Assert.Equal(LocalisationContentHash.Compute(fs.File.ReadAllText(CsvPath)), result.ContentHash);
    }

    // Source and Leading serve the save path only. Shipping them would multiply the payload of a
    // 19,000-row file for data the client never reads.
    [Fact]
    public async Task Handle_DoesNotShipTheVerbatimSourceToTheClient()
    {
        var handler = BuildHandler(Files());

        var result = await handler.Handle(new GetLocalisationRowsParams(CsvPath), CancellationToken.None);

        Assert.All(result.Rows, r =>
        {
            Assert.Equal("", r.Source);
            Assert.Equal("", r.Leading);
        });
    }

    // ── whether another language can be added ────────────────────────────────
    //
    // The rule lives in LocalisationDocumentEditor, which refuses per format. The client needs it
    // before it offers the action, and duplicating an extension list in TypeScript is how the two
    // drift apart.

    [Fact]
    public async Task Handle_CsvFile_CanTakeAnotherLanguage()
    {
        var handler = BuildHandler(Files());

        var result = await handler.Handle(new GetLocalisationRowsParams(CsvPath), CancellationToken.None);

        Assert.True(result.CanAddLanguage);
    }

    [Fact]
    public async Task Handle_PropertiesFile_CannotTakeAnotherLanguage()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/mod/data/text/MasterTextFile.properties"] = new("TEXT_A=Alpha\n")
        });
        var handler = BuildHandler(fs);

        var result = await handler.Handle(
            new GetLocalisationRowsParams("/mod/data/text/MasterTextFile.properties"), CancellationToken.None);

        Assert.Null(result.Error);
        Assert.False(result.CanAddLanguage);
    }

    // A credits file may hold several languages, and normally should: the DAT export writes one
    // CreditsText_<LANGUAGE>.dat per language it finds, so one authoring file produces every crawl.
    // The engine's per-language DAT constrains the export, not the file being edited.
    [Fact]
    public async Task Handle_CreditsFile_CanTakeAnotherLanguage()
    {
        var handler = BuildHandler(Files());

        var result = await handler.Handle(new GetLocalisationRowsParams(CreditsPath), CancellationToken.None);

        Assert.Equal(LocCategory.Credits, result.Category);
        Assert.True(result.CanAddLanguage);
    }

    // ── category and ordering ────────────────────────────────────────────────

    [Fact]
    public async Task Handle_TextFile_IsNotOrdered()
    {
        var handler = BuildHandler(Files());

        var result = await handler.Handle(new GetLocalisationRowsParams(CsvPath), CancellationToken.None);

        Assert.Equal(LocCategory.Text, result.Category);
        Assert.False(result.Ordered);
    }

    // Ordered is what tells the client its index addressing is authoritative and that a key is not
    // an identity - without it, duplicate rows would be collapsed or edited together.
    [Fact]
    public async Task Handle_CreditsFile_IsOrderedAndKeepsDuplicateKeys()
    {
        var handler = BuildHandler(Files());

        var result = await handler.Handle(new GetLocalisationRowsParams(CreditsPath), CancellationToken.None);

        Assert.Equal(LocCategory.Credits, result.Category);
        Assert.True(result.Ordered);
        Assert.Equal(2, result.Rows.Count);
        Assert.Equal("CREDIT_ROLE", result.Rows[0].Key);
        Assert.Equal("CREDIT_ROLE", result.Rows[1].Key);
        Assert.Equal([0, 1], result.Rows.Select(r => r.Index));
    }

    // The registry is the only place the project's own credits settings are known, so it wins over
    // the naming convention - that is how "detection": "none" takes effect here.
    [Fact]
    public async Task Handle_RegistryClassification_WinsOverTheConvention()
    {
        var registry = new LocalisationProjectRegistry();
        registry.Set([new LocProjectInfo(
            "creditstext.csv", CreditsPath, "Csv", "Root", 1, LocCategory.Text)]);
        var handler = BuildHandler(Files(), registry: registry);

        var result = await handler.Handle(new GetLocalisationRowsParams(CreditsPath), CancellationToken.None);

        Assert.Equal(LocCategory.Text, result.Category);
        Assert.False(result.Ordered);
    }

    // ── failures ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_FlagOff_ReturnsDisabledError()
    {
        var config = FakeLspConfigurationProvider.WithFeatures(
            new FeatureFlags { Tools = new ToolsFeatureFlags { Localisation = false } });
        var handler = BuildHandler(Files(), config);

        var result = await handler.Handle(new GetLocalisationRowsParams(CsvPath), CancellationToken.None);

        Assert.Equal(LocalisationFeatureDisabled.Message, result.Error);
        Assert.Empty(result.Rows);
    }

    [Fact]
    public async Task Handle_MissingPath_ReturnsError()
    {
        var handler = BuildHandler(Files());

        var result = await handler.Handle(new GetLocalisationRowsParams(""), CancellationToken.None);

        Assert.NotNull(result.Error);
        Assert.Empty(result.Rows);
    }

    [Fact]
    public async Task Handle_UnknownFile_ReturnsError()
    {
        var handler = BuildHandler(Files());

        var result = await handler.Handle(
            new GetLocalisationRowsParams("/mod/text/nope.csv"), CancellationToken.None);

        Assert.Contains("not found", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    // .dat is a supported, editable format (see DatRowRoundTripTest, which exercises the real
    // shipped file). A file that merely claims to be one must fail with a readable message rather
    // than an unhandled exception out of the binary reader.
    [Fact]
    public async Task Handle_CorruptDatFile_ReportsAParseFailureRatherThanThrowing()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/mod/text/creditstext_english.dat"] = new("not actually a dat archive")
        });
        var handler = BuildHandler(fs);

        var result = await handler.Handle(
            new GetLocalisationRowsParams("/mod/text/creditstext_english.dat"), CancellationToken.None);

        Assert.NotNull(result.Error);
        Assert.Contains("Failed to parse", result.Error, StringComparison.Ordinal);
        Assert.Empty(result.Rows);
    }

    [Fact]
    public async Task Handle_MalformedXml_ReportsAParseFailureRatherThanThrowing()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/mod/text/broken.xml"] = new("<Localisations><unclosed>")
        });
        var handler = BuildHandler(fs);

        var result = await handler.Handle(
            new GetLocalisationRowsParams("/mod/text/broken.xml"), CancellationToken.None);

        Assert.Contains("Failed to parse", result.Error, StringComparison.Ordinal);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static GetLocalisationRowsHandler BuildHandler(
        MockFileSystem fs,
        ILspConfigurationProvider? config = null,
        LocalisationProjectRegistry? registry = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IFileSystem>(fs);
        services.SupportLocalisationBaseline();
        services.AddSingleton<IFileHelper>(sp => new FileHelper(sp.GetRequiredService<IFileSystem>()));
        services.TryAddSingleton<ILspConfigurationProvider>(new FakeLspConfigurationProvider());
        services.AddSingleton<ILocalisationRowReader, LocalisationRowReader>();
        var sp = services.BuildServiceProvider();

        return new GetLocalisationRowsHandler(
            sp.GetRequiredService<ILocalisationRowReader>(),
            registry ?? new LocalisationProjectRegistry(),
            new FileHelper(fs),
            NullLogger<GetLocalisationRowsHandler>.Instance,
            config ?? new FakeLspConfigurationProvider());
    }
}

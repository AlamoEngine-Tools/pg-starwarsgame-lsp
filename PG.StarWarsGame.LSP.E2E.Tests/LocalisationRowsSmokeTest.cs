// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Server.Localisation;
using PG.StarWarsGame.LSP.Server.Localisation.Rows;

namespace PG.StarWarsGame.LSP.E2E.Tests;

/// <summary>
///     E2E smoke for the localisation row protocols: a live server, a real workspace on disk, and a
///     full read - edit - write - re-read cycle for both kinds of file.
/// </summary>
/// <remarks>
///     These protocols had unit tests over mock file systems and Playwright coverage of the webview,
///     and nothing at all that booted a server and let the two halves talk. The split into keyed and
///     positional editors is exactly the kind of change where the pieces each pass and the wiring
///     does not - a handler registered under the wrong method name, a DTO whose casing does not
///     survive the round trip - so what is being proved here is that the request reaches the server
///     and the file on disk actually changes.
/// </remarks>
[Trait("Category", "E2E")]
public sealed class LocalisationRowsSmokeTest : IClassFixture<LocalisationServerFixture>
{
    private readonly LocalisationServerFixture _fixture;

    public LocalisationRowsSmokeTest(LocalisationServerFixture fixture)
    {
        _fixture = fixture;
    }

    // ── keyed: translations ──────────────────────────────────────────────────

    [Fact]
    public async Task GetRows_TextFile_ReturnsKeysLanguagesAndIsNotOrdered()
    {
        await ReadyAsync();

        var result = await GetRowsAsync(_fixture.MasterTextPath);

        Assert.Null(result.Error);
        Assert.Equal(["ENGLISH", "GERMAN"], result.Languages);
        Assert.Equal(LocCategory.Text, result.Category);
        Assert.False(result.Ordered);
        Assert.Equal(3, result.Rows.Count);
        Assert.Contains(result.Rows, r => r.Key == "TEXT_E2E_ALPHA");
        // The CSV can hold another column, so the editor may offer to add one.
        Assert.True(result.CanAddLanguage);
    }

    [Fact]
    public async Task ApplyTranslationBatch_SetValueByKey_ChangesTheFileOnDisk()
    {
        await ReadyAsync();
        var before = await GetRowsAsync(_fixture.MasterTextPath);

        using var cts = Timeout();
        var applied = await _fixture.Client.SendRequest(
            new ApplyTranslationBatchParams(
                _fixture.MasterTextPath,
                before.ContentHash,
                [new LocKeyedCommandDto("setValue", "TEXT_E2E_BETA", Language: "GERMAN", Value: "Beta geaendert")]),
            cts.Token);

        Assert.True(applied.Success, applied.Error);
        Assert.NotNull(applied.NewContentHash);

        var after = await GetRowsAsync(_fixture.MasterTextPath);
        var row = Assert.Single(after.Rows, r => r.Key == "TEXT_E2E_BETA");
        Assert.Equal("Beta geaendert", row.Values.Single(v => v.Language == "GERMAN").Value);

        // The hash the write returned is the one a subsequent save must echo.
        Assert.Equal(applied.NewContentHash, after.ContentHash);

        // Only the row that was named changed; the round-trip guarantee is the whole point of the
        // document editor and it is worth proving through the wire, not just in a unit test.
        var text = await File.ReadAllTextAsync(_fixture.MasterTextPath, cts.Token);
        Assert.Contains("TEXT_E2E_ALPHA,Alpha,Alfa", text, StringComparison.Ordinal);
        Assert.Contains("TEXT_E2E_GAMMA,Gamma,Gamma", text, StringComparison.Ordinal);
    }

    // The desync guard: the client must echo the hash it last read, or the write is refused rather
    // than overwriting an edit made elsewhere.
    [Fact]
    public async Task ApplyTranslationBatch_StaleContentHash_IsRefusedAndWritesNothing()
    {
        await ReadyAsync();
        using var cts = Timeout();
        var textBefore = await File.ReadAllTextAsync(_fixture.MasterTextPath, cts.Token);

        var applied = await _fixture.Client.SendRequest(
            new ApplyTranslationBatchParams(
                _fixture.MasterTextPath,
                "definitely-not-the-current-hash",
                [new LocKeyedCommandDto("setValue", "TEXT_E2E_ALPHA", Language: "ENGLISH", Value: "nope")]),
            cts.Token);

        Assert.False(applied.Success);
        // Specifically refused for the hash, not for some earlier guard - without this the test
        // passes just as happily when the whole feature is switched off.
        Assert.Contains("changed on disk", applied.Error!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(textBefore, await File.ReadAllTextAsync(_fixture.MasterTextPath, cts.Token));
    }

    [Fact]
    public async Task ValidateTranslationBatch_DuplicateKey_IsReportedAndNothingIsWritten()
    {
        await ReadyAsync();
        using var cts = Timeout();
        var textBefore = await File.ReadAllTextAsync(_fixture.MasterTextPath, cts.Token);

        var result = await _fixture.Client.SendRequest(
            new ValidateTranslationBatchParams(
                _fixture.MasterTextPath,
                [new LocKeyedCommandDto("addEntry", "TEXT_E2E_ALPHA")]),
            cts.Token);

        Assert.Null(result.Error);
        Assert.Contains(result.Problems, p => p.Severity == "error");
        Assert.Equal(textBefore, await File.ReadAllTextAsync(_fixture.MasterTextPath, cts.Token));
    }

    // The bug a user hit on the first real use of "add language": an XML file has no column list,
    // so addLanguage has nothing to declare - and every value written into the new language was then
    // refused with "Row 0 has no 'GERMAN' translation", after the grid had already shown the column.
    [Fact]
    public async Task ApplyTranslationBatch_AddLanguageThenFillIt_WritesTheNewLanguageToXml()
    {
        await ReadyAsync();
        var before = await GetRowsAsync(_fixture.XmlTextPath);

        using var cts = Timeout();
        var applied = await _fixture.Client.SendRequest(
            new ApplyTranslationBatchParams(
                _fixture.XmlTextPath,
                before.ContentHash,
                [
                    new LocKeyedCommandDto("addLanguage", Language: "GERMAN"),
                    new LocKeyedCommandDto("setValue", "TEXT_E2E_XML", Language: "GERMAN", Value: "Alfa"),
                ]),
            cts.Token);

        Assert.True(applied.Success, applied.Error);

        var after = await GetRowsAsync(_fixture.XmlTextPath);
        Assert.Contains("GERMAN", after.Languages);
        var row = Assert.Single(after.Rows, r => r.Key == "TEXT_E2E_XML");
        Assert.Equal("Alfa", row.Values.Single(v => v.Language == "GERMAN").Value);
        // The language it already had is untouched.
        Assert.Equal("Alpha", row.Values.Single(v => v.Language == "ENGLISH").Value);
    }

    // ── positional: credits ──────────────────────────────────────────────────

    [Fact]
    public async Task GetRows_CreditsFile_IsOrderedAndKeepsItsDuplicateKeys()
    {
        await ReadyAsync();

        var result = await GetRowsAsync(_fixture.CreditsPath);

        Assert.Null(result.Error);
        Assert.Equal(LocCategory.Credits, result.Category);
        Assert.True(result.Ordered);
        Assert.Equal(5, result.Rows.Count);
        // Three CENTER rows and two HEADER rows - collapsing them by key would lose the crawl.
        Assert.Equal(3, result.Rows.Count(r => r.Key == "CENTER"));
        Assert.Equal(2, result.Rows.Count(r => r.Key == "HEADER"));
    }

    [Fact]
    public async Task ApplyCreditsBatch_SetCellByIndex_ChangesTheRowItNames()
    {
        await ReadyAsync();
        var before = await GetRowsAsync(_fixture.CreditsPath);

        using var cts = Timeout();
        var applied = await _fixture.Client.SendRequest(
            new ApplyCreditsBatchParams(
                _fixture.CreditsPath,
                before.ContentHash,
                [new LocEditCommandDto("setCell", 4, Language: "ENGLISH", Value: "Bob the Artist")]),
            cts.Token);

        Assert.True(applied.Success, applied.Error);

        var after = await GetRowsAsync(_fixture.CreditsPath);
        Assert.Equal(5, after.Rows.Count);
        Assert.Equal("Bob the Artist", after.Rows[4].Values.Single().Value);
        // The spacer is a value, not an absence, and has to survive a write untouched.
        Assert.Equal("[TBL]", after.Rows[2].Values.Single().Value);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static CancellationTokenSource Timeout()
    {
        return new CancellationTokenSource(TimeSpan.FromSeconds(30));
    }

    private async Task ReadyAsync()
    {
        await _fixture.ScanCompleted.WaitAsync(TimeSpan.FromSeconds(90));
    }

    private async Task<GetLocalisationRowsResult> GetRowsAsync(string path)
    {
        using var cts = Timeout();
        return await _fixture.Client.SendRequest(new GetLocalisationRowsParams(path), cts.Token);
    }
}

// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Server.Localisation;

namespace PG.StarWarsGame.LSP.E2E.Tests;

/// <summary>
///     E2E smoke for <c>aet/convertLocalisationFormat</c>.
/// </summary>
/// <remarks>
///     Its own class, and so its own fixture and its own throwaway workspace, because a successful
///     conversion repoints the <c>.pgproj</c> at the new format and reloads the project. Sharing a
///     workspace with the row round trips would make those depend on which ran first - and xUnit
///     promises nothing about that.
/// </remarks>
[Trait("Category", "E2E")]
public sealed class LocalisationConvertSmokeTest : IClassFixture<LocalisationServerFixture>
{
    private readonly LocalisationServerFixture _fixture;

    public LocalisationConvertSmokeTest(LocalisationServerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ConvertLocalisationFormat_CsvToXml_WritesTheNewFileAndKeepsTheOld()
    {
        await ReadyAsync();
        using var cts = Timeout();
        var originalText = await File.ReadAllTextAsync(_fixture.MasterTextPath, cts.Token);

        var result = await _fixture.Client.SendRequest(
            new ConvertLocalisationFormatParams(_fixture.MasterTextPath, "Xml"), cts.Token);

        Assert.Null(result.Error);
        Assert.NotNull(result.WrittenPath);
        Assert.True(File.Exists(result.WrittenPath!));
        Assert.EndsWith(".xml", result.WrittenPath!, StringComparison.OrdinalIgnoreCase);

        // "Convert, keep the old file" - the original is the fallback until the result is trusted.
        Assert.True(File.Exists(_fixture.MasterTextPath));
        Assert.Equal(originalText, await File.ReadAllTextAsync(_fixture.MasterTextPath, cts.Token));

        var converted = await File.ReadAllTextAsync(result.WrittenPath!, cts.Token);
        Assert.Contains("TEXT_E2E_ALPHA", converted, StringComparison.Ordinal);
        Assert.Contains("TEXT_E2E_GAMMA", converted, StringComparison.Ordinal);

        // The .pgproj really is repointed. The result flag saying so is not enough: the tree reads
        // the project file, and a conversion that leaves it behind is exactly how the sidebar ends
        // up listing the old format. Asserted here rather than in a test of its own because a
        // second conversion would change the declared format under this one.
        Assert.True(result.ProjectFormatChanged);
        var pgproj = Directory.GetFiles(_fixture.WorkspaceRoot, "*.pgproj").Single();
        var project = await File.ReadAllTextAsync(pgproj, cts.Token);
        Assert.Contains("XML", project, StringComparison.Ordinal);
        Assert.DoesNotContain("csv", project, StringComparison.OrdinalIgnoreCase);
        // Everything else in the project survives the patch.
        Assert.Contains("Localisation E2E", project, StringComparison.Ordinal);
        Assert.Contains("data/xml", project, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConvertLocalisationFormat_ToDat_IsRefusedWithTheExportHint()
    {
        await ReadyAsync();
        using var cts = Timeout();

        var result = await _fixture.Client.SendRequest(
            new ConvertLocalisationFormatParams(_fixture.CreditsPath, "Dat"), cts.Token);

        Assert.NotNull(result.Error);
        Assert.Contains("export", result.Error!, StringComparison.OrdinalIgnoreCase);
        Assert.Null(result.WrittenPath);
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
}

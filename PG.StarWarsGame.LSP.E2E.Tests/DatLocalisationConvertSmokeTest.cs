// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Server.Localisation;

namespace PG.StarWarsGame.LSP.E2E.Tests;

/// <summary>
///     Converting a DAT project, which is what the shipped <c>eaw/</c> workspace is.
/// </summary>
/// <remarks>
///     The reported bug: the new file appeared but the <c>.pgproj</c> kept saying DAT, so the
///     Localisation view went on listing the old format. The repoint asked the format *converter*
///     whether the declared format had an extension, and the converter answers "what can I write" -
///     which is never DAT, by design.
/// </remarks>
[Trait("Category", "E2E")]
public sealed class DatLocalisationConvertSmokeTest : IClassFixture<DatLocalisationServerFixture>
{
    private readonly DatLocalisationServerFixture _fixture;

    public DatLocalisationConvertSmokeTest(DatLocalisationServerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Convert_DatToCsv_WritesTheCsvAndRepointsThePgproj()
    {
        await _fixture.ScanCompleted.WaitAsync(TimeSpan.FromSeconds(90));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var pgproj = Directory.GetFiles(_fixture.WorkspaceRoot, "*.pgproj").Single();

        var result = await _fixture.Client.SendRequest(
            new ConvertLocalisationFormatParams(_fixture.DatPath, "Csv"), cts.Token);

        Assert.True(result.Error is null, $"error was: {result.Error}");
        Assert.NotNull(result.WrittenPath);
        Assert.EndsWith(".csv", result.WrittenPath!, StringComparison.OrdinalIgnoreCase);

        // The DAT was actually read, not merely renamed.
        var csv = await File.ReadAllTextAsync(result.WrittenPath!, cts.Token);
        Assert.Contains("TEXT_FROM_DAT", csv, StringComparison.Ordinal);
        Assert.Contains("Hello from a DAT", csv, StringComparison.Ordinal);

        // The original .dat is kept, as every conversion does.
        Assert.True(File.Exists(_fixture.DatPath));

        // And the project follows the conversion - this is the part that was broken.
        Assert.True(result.ProjectFormatChanged);
        var project = await File.ReadAllTextAsync(pgproj, cts.Token);
        Assert.Contains("CSV", project, StringComparison.Ordinal);
        Assert.DoesNotContain("\"dat\"", project, StringComparison.OrdinalIgnoreCase);
    }
}

// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Server.Localisation;

namespace PG.StarWarsGame.LSP.E2E.Tests;

/// <summary>
///     E2E smoke for <c>aet/setLocalisationProjectFormat</c> (#121): switching a CSV project to the DAT
///     files it already has, which the reporter could only do by editing the <c>.pgproj</c>.
/// </summary>
/// <remarks>
///     Its own class and so its own throwaway workspace, because the switch rewrites the <c>.pgproj</c> and
///     reloads the project. One test rather than several: a second switch would change the declared
///     format under the first, and xUnit promises no order.
/// </remarks>
[Trait("Category", "E2E")]
public sealed class LocalisationProjectFormatSmokeTest : IClassFixture<LocalisationServerFixture>
{
    private readonly LocalisationServerFixture _fixture;

    public LocalisationProjectFormatSmokeTest(LocalisationServerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task SetLocalisationProjectFormat_CsvToDat_RepointsTheProjectAndConvertsNothing()
    {
        await _fixture.ScanCompleted.WaitAsync(TimeSpan.FromSeconds(90));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        // A real DAT beside the CSV, as the reporter had.
        var eaw = LspTestEnvironment.EawWorkspacePath;
        Assert.NotNull(eaw);
        var datPath = Path.Combine(_fixture.TextDirectory, "mastertextfile_english.dat");
        File.Copy(Path.Combine(eaw!, "Data", "Text", "mastertextfile_english.dat"), datPath);
        var csvBefore = await File.ReadAllTextAsync(_fixture.MasterTextPath, cts.Token);

        var result = await _fixture.Client.SendRequest(new SetLocalisationProjectFormatParams("Dat"), cts.Token);

        Assert.Null(result.Error);
        Assert.True(result.Changed);
        Assert.Equal("DAT", result.Format);
        Assert.Equal(1, result.FilesInFormat);

        // The .pgproj really is repointed, and everything else in it survives the patch.
        var pgproj = Directory.GetFiles(_fixture.WorkspaceRoot, "*.pgproj").Single();
        var project = await File.ReadAllTextAsync(pgproj, cts.Token);
        Assert.Contains("\"DAT\"", project, StringComparison.Ordinal);
        Assert.Contains("data/text", project, StringComparison.Ordinal);
        Assert.Contains("data/xml", project, StringComparison.Ordinal);
        Assert.Contains("Localisation E2E", project, StringComparison.Ordinal);

        // Nothing was converted: the CSV is untouched and no new text file appeared.
        Assert.Equal(csvBefore, await File.ReadAllTextAsync(_fixture.MasterTextPath, cts.Token));
        Assert.False(File.Exists(Path.ChangeExtension(_fixture.MasterTextPath, ".dat")));

        // Asking again for the format it now loads writes nothing.
        var again = await _fixture.Client.SendRequest(new SetLocalisationProjectFormatParams("dat"), cts.Token);
        Assert.Null(again.Error);
        Assert.False(again.Changed);
    }
}
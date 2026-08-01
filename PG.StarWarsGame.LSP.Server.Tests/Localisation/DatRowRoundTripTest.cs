// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using PG.StarWarsGame.Files.DAT.Services;
using PG.StarWarsGame.Localisation.Baseline;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Server.Localisation.Rows;

namespace PG.StarWarsGame.LSP.Server.Tests.Localisation;

/// <summary>
///     Compiled DAT files, read and written through the editor's row model.
///     <para>
///         Exercised against the real <c>creditstext_english.dat</c> the workspace ships rather than
///         a fixture: the format is binary and produced by the game's own tools, so a hand-built
///         sample would only prove the code agrees with itself.
///     </para>
/// </summary>
public sealed class DatRowRoundTripTest
{
    private static string RealCreditsDat()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "PG.StarWarsGame.LSP.slnx")))
            dir = dir.Parent;

        return Path.Combine(dir!.FullName, "eaw", "Data", "Text", "creditstext_english.dat");
    }

    private static (ILocalisationRowReader Reader, ILocalisationDocumentEditor Editor) Build()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IFileSystem>(new FileSystem());
        services.SupportLocalisationBaseline();
        services.AddSingleton<IFileHelper>(sp => new FileHelper(sp.GetRequiredService<IFileSystem>()));
        services.AddSingleton<ILocalisationRowReader, LocalisationRowReader>();
        services.AddSingleton<ILocalisationDocumentEditor, LocalisationDocumentEditor>();
        var sp = services.BuildServiceProvider();

        return (sp.GetRequiredService<ILocalisationRowReader>(),
            sp.GetRequiredService<ILocalisationDocumentEditor>());
    }

    /// <summary>Copies the shipped file into a temp directory so the workspace is never written to.</summary>
    private static string CopyToTemp()
    {
        var temp = Path.Combine(Path.GetTempPath(), $"aet_dat_{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        var target = Path.Combine(temp, "creditstext_english.dat");
        File.Copy(RealCreditsDat(), target);
        return target;
    }

    [Fact]
    public void ReadFile_RealCreditsDat_YieldsRowsInOrder()
    {
        var (reader, _) = Build();

        var document = reader.ReadFile(RealCreditsDat());

        Assert.NotEmpty(document.Rows);
        Assert.Equal(["ENGLISH"], document.Languages);
        Assert.Equal(Enumerable.Range(0, document.Rows.Count), document.Rows.Select(r => r.Index));
        Assert.All(document.Rows, r => Assert.Single(r.Values));
    }

    // A credits DAT is unsorted and repeats keys - that is the format, not a defect - so the reader
    // must not collapse or reorder anything.
    [Fact]
    public void ReadFile_RealCreditsDat_KeepsDuplicateKeys()
    {
        var (reader, _) = Build();

        var rows = reader.ReadFile(RealCreditsDat()).Rows;

        Assert.True(rows.Select(r => r.Key).Distinct().Count() < rows.Count,
            "expected the shipped credits file to repeat keys");
    }

    [Fact]
    public async Task ApplyToFile_EditingOneRow_ChangesOnlyThatRowAndKeepsOrder()
    {
        var (reader, editor) = Build();
        var path = CopyToTemp();
        var before = reader.ReadFile(path).Rows;
        var target = before.Count / 2;

        var result = await editor.ApplyToFileAsync(
            path,
            [new LocEditCommandDto("setCell", target, Language: "ENGLISH", Value: "Edited By Test")],
            CancellationToken.None);

        Assert.True(result.Success, result.Error);

        var after = reader.ReadFile(path).Rows;
        Assert.Equal(before.Count, after.Count);
        Assert.Equal("Edited By Test", after[target].Values[0].Value);
        Assert.Equal(before.Select(r => r.Key), after.Select(r => r.Key));

        for (var i = 0; i < before.Count; i++)
            if (i != target)
                Assert.Equal(before[i].Values[0].Value, after[i].Values[0].Value);
    }

    // Rewriting an unsorted credits file as a CRC-sorted one would scramble the crawl into
    // checksum order, which is why the file's own sort order is read back and reused.
    [Fact]
    public async Task ApplyToFile_PreservesTheFilesSortOrder()
    {
        var (_, editor) = Build();
        var path = CopyToTemp();

        var services = new ServiceCollection();
        services.AddSingleton<IFileSystem>(new FileSystem());
        services.SupportLocalisationBaseline();
        var datService = services.BuildServiceProvider().GetRequiredService<IDatFileService>();

        var before = datService.Load(path).Content.KeySortOrder;

        await editor.ApplyToFileAsync(
            path, [new LocEditCommandDto("setCell", 0, Language: "ENGLISH", Value: "X")],
            CancellationToken.None);

        Assert.Equal(before, datService.Load(path).Content.KeySortOrder);
    }

    [Fact]
    public async Task ApplyToFile_InsertAndDelete_ChangeTheRowCount()
    {
        var (reader, editor) = Build();
        var path = CopyToTemp();
        var before = reader.ReadFile(path).Rows.Count;

        await editor.ApplyToFileAsync(
            path,
            [new LocEditCommandDto("insertRow", 0, Key: "TEST_INSERTED",
                Values: [new LocValueDto("ENGLISH", "Inserted")])],
            CancellationToken.None);

        var afterInsert = reader.ReadFile(path).Rows;
        Assert.Equal(before + 1, afterInsert.Count);
        Assert.Equal("Inserted", afterInsert[0].Values[0].Value);

        await editor.ApplyToFileAsync(
            path, [new LocEditCommandDto("deleteRow", 0)], CancellationToken.None);

        Assert.Equal(before, reader.ReadFile(path).Rows.Count);
    }

    // A DAT carries the one language its filename names; a second column cannot be represented.
    [Fact]
    public async Task ApplyToFile_AddLanguage_IsRefused()
    {
        var (_, editor) = Build();
        var path = CopyToTemp();

        var result = await editor.ApplyToFileAsync(
            path, [new LocEditCommandDto("addLanguage", Language: "GERMAN")], CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("single language", result.Error, StringComparison.OrdinalIgnoreCase);
    }
}

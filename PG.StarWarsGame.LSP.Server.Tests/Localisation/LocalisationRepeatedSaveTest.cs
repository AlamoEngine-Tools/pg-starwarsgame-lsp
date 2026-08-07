// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PG.StarWarsGame.Localisation.Baseline;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Server.Localisation.Rows;

using PG.StarWarsGame.LSP.Core.Configuration;

namespace PG.StarWarsGame.LSP.Server.Tests.Localisation;

/// <summary>
///     Saving the same file repeatedly must not grow it.
/// </summary>
/// <remarks>
///     The round-trip tests each compose once, which cannot see a defect that only shows on the
///     second pass - a trailing line ending added on write and read back as an extra row would
///     multiply blank lines one per save, and a credits file is mostly blank lines.
/// </remarks>
public sealed class LocalisationRepeatedSaveTest
{
    private const string Credits =
        "key,ENGLISH\r\n"
        + "HEADER,Lead Designer\r\n"
        + "CENTER,Alice\r\n"
        + "CENTER,[TBL]\r\n"
        + "HEADER,Lead Artist\r\n"
        + "CENTER,Bob\r\n";

    private const string Text =
        "key,ENGLISH,GERMAN\r\n"
        + "TEXT_A,Alpha,Alfa\r\n"
        + "TEXT_B,Beta,Beta DE\r\n";

    private static (ILocalisationDocumentEditor Editor, ILocalisationRowReader Reader) Build()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IFileSystem>(new FileSystem());
        services.SupportLocalisationBaseline();
        services.AddSingleton<IFileHelper>(sp => new FileHelper(sp.GetRequiredService<IFileSystem>()));
        services.TryAddSingleton<ILspConfigurationProvider>(new FakeLspConfigurationProvider());
        services.AddSingleton<ILocalisationRowReader, LocalisationRowReader>();
        services.AddSingleton<ILocalisationDocumentEditor, LocalisationDocumentEditor>();
        var sp2 = services.BuildServiceProvider();
        return (sp2.GetRequiredService<ILocalisationDocumentEditor>(),
            sp2.GetRequiredService<ILocalisationRowReader>());
    }

    /// <summary>Composes an edit, feeds the result back in, and composes again.</summary>
    private static (string First, string Second) SaveTwice(string original, string extension)
    {
        var (editor, _) = Build();
        var edit = new LocEditCommandDto("setCell", 1, Language: "ENGLISH", Value: "Edited");

        var first = editor.Apply(original, extension, [edit]);
        Assert.True(first.Success, first.Error);

        var second = editor.Apply(first.NewText!, extension, [edit]);
        Assert.True(second.Success, second.Error);

        return (first.NewText!, second.NewText!);
    }

    [Fact]
    public void CreditsFile_SavedTwice_DoesNotGrow()
    {
        var (first, second) = SaveTwice(Credits, ".csv");

        Assert.Equal(first, second);
    }

    [Fact]
    public void TextFile_SavedTwice_DoesNotGrow()
    {
        var (first, second) = SaveTwice(Text, ".csv");

        Assert.Equal(first, second);
    }

    // The row count is what a growing trailing newline would show up in first.
    [Fact]
    public void SavingDoesNotAddARow()
    {
        var (_, reader) = Build();
        var before = reader.Read(Credits, ".csv");

        var (first, second) = SaveTwice(Credits, ".csv");

        Assert.Equal(before.Rows.Count, reader.Read(first, ".csv").Rows.Count);
        Assert.Equal(before.Rows.Count, reader.Read(second, ".csv").Rows.Count);
    }

    // A file the author left without a trailing newline must not gain one, and one that has it must
    // not gain a second.
    [Theory]
    [InlineData("key,ENGLISH\r\nHEADER,Lead\r\nCENTER,Alice")]
    [InlineData("key,ENGLISH\r\nHEADER,Lead\r\nCENTER,Alice\r\n")]
    public void TrailingNewlineIsNotMultiplied(string original)
    {
        var (first, second) = SaveTwice(original, ".csv");

        Assert.Equal(first, second);
        Assert.DoesNotContain("\r\n\r\n", second, StringComparison.Ordinal);
    }

    // Ten passes: a defect adding one line per save is obvious here even if one pass looks clean.
    [Fact]
    public void TenSaves_LeaveTheFileTheSameLength()
    {
        var (editor, _) = Build();
        var edit = new LocEditCommandDto("setCell", 1, Language: "ENGLISH", Value: "Edited");

        var text = Credits;
        for (var i = 0; i < 10; i++)
        {
            var result = editor.Apply(text, ".csv", [edit]);
            Assert.True(result.Success, result.Error);
            text = result.NewText!;
        }

        Assert.Equal(
            Credits.Split("\r\n").Length,
            text.Split("\r\n").Length);
    }
}

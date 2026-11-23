// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PG.StarWarsGame.Localisation.Baseline;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Messages;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Localisation.Rows;

namespace PG.StarWarsGame.LSP.Localisation.Tests;

/// <summary>
///     What a save through the localisation grid or the credits editor does to a file somebody
///     wrote by hand (#339 FR-3): the file differs in the lines the edit is about and in nothing
///     else.
///     <para>
///         Each file here holds what the grid does not model - comments, an attribute and an element
///         no schema knows, rows and languages out of order, odd spacing inside a tag, single quotes,
///         an entity, a CDATA section, a blank line, a byte order mark - and is saved with one edit,
///         then compared with the original line by line or byte by byte.
///     </para>
/// </summary>
public sealed class LocalisationRoundTripTest : IDisposable
{
    private const string XmlRewritten =
        "#339 FR-3, red on today's writer: the XML is serialised anew, so the declaration and attribute quotes are rewritten, spacing inside a tag is normalised, and the comments outside the root and the last line ending are dropped";

    private const string CsvBlankLine =
        "#339 FR-3, red on today's writer: a blank line inside a CSV file is dropped";

    private const string CsvByteOrderMark =
        "#339 FR-3, red on today's writer: the file's byte order mark is dropped";

    private const string PropertiesBlankLine =
        "#339 FR-3, red on today's writer: the blank line after the leading comments of a properties file is dropped";

    private const string PropertiesSpacedKey =
        "#339 FR-3, red on today's reader: an entry written 'KEY = value' is not found by its key";

    private static readonly string[] XmlLines =
    [
        "<?xml version='1.0' encoding='utf-8' ?>",
        "<!-- Texts of the mod. Sorted by hand. -->",
        "<Localisations>",
        "  <!-- the menu -->",
        "  <Localisation Key=\"TEXT_B\" Reviewed='yes'>",
        "    <Note>not a translation</Note>",
        "    <TranslationData>",
        "      <Translation Language=\"GERMAN\">Beta &amp; mehr</Translation>",
        "      <Translation Language=\"ENGLISH\">Beta</Translation>",
        "    </TranslationData>",
        "  </Localisation>",
        "",
        "  <Localisation   Key=\"TEXT_A\" >",
        "    <TranslationData>",
        "      <Translation Language=\"ENGLISH\"><![CDATA[Alpha <b>]]></Translation>",
        "    </TranslationData>",
        "  </Localisation>",
        "</Localisations>",
        "<!-- end of the file -->"
    ];

    private static readonly string[] CsvLines =
    [
        "key,GERMAN,ENGLISH",
        "TEXT_B,\"Beta, mehr\",Beta",
        "",
        "TEXT_A,Alfa ,\"Alpha\"",
        "TEXT_C,Gamma"
    ];

    private static readonly string[] PropertiesLines =
    [
        "# Texts of the mod",
        "! an older note",
        "",
        "TEXT_B = Beta",
        "TEXT_A=Alpha",
        "  # indented note",
        "TEXT_C:Gamma"
    ];

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "aet-loc-round-trip-" + Guid.NewGuid().ToString("N"));

    public LocalisationRoundTripTest()
    {
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        Directory.Delete(_directory, true);
    }

    private static ILocalisationDocumentEditor Editor()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IFileSystem>(new FileSystem());
        services.SupportPetroglyphHashing();
        services.SupportLocalisationBaseline();
        services.AddSingleton<IFileHelper>(sp => new FileHelper(sp.GetRequiredService<IFileSystem>()));
        services.TryAddSingleton<ILspConfigurationProvider>(new FakeLspConfigurationProvider());
        services.AddSingleton<ILocalisationRowReader, LocalisationRowReader>();
        services.AddSingleton<ILocalisationDocumentEditor, LocalisationDocumentEditor>();
        services.AddSingleton<IMessageCatalog>(ResxMessageCatalog.Server("en"));
        return services.BuildServiceProvider().GetRequiredService<ILocalisationDocumentEditor>();
    }

    private static string Text(string[] lines, string lineEnding)
    {
        return string.Join(lineEnding, lines) + lineEnding;
    }

    /// <summary>Writes the text as a file, saves one keyed edit into it, and returns the file's new text.</summary>
    private async Task<string> SaveKeyedAsync(string fileName, string original, params LocKeyedCommandDto[] commands)
    {
        var path = Path.Combine(_directory, fileName);
        await File.WriteAllTextAsync(path, original, new UTF8Encoding(false), TestContext.Current.CancellationToken);

        var result = await Editor().ApplyKeyedToFileAsync(path, commands, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        return await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
    }

    private static LocKeyedCommandDto SetValue(string key, string language, string value)
    {
        return new LocKeyedCommandDto("setValue", key, null, language, value);
    }

    /// <summary>The lines only the original holds and the lines only the saved text holds, endings included.</summary>
    private static (List<string> Removed, List<string> Added) Diff(string original, string saved)
    {
        var before = Lines(original);
        var after = Lines(saved);

        // Longest common subsequence over whole lines: what it leaves out on either side is the change.
        var common = new int[before.Count + 1, after.Count + 1];
        for (var i = before.Count - 1; i >= 0; i--)
        for (var j = after.Count - 1; j >= 0; j--)
            common[i, j] = before[i] == after[j]
                ? common[i + 1, j + 1] + 1
                : Math.Max(common[i + 1, j], common[i, j + 1]);

        var removed = new List<string>();
        var added = new List<string>();
        int a = 0, b = 0;
        while (a < before.Count && b < after.Count)
            if (before[a] == after[b])
            {
                a++;
                b++;
            }
            else if (common[a + 1, b] >= common[a, b + 1])
            {
                removed.Add(before[a++]);
            }
            else
            {
                added.Add(after[b++]);
            }

        removed.AddRange(before.Skip(a));
        added.AddRange(after.Skip(b));
        return (removed, added);
    }

    /// <summary>The text cut after every line feed, so a line carries its own ending.</summary>
    private static List<string> Lines(string text)
    {
        var lines = new List<string>();
        var start = 0;
        while (start < text.Length)
        {
            var feed = text.IndexOf('\n', start);
            var end = feed < 0 ? text.Length : feed + 1;
            lines.Add(text[start..end]);
            start = end;
        }

        return lines;
    }

    /// <summary>
    ///     Fails with both sides of the line diff when the save changed anything but the expected
    ///     lines. <c>blankLinesCount</c> is false where a removal may take the blank line beside it
    ///     or leave its indentation behind: lines of nothing but white space are then left out of
    ///     the comparison.
    /// </summary>
    private static void AssertChange(
        string original, string saved, IReadOnlyList<string> expectedRemoved, IReadOnlyList<string> expectedAdded,
        bool blankLinesCount = true)
    {
        var (removed, added) = Diff(original, saved);
        if (!blankLinesCount)
        {
            removed.RemoveAll(string.IsNullOrWhiteSpace);
            added.RemoveAll(string.IsNullOrWhiteSpace);
        }

        Assert.True(
            removed.SequenceEqual(expectedRemoved) && added.SequenceEqual(expectedAdded),
            $"Lost: {Describe(removed)}\nGained: {Describe(added)}\n"
            + $"Expected to lose: {Describe(expectedRemoved)}\nExpected to gain: {Describe(expectedAdded)}");
    }

    private static string Describe(IEnumerable<string> lines)
    {
        var shown = lines
            .Select(l => "[" + l.Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t") + "]")
            .ToList();
        return shown.Count == 0 ? "nothing" : string.Join(" ", shown);
    }

    public static TheoryData<string> LineEndings => ["\n", "\r\n"];

    // ── XML ──────────────────────────────────────────────────────────────────

    [Theory(Skip = XmlRewritten)]
    [MemberData(nameof(LineEndings))]
    public void Xml_SetCell_ChangesTheValuesLine_AndNoOther(string lineEnding)
    {
        var original = Text(XmlLines, lineEnding);

        var result = Editor().Apply(original, ".xml",
            [new LocEditCommandDto("setCell", 0, Language: "ENGLISH", Value: "Changed", ExpectedKey: "TEXT_B")]);

        Assert.True(result.Success, result.Error);
        AssertChange(original, result.NewText!,
            [$"      <Translation Language=\"ENGLISH\">Beta</Translation>{lineEnding}"],
            [$"      <Translation Language=\"ENGLISH\">Changed</Translation>{lineEnding}"]);
    }

    [Theory(Skip = XmlRewritten)]
    [MemberData(nameof(LineEndings))]
    public void Xml_DeleteRow_RemovesTheRowsLines_AndNoOther(string lineEnding)
    {
        var original = Text(XmlLines, lineEnding);

        var result = Editor().Apply(original, ".xml",
            [new LocEditCommandDto("deleteRow", 1, ExpectedKey: "TEXT_A")]);

        Assert.True(result.Success, result.Error);
        AssertChange(original, result.NewText!,
            XmlLines.Skip(12).Take(5).Select(l => l + lineEnding).ToList(),
            [],
            blankLinesCount: false);
    }

    [Theory(Skip = XmlRewritten)]
    [MemberData(nameof(LineEndings))]
    public async Task Xml_KeyedSetValue_ChangesTheValuesLine_AndNoOther(string lineEnding)
    {
        var original = Text(XmlLines, lineEnding);

        var saved = await SaveKeyedAsync("texts.xml", original, SetValue("TEXT_B", "ENGLISH", "Changed"));

        AssertChange(original, saved,
            [$"      <Translation Language=\"ENGLISH\">Beta</Translation>{lineEnding}"],
            [$"      <Translation Language=\"ENGLISH\">Changed</Translation>{lineEnding}"]);
    }

    // ── CSV ──────────────────────────────────────────────────────────────────

    [Theory(Skip = CsvBlankLine)]
    [MemberData(nameof(LineEndings))]
    public async Task Csv_KeyedSetValue_ChangesTheRowsLine_AndNoOther(string lineEnding)
    {
        var original = Text(CsvLines, lineEnding);

        var saved = await SaveKeyedAsync("texts.csv", original, SetValue("TEXT_C", "ENGLISH", "Gamma EN"));

        AssertChange(original, saved,
            [$"TEXT_C,Gamma{lineEnding}"],
            [$"TEXT_C,Gamma,Gamma EN{lineEnding}"]);
    }

    [Fact(Skip = CsvByteOrderMark)]
    public async Task Csv_Save_KeepsTheFilesByteOrderMark()
    {
        var path = Path.Combine(_directory, "marked.csv");
        var original = Text(CsvLines, "\r\n");
        await File.WriteAllTextAsync(path, original, new UTF8Encoding(true), TestContext.Current.CancellationToken);

        var result = await Editor().ApplyKeyedToFileAsync(
            path, [SetValue("TEXT_B", "ENGLISH", "Changed")], TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        var bytes = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3).ToArray());
    }

    // ── .properties ──────────────────────────────────────────────────────────

    [Theory(Skip = PropertiesBlankLine)]
    [MemberData(nameof(LineEndings))]
    public async Task Properties_KeyedSetValue_ChangesTheEntrysLine_AndNoOther(string lineEnding)
    {
        var original = Text(PropertiesLines, lineEnding);

        var saved = await SaveKeyedAsync(
            "texts_english.properties", original, SetValue("TEXT_A", "ENGLISH", "Changed"));

        AssertChange(original, saved,
            [$"TEXT_A=Alpha{lineEnding}"],
            [$"TEXT_A=Changed{lineEnding}"]);
    }

    [Theory(Skip = PropertiesSpacedKey)]
    [MemberData(nameof(LineEndings))]
    public async Task Properties_KeyedSetValue_KeepsTheSpacingAroundTheSeparator(string lineEnding)
    {
        var original = Text(PropertiesLines, lineEnding);

        var saved = await SaveKeyedAsync(
            "texts_english.properties", original, SetValue("TEXT_B", "ENGLISH", "Changed"));

        AssertChange(original, saved,
            [$"TEXT_B = Beta{lineEnding}"],
            [$"TEXT_B = Changed{lineEnding}"]);
    }
}

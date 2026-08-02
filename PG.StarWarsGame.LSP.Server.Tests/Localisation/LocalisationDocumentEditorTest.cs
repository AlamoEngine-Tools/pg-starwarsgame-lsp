// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using PG.StarWarsGame.Localisation.Baseline;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Server.Localisation.Rows;

namespace PG.StarWarsGame.LSP.Server.Tests.Localisation;

/// <summary>
///     The save path: a staged batch composed into the file's new text, as a pure string-to-string
///     function with no IO.
///     <para>
///         The contract that matters most is round-trip fidelity. A save that reformats rows the
///         user did not touch produces an unreviewable diff on a 19,000-row file and destroys trust
///         in the editor permanently - worse than a crash, because it looks like it worked.
///     </para>
/// </summary>
public sealed class LocalisationDocumentEditorTest
{
    private static ILocalisationDocumentEditor Editor()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IFileSystem>(new FileSystem());
        services.SupportLocalisationBaseline();
        services.AddSingleton<IFileHelper>(sp => new FileHelper(sp.GetRequiredService<IFileSystem>()));
        services.AddSingleton<ILocalisationRowReader, LocalisationRowReader>();
        services.AddSingleton<ILocalisationDocumentEditor, LocalisationDocumentEditor>();
        return services.BuildServiceProvider().GetRequiredService<ILocalisationDocumentEditor>();
    }

    private static string Apply(string text, string ext, params LocEditCommandDto[] commands)
    {
        var result = Editor().Apply(text, ext, commands);
        Assert.True(result.Success, result.Error);
        return result.NewText!;
    }

    private static LocEditCommandDto SetCell(int index, string language, string value, string? expectedKey = null)
    {
        return new LocEditCommandDto("setCell", index, Language: language, Value: value, ExpectedKey: expectedKey);
    }

    private const string Csv = "key,ENGLISH,GERMAN\nTEXT_A,Alpha,Alfa\nTEXT_B,Beta,Beta_DE\nTEXT_C,Gamma,Gamma_DE\n";

    // ── round-trip fidelity ──────────────────────────────────────────────────

    [Fact]
    public void EmptyBatch_ReturnsTheFileUnchanged()
    {
        Assert.Equal(Csv, Apply(Csv, ".csv"));
    }

    [Fact]
    public void SetCell_ChangesOnlyTheTargetedLine()
    {
        var result = Apply(Csv, ".csv", SetCell(1, "GERMAN", "Beta_NEU"));

        Assert.Equal(
            "key,ENGLISH,GERMAN\nTEXT_A,Alpha,Alfa\nTEXT_B,Beta,Beta_NEU\nTEXT_C,Gamma,Gamma_DE\n",
            result);
    }

    // Preserving CRLF matters because the alternative is a diff touching every line of the file.
    [Fact]
    public void CrlfLineEndings_AreNotNormalised()
    {
        const string crlf = "key,ENGLISH\r\nTEXT_A,Alpha\r\nTEXT_B,Beta\r\n";

        var result = Apply(crlf, ".csv", SetCell(0, "ENGLISH", "Changed"));

        Assert.Equal("key,ENGLISH\r\nTEXT_A,Changed\r\nTEXT_B,Beta\r\n", result);
    }

    [Fact]
    public void UntouchedRowsKeepTheirOriginalQuotingVerbatim()
    {
        // "Beta" needs no quotes by the writer's rules, but the file quoted it anyway. Re-emitting
        // it unquoted would be a diff on a row the user never edited.
        const string csv = "key,ENGLISH\nTEXT_A,\"Beta\"\nTEXT_B,Gamma\n";

        var result = Apply(csv, ".csv", SetCell(1, "ENGLISH", "Delta"));

        Assert.Contains("TEXT_A,\"Beta\"", result, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEditedValueNeedingQuotes_IsQuoted()
    {
        var result = Apply(Csv, ".csv", SetCell(0, "ENGLISH", "one, two"));

        Assert.Contains("TEXT_A,\"one, two\",Alfa", result, StringComparison.Ordinal);
    }

    // ── the golden file ──────────────────────────────────────────────────────

    // The acceptance gate for R2, against the real 19,000-row shipped file rather than a fixture.
    [Fact]
    public void GoldenFile_SingleEdit_LeavesEveryOtherLineByteIdentical()
    {
        var path = Path.Combine(FindRepoRoot(), "eaw", "Data", "Text", "MasterTextFile.csv");
        Assert.True(File.Exists(path), $"Golden file not found: {path}");

        var original = File.ReadAllText(path);
        var result = Apply(original, ".csv", SetCell(10, "ENGLISH", "A Changed Value"));

        var before = original.Split('\n');
        var after = result.Split('\n');

        Assert.Equal(before.Length, after.Length);
        var differing = Enumerable.Range(0, before.Length)
            .Where(i => !string.Equals(before[i], after[i], StringComparison.Ordinal))
            .ToList();

        Assert.Single(differing);
        Assert.Contains("A Changed Value", after[differing[0]], StringComparison.Ordinal);
    }

    // ── row operations ───────────────────────────────────────────────────────

    [Fact]
    public void InsertRow_ShiftsEverythingBelowDown()
    {
        var result = Apply(Csv, ".csv", new LocEditCommandDto(
            "insertRow", 0, Key: "TEXT_NEW",
            Values: [new LocValueDto("ENGLISH", "New"), new LocValueDto("GERMAN", "Neu")]));

        Assert.Equal(
            "key,ENGLISH,GERMAN\nTEXT_NEW,New,Neu\nTEXT_A,Alpha,Alfa\nTEXT_B,Beta,Beta_DE\nTEXT_C,Gamma,Gamma_DE\n",
            result);
    }

    [Fact]
    public void DeleteRow_RemovesOnlyThatRow()
    {
        var result = Apply(Csv, ".csv", new LocEditCommandDto("deleteRow", 1));

        Assert.Equal("key,ENGLISH,GERMAN\nTEXT_A,Alpha,Alfa\nTEXT_C,Gamma,Gamma_DE\n", result);
    }

    [Fact]
    public void MoveRow_ReordersWithoutRewritingTheRow()
    {
        var result = Apply(Csv, ".csv", new LocEditCommandDto("moveRow", 2, ToIndex: 0));

        Assert.Equal(
            "key,ENGLISH,GERMAN\nTEXT_C,Gamma,Gamma_DE\nTEXT_A,Alpha,Alfa\nTEXT_B,Beta,Beta_DE\n",
            result);
    }

    [Fact]
    public void SetKey_RenamesOnlyThatRow()
    {
        var result = Apply(Csv, ".csv", new LocEditCommandDto("setKey", 0, Key: "TEXT_RENAMED"));

        Assert.Contains("TEXT_RENAMED,Alpha,Alfa", result, StringComparison.Ordinal);
        Assert.DoesNotContain("TEXT_A,", result, StringComparison.Ordinal);
    }

    [Fact]
    public void AddLanguage_AppendsAColumnToTheHeaderAndEveryRow()
    {
        var result = Apply(Csv, ".csv", new LocEditCommandDto("addLanguage", Language: "FRENCH"));

        Assert.StartsWith("key,ENGLISH,GERMAN,FRENCH\n", result, StringComparison.Ordinal);
        Assert.Contains("TEXT_A,Alpha,Alfa,\n", result, StringComparison.Ordinal);
    }

    // ── duplicate keys ───────────────────────────────────────────────────────

    // The reason index addressing exists: a key cannot name which of two rows to edit.
    [Fact]
    public void DuplicateKeys_EditingOne_LeavesTheOtherAlone()
    {
        const string credits = "key,ENGLISH\nCREDIT_ROLE,Director\nCREDIT_ROLE,Producer\n";

        var result = Apply(credits, ".csv", SetCell(1, "ENGLISH", "Executive Producer"));

        Assert.Equal("key,ENGLISH\nCREDIT_ROLE,Director\nCREDIT_ROLE,Executive Producer\n", result);
    }

    // ── batch semantics ──────────────────────────────────────────────────────

    // Indices resolve against the document as of all preceding commands in the batch, so a client
    // can compose operations the way the user performed them.
    [Fact]
    public void IndicesResolveAgainstThePrecedingCommandsResult()
    {
        var result = Apply(Csv, ".csv",
            new LocEditCommandDto("deleteRow", 0),
            SetCell(0, "ENGLISH", "WasRowOne"));

        Assert.Equal("key,ENGLISH,GERMAN\nTEXT_B,WasRowOne,Beta_DE\nTEXT_C,Gamma,Gamma_DE\n", result);
    }

    [Fact]
    public void RepeatedEditsToOneCell_LastOneWins()
    {
        var result = Apply(Csv, ".csv",
            SetCell(0, "ENGLISH", "First"),
            SetCell(0, "ENGLISH", "Second"));

        Assert.Contains("TEXT_A,Second,Alfa", result, StringComparison.Ordinal);
    }

    // ── the desync guard ─────────────────────────────────────────────────────

    // If the client's view of row order has drifted from the server's, an index-addressed edit
    // lands on the wrong row. Failing the batch turns silent corruption into a clear error.
    [Fact]
    public void ExpectedKeyMismatch_FailsTheBatchAndWritesNothing()
    {
        var result = Editor().Apply(Csv, ".csv", [SetCell(1, "ENGLISH", "X", "TEXT_WRONG")]);

        Assert.False(result.Success);
        Assert.Null(result.NewText);
        Assert.Equal(0, result.FailedIndex);
        Assert.Contains("TEXT_WRONG", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void ExpectedKeyMatch_Proceeds()
    {
        var result = Editor().Apply(Csv, ".csv", [SetCell(1, "ENGLISH", "X", "TEXT_B")]);

        Assert.True(result.Success);
    }

    [Fact]
    public void FailedIndex_NamesTheOffendingCommand()
    {
        var result = Editor().Apply(Csv, ".csv",
        [
            SetCell(0, "ENGLISH", "Fine"),
            SetCell(1, "ENGLISH", "Fine"),
            new LocEditCommandDto("deleteRow", 99)
        ]);

        Assert.False(result.Success);
        Assert.Equal(2, result.FailedIndex);
    }

    [Theory]
    [InlineData("deleteRow")]
    [InlineData("setKey")]
    public void IndexOutOfRange_Fails(string kind)
    {
        var result = Editor().Apply(Csv, ".csv", [new LocEditCommandDto(kind, 99, Key: "K")]);

        Assert.False(result.Success);
        Assert.Equal(0, result.FailedIndex);
    }

    [Fact]
    public void UnknownCommandKind_Fails()
    {
        var result = Editor().Apply(Csv, ".csv", [new LocEditCommandDto("teleportRow", 0)]);

        Assert.False(result.Success);
        Assert.Contains("teleportRow", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void SetCell_UnknownLanguage_Fails()
    {
        var result = Editor().Apply(Csv, ".csv", [SetCell(0, "KLINGON", "X")]);

        Assert.False(result.Success);
        Assert.Contains("KLINGON", result.Error, StringComparison.Ordinal);
    }

    // ── .properties ──────────────────────────────────────────────────────────

    [Fact]
    public void Properties_SetCell_RewritesOnlyThatEntry()
    {
        const string text = "# a note\nTEXT_A=Alpha\nTEXT_B=Beta\n";

        var result = Apply(text, ".properties", SetCell(1, "ENGLISH", "Changed"));

        Assert.Equal("# a note\nTEXT_A=Alpha\nTEXT_B=Changed\n", result);
    }

    // Comments are the file's only documentation; dropping them would be a destructive edit
    // disguised as a translation change.
    [Fact]
    public void Properties_CommentsSurviveARowDeletion()
    {
        const string text = "# leading\nTEXT_A=Alpha\n# about B\nTEXT_B=Beta\n";

        var result = Apply(text, ".properties", new LocEditCommandDto("deleteRow", 0));

        Assert.Contains("# leading", result, StringComparison.Ordinal);
        Assert.Contains("# about B", result, StringComparison.Ordinal);
        Assert.DoesNotContain("TEXT_A", result, StringComparison.Ordinal);
    }

    // The format is single-language by definition, so a second column cannot be represented.
    [Fact]
    public void Properties_AddLanguage_Fails()
    {
        var result = Editor().Apply(
            "TEXT_A=Alpha\n", ".properties", [new LocEditCommandDto("addLanguage", Language: "GERMAN")]);

        Assert.False(result.Success);
        Assert.Contains("single-language", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    // ── XML ──────────────────────────────────────────────────────────────────

    private const string Xml = """
                               <?xml version="1.0" encoding="utf-8"?>
                               <Localisations xmlns="urn:alamoenginetools:localisation:v1">
                                 <Localisation key="TEXT_A">
                                   <TranslationData>
                                     <Translation Language="ENGLISH">Alpha</Translation>
                                   </TranslationData>
                                 </Localisation>
                                 <Localisation key="TEXT_B">
                                   <TranslationData>
                                     <Translation Language="ENGLISH">Beta</Translation>
                                   </TranslationData>
                                 </Localisation>
                               </Localisations>
                               """;

    [Fact]
    public void Xml_EmptyBatch_ReturnsTheDocumentUnchanged()
    {
        Assert.Equal(Xml, Apply(Xml, ".xml"));
    }

    [Fact]
    public void Xml_SetCell_ChangesOnlyThatTranslation()
    {
        var result = Apply(Xml, ".xml", SetCell(1, "ENGLISH", "Changed"));

        Assert.Contains(">Changed<", result, StringComparison.Ordinal);
        Assert.Contains(">Alpha<", result, StringComparison.Ordinal);
        Assert.Equal(Xml.Replace(">Beta<", ">Changed<", StringComparison.Ordinal), result);
    }

    [Fact]
    public void Xml_DeleteRow_RemovesTheElement()
    {
        var result = Apply(Xml, ".xml", new LocEditCommandDto("deleteRow", 0));

        Assert.DoesNotContain("TEXT_A", result, StringComparison.Ordinal);
        Assert.Contains("TEXT_B", result, StringComparison.Ordinal);
    }

    // ── the reference model ──────────────────────────────────────────────────

    // A randomised sequence checked against an independent list implementation: the composer's own
    // ordering logic cannot be trusted to verify itself.
    [Fact]
    public void RandomisedSequence_MatchesAnIndependentRowModel()
    {
        var random = new Random(20260731);
        var text = Csv;
        var model = new List<(string Key, string English)>
        {
            ("TEXT_A", "Alpha"), ("TEXT_B", "Beta"), ("TEXT_C", "Gamma")
        };

        for (var step = 0; step < 50; step++)
        {
            var count = model.Count;
            LocEditCommandDto command;

            switch (count == 0 ? 0 : random.Next(4))
            {
                case 0:
                    var at = count == 0 ? 0 : random.Next(count + 1);
                    var key = $"K{step}";
                    command = new LocEditCommandDto("insertRow", at, Key: key,
                        Values: [new LocValueDto("ENGLISH", $"V{step}"), new LocValueDto("GERMAN", "")]);
                    model.Insert(at, (key, $"V{step}"));
                    break;
                case 1:
                    var del = random.Next(count);
                    command = new LocEditCommandDto("deleteRow", del);
                    model.RemoveAt(del);
                    break;
                case 2:
                    var from = random.Next(count);
                    var to = random.Next(count);
                    command = new LocEditCommandDto("moveRow", from, ToIndex: to);
                    var moved = model[from];
                    model.RemoveAt(from);
                    model.Insert(to, moved);
                    break;
                default:
                    var edit = random.Next(count);
                    command = SetCell(edit, "ENGLISH", $"E{step}");
                    model[edit] = (model[edit].Key, $"E{step}");
                    break;
            }

            var result = Editor().Apply(text, ".csv", [command]);
            Assert.True(result.Success, $"step {step}: {result.Error}");
            text = result.NewText!;
        }

        var actual = Editor() is var _ ? ReadKeysAndEnglish(text) : [];
        Assert.Equal(model, actual);
    }

    private static List<(string Key, string English)> ReadKeysAndEnglish(string csv)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IFileSystem>(new FileSystem());
        services.SupportLocalisationBaseline();
        services.AddSingleton<IFileHelper>(sp => new FileHelper(sp.GetRequiredService<IFileSystem>()));
        services.AddSingleton<ILocalisationRowReader, LocalisationRowReader>();
        var reader = services.BuildServiceProvider().GetRequiredService<ILocalisationRowReader>();

        return reader.Read(csv, ".csv").Rows
            .Select(r => (r.Key, r.Values.Single(v => v.Language == "ENGLISH").Value))
            .ToList();
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "PG.StarWarsGame.LSP.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Could not locate repo root.");
    }
}

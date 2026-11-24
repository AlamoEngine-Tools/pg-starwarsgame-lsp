// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Localisation;
using PG.StarWarsGame.LSP.Localisation.Rows;
using PG.StarWarsGame.LSP.Server.Editing;
using static PG.StarWarsGame.LSP.Server.Tests.Localisation.LocalisationEditBatchFixture;

namespace PG.StarWarsGame.LSP.Server.Tests.Localisation;

/// <summary>
///     The key-addressed save and its dry run, for translation files.
///     <para>
///         Mirrors the guarantees the positional batch makes - atomic and hash-guarded - against a
///         vocabulary that never mentions a row position. A save composes the edit and writes
///         nothing: the file is the editor's window's to write.
///     </para>
/// </summary>
public sealed class TranslationEditBatchComposerTest
{
    private static readonly string TextDirectory =
        Path.Combine(Path.GetPathRoot(Path.GetFullPath("."))!, "mod", "text");

    private static readonly string File = Path.Combine(TextDirectory, "MasterTextFile.csv");
    private const string Csv = "key,ENGLISH\nTEXT_A,Alpha\nTEXT_B,Beta\n";

    private static LocalisationEditBatchFixture Fixture(
        string content = Csv, Func<FileHelper, IProjectLayerMap>? layers = null)
    {
        return new LocalisationEditBatchFixture(new Dictionary<string, string> { [File] = content }, layers);
    }

    private static LocKeyedCommandDto SetValue(string key, string value, string language = "ENGLISH")
    {
        return new LocKeyedCommandDto("setValue", key, Language: language, Value: value);
    }

    private static Task<ApplyEditBatchResult> ComposeAsync(
        LocalisationEditBatchFixture fixture, params LocKeyedCommandDto[] commands)
    {
        return fixture.Translation.ComposeAsync(fixture.Subject(File), Staged(commands), CancellationToken.None);
    }

    // ── the save ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Compose_ForADependencysFile_IsRefusedAndComposesNothing()
    {
        var fixture = Fixture(layers: DependencyOwnsTheFile);

        var result = await ComposeAsync(fixture, SetValue("TEXT_B", "Changed"));

        Assert.False(result.Success);
        Assert.Contains("read-only", result.Error);
        Assert.Null(result.Edit);
    }

    [Fact]
    public async Task Compose_ResolvesTheKeyAndComposesTheText_AndWritesNothing()
    {
        var fixture = Fixture();

        var result = await ComposeAsync(fixture, SetValue("TEXT_B", "Changed"));

        Assert.True(result.Success, result.Error);
        const string composed = "key,ENGLISH\nTEXT_A,Alpha\nTEXT_B,Changed\n";
        Assert.Equal(composed, Assert.Single(Composed(result)).Value);
        // The hash the next batch has to carry is the composed text's, which the window is about to write.
        Assert.Equal(LocalisationContentHash.Compute(composed), Assert.Single(result.Documents!).ContentHash);
        Assert.Equal(Csv, fixture.Files.File.ReadAllText(File));
    }

    [Fact]
    public async Task Compose_AddEntry_AppendsARow()
    {
        var result = await ComposeAsync(Fixture(),
            new LocKeyedCommandDto("addEntry", "TEXT_C", Values: [new LocValueDto("ENGLISH", "Gamma")]));

        Assert.True(result.Success, result.Error);
        Assert.Contains("TEXT_C,Gamma", Assert.Single(Composed(result)).Value);
    }

    [Fact]
    public async Task Compose_DeleteEntry_RemovesTheRow()
    {
        var result = await ComposeAsync(Fixture(), new LocKeyedCommandDto("deleteEntry", "TEXT_A"));

        Assert.True(result.Success, result.Error);
        var composed = Assert.Single(Composed(result)).Value;
        Assert.DoesNotContain("TEXT_A", composed);
        Assert.Contains("TEXT_B,Beta", composed);
    }

    [Fact]
    public async Task Compose_EmptyBatch_SucceedsWithNoEdit_AndTheFilesOwnHash()
    {
        var result = await ComposeAsync(Fixture());

        Assert.True(result.Success);
        Assert.Null(result.Edit);
        Assert.Equal(LocalisationContentHash.Compute(Csv), Assert.Single(result.Documents!).ContentHash);
    }

    /// <summary>
    ///     The whole point of round-trip fidelity, reached through the keyed path: a one-value edit
    ///     leaves every other line alone.
    /// </summary>
    [Fact]
    public async Task Compose_LeavesUntouchedRowsByteIdentical()
    {
        const string quoted = "key,ENGLISH\nTEXT_A,\"Alpha, with comma\"\nTEXT_B,Beta\n";

        var result = await ComposeAsync(Fixture(quoted), SetValue("TEXT_B", "Changed"));

        Assert.Contains("TEXT_A,\"Alpha, with comma\"", Assert.Single(Composed(result)).Value);
    }

    // ── addressing failures ──────────────────────────────────────────────────

    [Fact]
    public async Task Compose_UnknownKey_FailsAndComposesNothing()
    {
        var result = await ComposeAsync(Fixture(), SetValue("TEXT_MISSING", "x"));

        Assert.False(result.Success);
        Assert.Contains("TEXT_MISSING", result.Error);
        Assert.Null(result.Edit);
    }

    [Fact]
    public async Task Compose_ReportsWhichStagedChangeFailed()
    {
        var result = await ComposeAsync(Fixture(), SetValue("TEXT_A", "fine"), SetValue("TEXT_MISSING", "bad"));

        Assert.False(result.Success);
        Assert.Equal(1, result.FailedIndex);
        Assert.Null(result.Edit);
    }

    // ── the concurrency guard ────────────────────────────────────────────────

    [Fact]
    public async Task Compose_MissingHash_IsRejected()
    {
        var fixture = Fixture();

        var result = await fixture.Translation.ComposeAsync(
            new EditBatchSubject(Documents: [new EditBatchDocument(File)]),
            Staged(SetValue("TEXT_A", "Changed")), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Null(result.Edit);
    }

    [Fact]
    public async Task Compose_StaleHash_IsRejected()
    {
        var fixture = Fixture();

        var result = await fixture.Translation.ComposeAsync(
            new EditBatchSubject(Documents: [new EditBatchDocument(File, "not-the-current-hash")]),
            Staged(SetValue("TEXT_A", "x")), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("changed on disk", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Null(result.Edit);
    }

    // ── what a batch may not be written to ───────────────────────────────────

    [Fact]
    public async Task Compose_WithNoFileNamed_IsRefused()
    {
        var result = await Fixture().Translation.ComposeAsync(
            new EditBatchSubject(), Staged(SetValue("TEXT_A", "x")), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("No project file path provided.", result.Error);
    }

    /// <summary>
    ///     A compiled DAT is what a build makes of the project. It has no text for an edit to be made
    ///     in, so a batch for one is refused with what to do instead - before the file is even read.
    /// </summary>
    [Fact]
    public async Task Compose_ForACompiledDat_IsRefusedWithTheWayToEditItsTexts()
    {
        var dat = Path.Combine(TextDirectory, "mastertextfile_english.dat");
        var fixture = new LocalisationEditBatchFixture(new Dictionary<string, string> { [dat] = "not read" });

        var result = await fixture.Translation.ComposeAsync(
            new EditBatchSubject(Documents: [new EditBatchDocument(dat, "any")]),
            Staged(SetValue("TEXT_A", "x")), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Null(result.Edit);
        Assert.Equal(
            "'mastertextfile_english.dat' is a compiled DAT, which a build writes from the project. "
            + "Convert the project to CSV or XML to edit its texts.",
            result.Error);
    }

    // ── what the edit says it was composed from ──────────────────────────────

    [Fact]
    public async Task Compose_FromTheSavedFile_SaysSoByCarryingNoVersion()
    {
        var result = await ComposeAsync(Fixture(), SetValue("TEXT_B", "Changed"));

        Assert.Null(Assert.Single(result.Edit!.DocumentChanges!).TextDocumentEdit!.TextDocument.Version);
    }

    [Fact]
    public async Task Compose_FromAnOpenDocument_ComposesItsTextAndCarriesItsVersion()
    {
        var fixture = Fixture();
        const string unsaved = "key,ENGLISH\nTEXT_A,Typed and not saved\nTEXT_B,Beta\n";
        fixture.Host.AddOrUpdate(fixture.UriOf(File), unsaved, 7);

        var result = await fixture.Translation.ComposeAsync(
            new EditBatchSubject(Documents:
                [new EditBatchDocument(File, LocalisationContentHash.Compute(unsaved))]),
            Staged(SetValue("TEXT_B", "Changed")), CancellationToken.None);

        Assert.True(result.Success, result.Error);
        var edit = Assert.Single(result.Edit!.DocumentChanges!).TextDocumentEdit!;
        Assert.Equal(7, edit.TextDocument.Version);
        Assert.Equal("key,ENGLISH\nTEXT_A,Typed and not saved\nTEXT_B,Changed\n", edit.Edits.Single().NewText);
    }

    // ── a set of single-language files ───────────────────────────────────────

    private static readonly string English = Path.Combine(TextDirectory, "texts_english.properties");
    private static readonly string German = Path.Combine(TextDirectory, "texts_german.properties");

    private static LocalisationEditBatchFixture Set()
    {
        return new LocalisationEditBatchFixture(new Dictionary<string, string>
        {
            [English] = "TEXT_A=Alpha\nTEXT_B=Beta\n",
            [German] = "TEXT_A=Alfa\nTEXT_B=Beta DE\n"
        });
    }

    private static Task<ApplyEditBatchResult> ComposeSetAsync(
        LocalisationEditBatchFixture fixture, params LocKeyedCommandDto[] commands)
    {
        return fixture.Translation.ComposeAsync(
            fixture.Subject(English, German), Staged(commands), CancellationToken.None);
    }

    [Fact]
    public async Task ComposeForASet_AValueGoesToTheFileOfItsLanguage_AndTheOtherIsNotInTheEdit()
    {
        var result = await ComposeSetAsync(Set(), SetValue("TEXT_A", "Alfa neu", "GERMAN"));

        Assert.True(result.Success, result.Error);
        var composed = Assert.Single(Composed(result));
        Assert.Equal("texts_german.properties", composed.Key);
        Assert.Equal("TEXT_A=Alfa neu\nTEXT_B=Beta DE\n", composed.Value);
    }

    /// <summary>
    ///     A change of the table's shape goes to every file, in one edit: the files of a set are
    ///     written together or not at all, so they cannot drift into different key lists.
    /// </summary>
    [Fact]
    public async Task ComposeForASet_ANewEntryGoesToEveryFile_EachWithItsOwnLanguagesValue_InOneEdit()
    {
        var result = await ComposeSetAsync(Set(),
            new LocKeyedCommandDto("addEntry", "TEXT_C",
                Values: [new LocValueDto("ENGLISH", "Gamma"), new LocValueDto("GERMAN", "Gamma DE")]));

        Assert.True(result.Success, result.Error);
        var composed = Composed(result);
        Assert.Equal(2, composed.Count);
        Assert.EndsWith("TEXT_C=Gamma\n", composed["texts_english.properties"]);
        Assert.EndsWith("TEXT_C=Gamma DE\n", composed["texts_german.properties"]);
    }

    [Fact]
    public async Task ComposeForASet_AnswersEveryFilesHash_ChangedOrNot()
    {
        var fixture = Set();

        var result = await ComposeSetAsync(fixture, SetValue("TEXT_A", "Alfa neu", "GERMAN"));

        Assert.Equal(
        [
            new EditBatchDocument(English, LocalisationContentHash.Compute("TEXT_A=Alpha\nTEXT_B=Beta\n")),
            new EditBatchDocument(German, LocalisationContentHash.Compute("TEXT_A=Alfa neu\nTEXT_B=Beta DE\n"))
        ], result.Documents);
    }

    [Fact]
    public async Task ComposeForASet_AFailureInOneFile_NamesTheChangeAsStaged_AndComposesNothing()
    {
        var result = await ComposeSetAsync(Set(),
            SetValue("TEXT_A", "fine", "GERMAN"),
            SetValue("TEXT_B", "fine"),
            SetValue("TEXT_MISSING", "bad", "GERMAN"));

        Assert.False(result.Success);
        Assert.Equal(2, result.FailedIndex);
        Assert.Null(result.Edit);
    }

    [Fact]
    public async Task ComposeForASet_WithOneFileChangedSinceItWasRead_IsRefusedWhole()
    {
        var fixture = Set();
        var subject = new EditBatchSubject(Documents:
        [
            fixture.Subject(English).Documents![0],
            new EditBatchDocument(German, "not-the-current-hash")
        ]);

        var result = await fixture.Translation.ComposeAsync(
            subject, Staged(SetValue("TEXT_A", "x")), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Null(result.Edit);
    }

    // ── the dry run ──────────────────────────────────────────────────────────

    private static Task<ValidateEditBatchResult> ValidateAsync(
        LocalisationEditBatchFixture fixture, params LocKeyedCommandDto[] commands)
    {
        return fixture.Translation.ValidateAsync(fixture.Subject(File), Staged(commands), CancellationToken.None);
    }

    [Fact]
    public async Task Validate_AWorkableBatch_ReportsNothing()
    {
        var result = await ValidateAsync(Fixture(), SetValue("TEXT_A", "x"));

        Assert.Empty(result.Problems);
    }

    [Fact]
    public async Task Validate_AnUnknownKey_IsReportedAgainstTheChange()
    {
        var result = await ValidateAsync(Fixture(), SetValue("TEXT_MISSING", "x"));

        var problem = Assert.Single(result.Problems);
        Assert.Contains("Change 1", problem.Message);
    }

    [Fact]
    public async Task Validate_ADuplicateAlreadyInTheFile_IsReported()
    {
        var result = await ValidateAsync(Fixture("key,ENGLISH\nTEXT_A,Alpha\nTEXT_A,Again\n"));

        var problem = Assert.Single(result.Problems);
        Assert.Equal("TEXT_A", problem.Key);
    }

    [Fact]
    public async Task Validate_WritesNothing()
    {
        var fixture = Fixture();

        await ValidateAsync(fixture, SetValue("TEXT_A", "x"));

        Assert.Equal(Csv, fixture.Files.File.ReadAllText(File));
    }

    /// <summary>The file under test belongs to a dependency; the project itself writes elsewhere.</summary>
    private static IProjectLayerMap DependencyOwnsTheFile(FileHelper helper)
    {
        var map = new ProjectLayerMap(helper);
        map.SetLayers([
            new ProjectLayer(0, "Core", [], [], [TextDirectory], [], null),
            new ProjectLayer(1, "Rev", [], [], [Path.Combine(TextDirectory, "..", "..", "leaf", "text")], [], null)
        ]);
        return map;
    }
}
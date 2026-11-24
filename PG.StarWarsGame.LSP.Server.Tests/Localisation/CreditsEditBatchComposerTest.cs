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
///     The index-addressed save and its dry run, for credits files.
///     <para>
///         What separates these from the translation composer is what they do <em>not</em> check:
///         a credits file repeats its keys and relies on blank rows, so neither is a problem here.
///     </para>
/// </summary>
public sealed class CreditsEditBatchComposerTest
{
    private static readonly string TextDirectory =
        Path.Combine(Path.GetPathRoot(Path.GetFullPath("."))!, "mod", "text");

    private static readonly string File = Path.Combine(TextDirectory, "creditstext.csv");
    private const string Csv = "key,ENGLISH\nHEADER,Directed by\nCENTER,SOMEONE\nCENTER,[TBL]\n";

    private static LocalisationEditBatchFixture Fixture(
        string content = Csv, Func<FileHelper, IProjectLayerMap>? layers = null)
    {
        return new LocalisationEditBatchFixture(new Dictionary<string, string> { [File] = content }, layers);
    }

    private static Task<ApplyEditBatchResult> ComposeAsync(
        LocalisationEditBatchFixture fixture, params LocEditCommandDto[] commands)
    {
        return fixture.Credits.ComposeAsync(fixture.Subject(File), Staged(commands), CancellationToken.None);
    }

    private static Task<ValidateEditBatchResult> ValidateAsync(
        LocalisationEditBatchFixture fixture, params LocEditCommandDto[] commands)
    {
        return fixture.Credits.ValidateAsync(fixture.Subject(File), Staged(commands), CancellationToken.None);
    }

    [Fact]
    public async Task Compose_ForADependencysFile_IsRefusedAndComposesNothing()
    {
        var result = await ComposeAsync(Fixture(layers: DependencyOwnsTheFile),
            new LocEditCommandDto("setCell", 1, Language: "ENGLISH", Value: "SOMEONE ELSE"));

        Assert.False(result.Success);
        Assert.Contains("read-only", result.Error);
        Assert.Null(result.Edit);
    }

    [Fact]
    public async Task Compose_ComposesTheText_AnswersItsHash_AndWritesNothing()
    {
        var fixture = Fixture();

        var result = await ComposeAsync(fixture,
            new LocEditCommandDto("setCell", 1, Language: "ENGLISH", Value: "SOMEONE ELSE"));

        Assert.True(result.Success, result.Error);
        var composed = Assert.Single(Composed(result)).Value;
        Assert.Equal("key,ENGLISH\nHEADER,Directed by\nCENTER,SOMEONE ELSE\nCENTER,[TBL]\n", composed);
        Assert.Equal(LocalisationContentHash.Compute(composed), Assert.Single(result.Documents!).ContentHash);
        Assert.Equal(Csv, fixture.Files.File.ReadAllText(File));
    }

    [Fact]
    public async Task Compose_MoveRow_ReordersTheFile()
    {
        var result = await ComposeAsync(Fixture(), new LocEditCommandDto("moveRow", 0, 2));

        Assert.True(result.Success, result.Error);
        var lines = Assert.Single(Composed(result)).Value.Split('\n');
        Assert.Equal("HEADER,Directed by", lines[3]);
    }

    [Fact]
    public async Task Compose_OutOfRangeIndex_FailsNamingTheChange_AndComposesNothing()
    {
        var result = await ComposeAsync(Fixture(),
            new LocEditCommandDto("setCell", 99, Language: "ENGLISH", Value: "x"));

        Assert.False(result.Success);
        Assert.Equal(0, result.FailedIndex);
        Assert.Null(result.Edit);
    }

    [Fact]
    public async Task Compose_StaleHash_IsRejected()
    {
        var fixture = Fixture();

        var result = await fixture.Credits.ComposeAsync(
            new EditBatchSubject(Documents: [new EditBatchDocument(File, "not-the-current-hash")]),
            Staged(new LocEditCommandDto("deleteRow", 0)), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("changed on disk", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Null(result.Edit);
    }

    [Fact]
    public async Task Compose_EmptyBatch_SucceedsWithNoEdit_AndTheFilesOwnHash()
    {
        var result = await ComposeAsync(Fixture());

        Assert.True(result.Success);
        Assert.Null(result.Edit);
        Assert.Equal(LocalisationContentHash.Compute(Csv), Assert.Single(result.Documents!).ContentHash);
    }

    // ── the dry run ──────────────────────────────────────────────────────────

    /// <summary>
    ///     The behaviour that separates this from the translation validator: the shipped credits
    ///     file repeats <c>CENTER</c> on hundreds of rows and uses <c>[TBL]</c> as a blank line, and
    ///     neither is worth a word to the user.
    /// </summary>
    [Fact]
    public async Task Validate_DuplicateKeysAndSpacers_AreNotProblems()
    {
        var result = await ValidateAsync(Fixture());

        Assert.Empty(result.Problems);
    }

    /// <summary>
    ///     A heading with nothing under it is reported as information, never as a problem.
    ///     <para>
    ///         A credits key is a formatting directive, so such a file is valid and the crawl renders
    ///         it exactly as written - it just reads the heading out and moves on. It is worth seeing,
    ///         since it usually means the label was translated and the names under it were not, but
    ///         grading a valid file as broken teaches people to ignore the tag.
    ///     </para>
    /// </summary>
    [Fact]
    public async Task Validate_AHeadingWithNothingUnderIt_IsInformationNotAProblem()
    {
        var result = await ValidateAsync(Fixture(
            "key,ENGLISH\nHEADER,Directed by\nHEADER,Produced by\nCENTER,SOMEONE\n"));

        var problem = Assert.Single(result.Problems);
        Assert.Equal(LocProblemSeverity.Info, problem.Severity);
        Assert.Contains("Directed by", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Validate_ABatchThatCannotCompose_IsReported()
    {
        var result = await ValidateAsync(Fixture(), new LocEditCommandDto("deleteRow", 99));

        var problem = Assert.Single(result.Problems);
        Assert.Contains("Change 1", problem.Message);
    }

    /// <summary>
    ///     A batch that cannot compose is about a staged command, not about a row - so it names the
    ///     command in its message and leaves <see cref="EditBatchProblemDto.Index" /> null.
    ///     <para>
    ///         The two indices are unrelated and both are small integers, which is what made putting
    ///         one where the other belongs invisible.
    ///         <see cref="LocalisationEditResult.FailedIndex" /> counts commands in the batch;
    ///         <see cref="EditBatchProblemDto.Index" /> is a row in the file, and the grid uses it to
    ///         tint that row and to scroll to it. Sending the command number here made a failed edit
    ///         highlight whichever row happened to share its number - a row the user had not touched
    ///         and the message did not mention.
    ///     </para>
    /// </summary>
    [Fact]
    public async Task Validate_ABatchThatCannotCompose_NamesNoRow()
    {
        // Deleting row 99 of a 3-row file fails as command 0. Row 0 exists and is untouched by the
        // batch, so reporting index 0 would point the grid at a perfectly good row.
        var result = await ValidateAsync(Fixture(), new LocEditCommandDto("deleteRow", 99));

        var problem = Assert.Single(result.Problems);
        Assert.Null(problem.Index);
    }

    [Fact]
    public async Task Validate_WritesNothing()
    {
        var fixture = Fixture();

        await ValidateAsync(fixture, new LocEditCommandDto("deleteRow", 0));

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
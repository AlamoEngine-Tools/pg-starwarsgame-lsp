// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.Localisation.Baseline;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Server.Localisation;
using PG.StarWarsGame.LSP.Server.Localisation.Rows;
using PG.StarWarsGame.LSP.Server.Project;

namespace PG.StarWarsGame.LSP.Server.Tests.Localisation;

/// <summary>
///     The position-addressed save and its dry run, for credits files.
///     <para>
///         What separates these from the translation handlers is what they do <em>not</em> check:
///         a credits file repeats its keys and relies on blank rows, so neither is a problem here.
///     </para>
/// </summary>
public sealed class CreditsBatchHandlerTest
{
    private const string Path = "/mod/text/creditstext.csv";
    private const string Csv = "key,ENGLISH\nHEADER,Directed by\nCENTER,SOMEONE\nCENTER,[TBL]\n";

    private static MockFileSystem Files(string content = Csv)
    {
        return new MockFileSystem(new Dictionary<string, MockFileData> { [Path] = new(content) });
    }

    private static string HashOf(MockFileSystem fs)
    {
        return LocalisationContentHash.Compute(fs.File.ReadAllText(Path));
    }

    [Fact]
    public async Task Apply_WritesTheComposedTextAndReturnsItsHash()
    {
        var fs = Files();
        var (apply, _, reload) = Build(fs);

        var result = await apply.Handle(
            new ApplyCreditsBatchParams(Path, HashOf(fs),
                [new LocEditCommandDto("setCell", 1, Language: "ENGLISH", Value: "SOMEONE ELSE")]),
            CancellationToken.None);

        Assert.True(result.Success, result.Error);
        Assert.Contains("CENTER,SOMEONE ELSE", fs.File.ReadAllText(Path));
        Assert.Equal(HashOf(fs), result.NewContentHash);
        Assert.Equal(1, reload.LocalisationReloads);
    }

    [Fact]
    public async Task Apply_MoveRow_ReordersTheFile()
    {
        var fs = Files();
        var (apply, _, _) = Build(fs);

        var result = await apply.Handle(
            new ApplyCreditsBatchParams(Path, HashOf(fs),
                [new LocEditCommandDto("moveRow", 0, ToIndex: 2)]),
            CancellationToken.None);

        Assert.True(result.Success, result.Error);
        var lines = fs.File.ReadAllText(Path).Split('\n');
        Assert.Equal("HEADER,Directed by", lines[3]);
    }

    [Fact]
    public async Task Apply_OutOfRangeIndex_FailsWithoutWriting()
    {
        var fs = Files();
        var (apply, _, reload) = Build(fs);

        var result = await apply.Handle(
            new ApplyCreditsBatchParams(Path, HashOf(fs),
                [new LocEditCommandDto("setCell", 99, Language: "ENGLISH", Value: "x")]),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(Csv, fs.File.ReadAllText(Path));
        Assert.Equal(0, reload.LocalisationReloads);
    }

    [Fact]
    public async Task Apply_StaleHash_IsRejectedWithoutWriting()
    {
        var fs = Files();
        var (apply, _, _) = Build(fs);

        var result = await apply.Handle(
            new ApplyCreditsBatchParams(Path, "not-the-current-hash",
                [new LocEditCommandDto("deleteRow", 0)]),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("changed on disk", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(Csv, fs.File.ReadAllText(Path));
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
        var (_, validate, _) = Build(Files());

        var result = await validate.Handle(
            new ValidateCreditsBatchParams(Path, []), CancellationToken.None);

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
        var (_, validate, _) = Build(Files(
            "key,ENGLISH\nHEADER,Directed by\nHEADER,Produced by\nCENTER,SOMEONE\n"));

        var result = await validate.Handle(
            new ValidateCreditsBatchParams(Path, []), CancellationToken.None);

        var problem = Assert.Single(result.Problems);
        Assert.Equal(LocProblemSeverity.Info, problem.Severity);
        Assert.Contains("Directed by", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Validate_ABatchThatCannotCompose_IsReported()
    {
        var (_, validate, _) = Build(Files());

        var result = await validate.Handle(
            new ValidateCreditsBatchParams(Path,
                [new LocEditCommandDto("deleteRow", 99)]),
            CancellationToken.None);

        var problem = Assert.Single(result.Problems);
        Assert.Contains("Change 1", problem.Message);
    }

    /// <summary>
    ///     A batch that cannot compose is about a staged command, not about a row - so it names the
    ///     command in its message and leaves <see cref="LocProblemDto.Index" /> null.
    ///     <para>
    ///         The two indices are unrelated and both are small integers, which is what made putting
    ///         one where the other belongs invisible.
    ///         <see cref="LocalisationEditResult.FailedIndex" /> counts commands in the batch;
    ///         <see cref="LocProblemDto.Index" /> is a row in the file, and the grid uses it to tint
    ///         that row and to scroll to it. Sending the command number here made a failed edit
    ///         highlight whichever row happened to share its number - a row the user had not touched
    ///         and the message did not mention. The translation validator has always reported this
    ///         correctly; this is the credits side matching it.
    ///     </para>
    /// </summary>
    [Fact]
    public async Task Validate_ABatchThatCannotCompose_NamesNoRow()
    {
        // Deleting row 99 of a 3-row file fails as command 0. Row 0 exists and is untouched by the
        // batch, so reporting index 0 would point the grid at a perfectly good row.
        var (_, validate, _) = Build(Files());

        var result = await validate.Handle(
            new ValidateCreditsBatchParams(Path,
                [new LocEditCommandDto("deleteRow", 99)]),
            CancellationToken.None);

        var problem = Assert.Single(result.Problems);
        Assert.Null(problem.Index);
    }

    [Fact]
    public async Task Validate_WritesNothing()
    {
        var fs = Files();
        var (_, validate, _) = Build(fs);

        await validate.Handle(
            new ValidateCreditsBatchParams(Path, [new LocEditCommandDto("deleteRow", 0)]),
            CancellationToken.None);

        Assert.Equal(Csv, fs.File.ReadAllText(Path));
    }

    [Fact]
    public async Task Apply_WithTheToolDisabled_RefusesAndWritesNothing()
    {
        var fs = Files();
        var config = FakeLspConfigurationProvider.WithFeatures(
            new FeatureFlags { Tools = new ToolsFeatureFlags { Localisation = false } });
        var (apply, _, _) = Build(fs, config);

        var result = await apply.Handle(
            new ApplyCreditsBatchParams(Path, HashOf(fs), [new LocEditCommandDto("deleteRow", 0)]),
            CancellationToken.None);

        Assert.Equal(LocalisationFeatureDisabled.Message, result.Error);
        Assert.Equal(Csv, fs.File.ReadAllText(Path));
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static (ApplyCreditsBatchHandler Apply, ValidateCreditsBatchHandler Validate,
        CountingReloadService Reload) Build(
            MockFileSystem fs, ILspConfigurationProvider? config = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IFileSystem>(fs);
        services.SupportLocalisationBaseline();
        services.AddSingleton<IFileHelper>(sp => new FileHelper(sp.GetRequiredService<IFileSystem>()));
        services.TryAddSingleton<ILspConfigurationProvider>(new FakeLspConfigurationProvider());
        services.AddSingleton<ILocalisationRowReader, LocalisationRowReader>();
        services.AddSingleton<ILocalisationDocumentEditor, LocalisationDocumentEditor>();
        var sp = services.BuildServiceProvider();

        var helper = new FileHelper(fs);
        var reload = new CountingReloadService();
        var configuration = config ?? new FakeLspConfigurationProvider();
        var editor = sp.GetRequiredService<ILocalisationDocumentEditor>();

        return (
            new ApplyCreditsBatchHandler(
                editor, reload, helper,
                NullLogger<ApplyCreditsBatchHandler>.Instance, configuration,
                new LocalisationWriteLedger(helper)),
            new ValidateCreditsBatchHandler(editor, helper, configuration),
            reload);
    }

    private sealed class CountingReloadService : IModProjectReloadService
    {
        public int LocalisationReloads { get; private set; }

        public IReadOnlyList<string>? LastAssetRoots => null;
        public WorkspaceConfiguration? LastWorkspaceConfig => null;
        public IReadOnlyList<string>? LastWorkspaceRoots => null;

        public Task LoadAsync(IEnumerable<string> workspaceRoots, CancellationToken ct)
        {
            return Task.CompletedTask;
        }

        public Task ReloadAsync(CancellationToken ct)
        {
            return Task.CompletedTask;
        }

        public Task ReloadLocalisationAsync(CancellationToken ct)
        {
            LocalisationReloads++;
            return Task.CompletedTask;
        }
    }
}

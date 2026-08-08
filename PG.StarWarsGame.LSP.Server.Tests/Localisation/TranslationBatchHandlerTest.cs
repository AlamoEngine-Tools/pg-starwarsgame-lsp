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

using PG.Commons.Hashing;

namespace PG.StarWarsGame.LSP.Server.Tests.Localisation;

/// <summary>
///     The key-addressed save and its dry run, for translation files.
///     <para>
///         Mirrors the guarantees the positional batch already makes - atomic, hash-guarded, one
///         reload on success - against a vocabulary that never mentions a row position.
///     </para>
/// </summary>
public sealed class TranslationBatchHandlerTest
{
    private const string Path = "/mod/text/MasterTextFile.csv";
    private const string Csv = "key,ENGLISH\nTEXT_A,Alpha\nTEXT_B,Beta\n";

    private static MockFileSystem Files(string content = Csv)
    {
        return new MockFileSystem(new Dictionary<string, MockFileData> { [Path] = new(content) });
    }

    private static string HashOf(MockFileSystem fs)
    {
        return LocalisationContentHash.Compute(fs.File.ReadAllText(Path));
    }

    private static LocKeyedCommandDto SetValue(string key, string value)
    {
        return new LocKeyedCommandDto("setValue", key, Language: "ENGLISH", Value: value);
    }

    // ── the write ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Apply_ResolvesTheKeyAndWritesTheComposedText()
    {
        var fs = Files();
        var (apply, _, reload) = Build(fs);

        var result = await apply.Handle(
            new ApplyTranslationBatchParams(Path, HashOf(fs), [SetValue("TEXT_B", "Changed")]),
            CancellationToken.None);

        Assert.True(result.Success, result.Error);
        Assert.Equal("key,ENGLISH\nTEXT_A,Alpha\nTEXT_B,Changed\n", fs.File.ReadAllText(Path));
        Assert.Equal(HashOf(fs), result.NewContentHash);
        Assert.Equal(1, reload.LocalisationReloads);
    }

    [Fact]
    public async Task Apply_AddEntry_AppendsARow()
    {
        var fs = Files();
        var (apply, _, _) = Build(fs);

        var result = await apply.Handle(
            new ApplyTranslationBatchParams(Path, HashOf(fs),
            [
                new LocKeyedCommandDto("addEntry", "TEXT_C",
                    Values: [new LocValueDto("ENGLISH", "Gamma")])
            ]),
            CancellationToken.None);

        Assert.True(result.Success, result.Error);
        Assert.Contains("TEXT_C,Gamma", fs.File.ReadAllText(Path));
    }

    [Fact]
    public async Task Apply_DeleteEntry_RemovesTheRow()
    {
        var fs = Files();
        var (apply, _, _) = Build(fs);

        var result = await apply.Handle(
            new ApplyTranslationBatchParams(Path, HashOf(fs),
                [new LocKeyedCommandDto("deleteEntry", "TEXT_A")]),
            CancellationToken.None);

        Assert.True(result.Success, result.Error);
        Assert.DoesNotContain("TEXT_A", fs.File.ReadAllText(Path));
        Assert.Contains("TEXT_B,Beta", fs.File.ReadAllText(Path));
    }

    [Fact]
    public async Task Apply_EmptyBatch_SucceedsAndLeavesTheFileAlone()
    {
        var fs = Files();
        var (apply, _, _) = Build(fs);

        var result = await apply.Handle(
            new ApplyTranslationBatchParams(Path, HashOf(fs), []), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(Csv, fs.File.ReadAllText(Path));
        Assert.Equal(HashOf(fs), result.NewContentHash);
    }

    /// <summary>
    ///     The whole point of round-trip fidelity, reached through the keyed path: a one-value edit
    ///     leaves every other line alone.
    /// </summary>
    [Fact]
    public async Task Apply_LeavesUntouchedRowsByteIdentical()
    {
        const string quoted = "key,ENGLISH\nTEXT_A,\"Alpha, with comma\"\nTEXT_B,Beta\n";
        var fs = Files(quoted);
        var (apply, _, _) = Build(fs);

        await apply.Handle(
            new ApplyTranslationBatchParams(Path, HashOf(fs), [SetValue("TEXT_B", "Changed")]),
            CancellationToken.None);

        Assert.Contains("TEXT_A,\"Alpha, with comma\"", fs.File.ReadAllText(Path));
    }

    // ── addressing failures ──────────────────────────────────────────────────

    [Fact]
    public async Task Apply_UnknownKey_FailsWithoutWriting()
    {
        var fs = Files();
        var (apply, _, reload) = Build(fs);

        var result = await apply.Handle(
            new ApplyTranslationBatchParams(Path, HashOf(fs), [SetValue("TEXT_MISSING", "x")]),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("TEXT_MISSING", result.Error);
        Assert.Equal(Csv, fs.File.ReadAllText(Path));
        Assert.Equal(0, reload.LocalisationReloads);
    }

    [Fact]
    public async Task Apply_ReportsWhichStagedChangeFailed()
    {
        var fs = Files();
        var (apply, _, _) = Build(fs);

        var result = await apply.Handle(
            new ApplyTranslationBatchParams(Path, HashOf(fs),
                [SetValue("TEXT_A", "fine"), SetValue("TEXT_MISSING", "bad")]),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(1, result.FailedIndex);
        Assert.Equal(Csv, fs.File.ReadAllText(Path));
    }

    // ── the concurrency guard ────────────────────────────────────────────────

    [Fact]
    public async Task Apply_MissingHash_IsRejectedWithoutWriting()
    {
        var fs = Files();
        var (apply, _, reload) = Build(fs);

        var result = await apply.Handle(
            new ApplyTranslationBatchParams(Path, null, [SetValue("TEXT_A", "Changed")]),
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
            new ApplyTranslationBatchParams(Path, "not-the-current-hash", [SetValue("TEXT_A", "x")]),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("changed on disk", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(Csv, fs.File.ReadAllText(Path));
    }

    // ── the dry run ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Validate_AWorkableBatch_ReportsNothing()
    {
        var (_, validate, _) = Build(Files());

        var result = await validate.Handle(
            new ValidateTranslationBatchParams(Path, [SetValue("TEXT_A", "x")]),
            CancellationToken.None);

        Assert.Empty(result.Problems);
    }

    [Fact]
    public async Task Validate_AnUnknownKey_IsReportedAgainstTheChange()
    {
        var (_, validate, _) = Build(Files());

        var result = await validate.Handle(
            new ValidateTranslationBatchParams(Path, [SetValue("TEXT_MISSING", "x")]),
            CancellationToken.None);

        var problem = Assert.Single(result.Problems);
        Assert.Contains("Change 1", problem.Message);
    }

    [Fact]
    public async Task Validate_ADuplicateAlreadyInTheFile_IsReported()
    {
        var (_, validate, _) = Build(Files("key,ENGLISH\nTEXT_A,Alpha\nTEXT_A,Again\n"));

        var result = await validate.Handle(
            new ValidateTranslationBatchParams(Path, []), CancellationToken.None);

        var problem = Assert.Single(result.Problems);
        Assert.Equal("TEXT_A", problem.Key);
    }

    [Fact]
    public async Task Validate_WritesNothing()
    {
        var fs = Files();
        var (_, validate, _) = Build(fs);

        await validate.Handle(
            new ValidateTranslationBatchParams(Path, [SetValue("TEXT_A", "x")]),
            CancellationToken.None);

        Assert.Equal(Csv, fs.File.ReadAllText(Path));
    }

    // ── the feature gate ─────────────────────────────────────────────────────

    [Fact]
    public async Task Apply_WithTheToolDisabled_RefusesAndWritesNothing()
    {
        var fs = Files();
        var config = FakeLspConfigurationProvider.WithFeatures(
            new FeatureFlags { Tools = new ToolsFeatureFlags { Localisation = false } });
        var (apply, _, _) = Build(fs, config);

        var result = await apply.Handle(
            new ApplyTranslationBatchParams(Path, HashOf(fs), [SetValue("TEXT_A", "x")]),
            CancellationToken.None);

        Assert.Equal(LocalisationFeatureDisabled.Message, result.Error);
        Assert.Equal(Csv, fs.File.ReadAllText(Path));
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static (ApplyTranslationBatchHandler Apply, ValidateTranslationBatchHandler Validate,
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
            new ApplyTranslationBatchHandler(
                editor, reload, helper,
                NullLogger<ApplyTranslationBatchHandler>.Instance, configuration,
                new LocalisationWriteLedger(helper)),
            new ValidateTranslationBatchHandler(
                editor, helper, sp.GetRequiredService<ICrc32HashingService>(), configuration),
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

// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Diagnostics.Suppression;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Server.Project;
using PG.StarWarsGame.LSP.Server.Startup;
using PG.StarWarsGame.LSP.Server.Suppression;

namespace PG.StarWarsGame.LSP.Server.Tests.Suppression;

public sealed class GlobalSuppressionStoreTest
{
    private static readonly string PgprojPath = Path.Combine(Rooted("ws"), "mod.pgproj");
    private static readonly string SidecarPath = Path.Combine(Rooted("ws"), ".aetswg", "suppressions.json");

    private static string Rooted(string sub)
    {
        return Path.Combine(Path.GetPathRoot(Path.GetFullPath("."))!, sub);
    }

    private static (GlobalSuppressionStore Store, MockFileSystem Fs) Build(
        bool withProject = true, string? existingJson = null)
    {
        return BuildWith(new RecordingUserNotifier(), withProject, existingJson);
    }

    private static (GlobalSuppressionStore Store, MockFileSystem Fs) BuildWith(
        RecordingUserNotifier notifier, bool withProject = true, string? existingJson = null)
    {
        var files = new Dictionary<string, MockFileData> { [PgprojPath] = new("{}") };
        if (existingJson is not null) files[SidecarPath] = new(existingJson);

        var layers = withProject
            ? new[] { new ProjectLayer(1, "Mod", [], [], [], [], null, PgprojPath.Replace('\\', '/')) }
            : [];
        var fs = new MockFileSystem(files);
        var store = new GlobalSuppressionStore(
            new StubReloadService(WorkspaceConfiguration.Empty with { Layers = layers }),
            new FileHelper(fs), NullLogger<GlobalSuppressionStore>.Instance, notifier);
        return (store, fs);
    }

    [Fact]
    public void NoSidecar_YieldsNoSuppressions()
    {
        var (store, _) = Build();

        Assert.Empty(store.GetAll());
    }

    [Fact]
    public void Add_PersistsAndIsReadableByAFreshInstance()
    {
        var (store, fs) = Build();
        store.Add(SuppressionMatcher.ForId(DiagnosticIds.DuplicateSymbol), "intentional in this mod");

        Assert.True(fs.File.Exists(SidecarPath));
        Assert.Contains("intentional in this mod", fs.File.ReadAllText(SidecarPath));

        var reread = new GlobalSuppressionStore(
            new StubReloadService(WorkspaceConfiguration.Empty with
            {
                Layers = [new ProjectLayer(1, "Mod", [], [], [], [], null, PgprojPath.Replace('\\', '/'))]
            }),
            new FileHelper(fs), NullLogger<GlobalSuppressionStore>.Instance);

        Assert.Equal(
            [SuppressionMatcher.ForId(DiagnosticIds.DuplicateSymbol)], reread.GetAll());
    }

    // The "suppress everywhere" quick fix can be invoked twice on the same diagnostic; the second
    // must not grow the file.
    [Fact]
    public void Add_IsIdempotent()
    {
        var (store, _) = Build();
        store.Add(SuppressionMatcher.ForGroup(DiagnosticGroup.Assets));
        store.Add(SuppressionMatcher.ForGroup(DiagnosticGroup.Assets));

        Assert.Single(store.GetAll());
    }

    [Fact]
    public void Remove_TakesItOutAndPersists()
    {
        var (store, fs) = Build();
        store.Add(SuppressionMatcher.ForId(DiagnosticIds.StoryChain));
        store.Remove(SuppressionMatcher.ForId(DiagnosticIds.StoryChain));

        Assert.Empty(store.GetAll());
        Assert.DoesNotContain("aetswg-009", fs.File.ReadAllText(SidecarPath));
    }

    [Fact]
    public void Remove_UnknownMatcher_IsSilent()
    {
        var (store, _) = Build();

        store.Remove(SuppressionMatcher.ForId(DiagnosticIds.StoryChain));

        Assert.Empty(store.GetAll());
    }

    // Failing open - reporting everything - is the safe direction: a corrupt file must not silence
    // diagnostics wholesale, which would look exactly like "the mod is clean".
    [Fact]
    public void CorruptSidecar_SuppressesNothing()
    {
        var (store, _) = Build(existingJson: "{ this is not json");

        Assert.Empty(store.GetAll());
    }

    // One unreadable entry must not discard the entries around it.
    [Fact]
    public void UnreadableEntry_IsSkippedButOthersSurvive()
    {
        var (store, _) = Build(existingJson:
            """[{"id":"garbage"},{"id":"aetswg-010-0001"}]""");

        Assert.Equal([SuppressionMatcher.ForId(DiagnosticIds.DuplicateSymbol)], store.GetAll());
    }

    // The failure this prevents: suppressions.json is hand-editable and committed, so a typo in it
    // is as likely as one in a comment - and it was reported only to the server log, where the user
    // never sees it. Comments now report themselves; this closes the same hole here.
    [Fact]
    public void UnreadableEntry_IsReportedToTheUser()
    {
        var notifier = new RecordingUserNotifier();
        var (store, _) = BuildWith(notifier, existingJson:
            """[{"id":"garbage"},{"id":"aetswg-010-0001"}]""");

        store.GetAll();

        Assert.Contains("garbage", Assert.Single(notifier.Errors), StringComparison.Ordinal);
    }

    [Fact]
    public void CorruptSidecar_IsReportedToTheUser()
    {
        var notifier = new RecordingUserNotifier();
        var (store, _) = BuildWith(notifier, existingJson: "{ this is not json");

        store.GetAll();

        Assert.Single(notifier.Errors);
    }

    // GetAll runs on every publish. Reporting per call would balloon the user once per keystroke.
    [Fact]
    public void UnreadableEntry_IsReportedOnlyOnce()
    {
        var notifier = new RecordingUserNotifier();
        var (store, _) = BuildWith(notifier, existingJson: """[{"id":"garbage"}]""");

        store.GetAll();
        store.GetAll();
        store.GetAll();

        Assert.Single(notifier.Errors);
    }

    [Fact]
    public void ValidEntries_ReportNothing()
    {
        var notifier = new RecordingUserNotifier();
        var (store, _) = BuildWith(notifier, existingJson: """[{"id":"aetswg-010-0001"}]""");

        store.GetAll();

        Assert.Empty(notifier.Errors);
    }

    // Without a .pgproj there is nowhere to anchor the file; the store degrades to in-memory
    // rather than guessing at a location or throwing.
    [Fact]
    public void NoProjectFile_DegradesToInMemory()
    {
        var (store, fs) = Build(false);

        store.Add(SuppressionMatcher.ForId(DiagnosticIds.StoryChain));

        Assert.Single(store.GetAll());
        Assert.False(fs.File.Exists(SidecarPath));
    }

    private sealed class StubReloadService(WorkspaceConfiguration config) : IModProjectReloadService
    {
        public IReadOnlyList<string>? LastAssetRoots => null;
        public WorkspaceConfiguration? LastWorkspaceConfig => config;
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
            return Task.CompletedTask;
        }
    }
}

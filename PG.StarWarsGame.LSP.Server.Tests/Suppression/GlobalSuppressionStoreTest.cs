// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions.TestingHelpers;
using System.Text.Json.Nodes;
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

    // ── the envelope ─────────────────────────────────────────────────────────

    // Every test above feeds the bare array this file has always been, so they are the migration's
    // regression suite. This one states the other half: once read, it is written back versioned,
    // and the next release finds a document that says what it is.
    [Fact]
    public void LegacyArrayFile_IsRewrittenWithItsEnvelope()
    {
        var (store, fs) = Build(existingJson: """[{"id":"aetswg-010-0001","reason":"known"}]""");

        store.GetAll();

        var written = JsonNode.Parse(fs.File.ReadAllText(SidecarPath))!.AsObject();
        Assert.Equal("aetswg.Suppressions", (string?)written["_type"]);
        Assert.Equal(SuppressionsDocument.Version.ToString(), (string?)written["_typeVersion"]);
        Assert.Equal("aetswg-010-0001", (string?)written["entries"]![0]!["id"]);
        Assert.Equal("known", (string?)written["entries"]![0]!["reason"]);
    }

    [Fact]
    public void VersionedFile_IsReadWithoutBeingMigrated()
    {
        var (store, _) = Build(existingJson:
            """
            {
              "_type": "aetswg.Suppressions",
              "_typeVersion": "aetswg-1.0.0",
              "entries": [ { "id": "aetswg-010-0001" } ]
            }
            """);

        Assert.Equal([SuppressionMatcher.ForId(DiagnosticIds.DuplicateSymbol)], store.GetAll());
    }

    // Enforced upgrading, and the half of it that protects the file: an older build suppresses
    // nothing rather than guessing, and must not write its empty view over the newer document.
    [Fact]
    public void NewerFile_SuppressesNothingAndIsLeftIntact()
    {
        const string newer =
            """{ "_type": "aetswg.Suppressions", "_typeVersion": "aetswg-99.0.0", "entries": [] }""";
        var (store, fs) = Build(existingJson: newer);

        Assert.Empty(store.GetAll());
        store.Add(SuppressionMatcher.ForId(DiagnosticIds.StoryChain));

        Assert.Equal(newer, fs.File.ReadAllText(SidecarPath));
    }

    [Fact]
    public void NewerFile_IsReportedToTheUser()
    {
        var notifier = new RecordingUserNotifier();
        var (store, _) = BuildWith(notifier, existingJson:
            """{ "_type": "aetswg.Suppressions", "_typeVersion": "aetswg-99.0.0", "entries": [] }""");

        store.GetAll();

        Assert.Contains("99.0.0", Assert.Single(notifier.Errors), StringComparison.Ordinal);
    }

    // ── the shape guard ──────────────────────────────────────────────────────

    // Fails when the document's shape changes and its version does not. Update BOTH the pin and
    // the version when this goes red - that is the point of it going red.
    [Fact]
    public void Document_StillHasThePinnedShape()
    {
        Assert.True(
            SuppressionsDocument.Shape.Matches(typeof(SuppressionsDocument.Payload),
                SuppressionsDocument.Version),
            $"'{SuppressionsDocument.TypeName}' changed shape. Bump its version, add a migration "
            + "from the old one, and re-pin the signature.");
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

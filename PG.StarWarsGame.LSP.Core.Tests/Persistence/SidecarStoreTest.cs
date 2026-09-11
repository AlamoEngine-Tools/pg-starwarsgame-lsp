// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions.TestingHelpers;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Core.Persistence;
using PG.StarWarsGame.LSP.Core.Util;

namespace PG.StarWarsGame.LSP.Core.Tests.Persistence;

public sealed class SidecarStoreTest
{
    private const string TypeName = "aetswg.TestDocument";
    private const string SidecarPath = "C:/mod/.aetswg/test-document.json";

    /// <summary>The document under test - a payload property plus a list, like the real sidecars.</summary>
    private sealed record TestDocument
    {
        public string Title { get; init; } = string.Empty;
        public List<string> Entries { get; init; } = [];
    }

    /// <summary>Locates the sidecar, or reports that there is no project to hold one.</summary>
    private sealed class FixedLocator(string? path) : ISidecarLocator
    {
        public string? TryLocate(string fileName)
        {
            return path;
        }
    }

    /// <summary>A migration declared inline, so each test states the chain it is exercising.</summary>
    private sealed class InlineMigration(
        string from, string to, Func<JsonNode, JsonNode> migrate, string? userNotice = null)
        : IDocumentMigration
    {
        public string TypeName => SidecarStoreTest.TypeName;
        public TypeVersion From { get; } = Parse(from);
        public TypeVersion To { get; } = Parse(to);
        public string? UserNotice { get; } = userNotice;

        public JsonNode Migrate(JsonNode document)
        {
            return migrate(document);
        }

        private static TypeVersion Parse(string raw)
        {
            Assert.True(TypeVersion.TryParse(raw, out var version));
            return version;
        }
    }

    private static SidecarStore<TestDocument> Store(
        MockFileSystem fs, string currentVersion, string? path = SidecarPath,
        params IDocumentMigration[] migrations)
    {
        Assert.True(TypeVersion.TryParse(currentVersion, out var current));
        return new SidecarStore<TestDocument>(
            "test-document.json", TypeName, current, migrations, new FixedLocator(path),
            new FileHelper(fs), NullLogger.Instance, () => new TestDocument());
    }

    private static MockFileSystem WithFile(string contents)
    {
        var fs = new MockFileSystem();
        fs.AddFile(SidecarPath, new MockFileData(contents));
        return fs;
    }

    // ── the envelope ─────────────────────────────────────────────────────────

    [Fact]
    public void Save_StampsTypeAndVersionOnTheDocumentItself()
    {
        var fs = new MockFileSystem();
        var store = Store(fs, "aetswg-1.0.0");

        Assert.True(store.TrySave(new TestDocument { Title = "one", Entries = ["a"] }, out _));

        var written = JsonNode.Parse(fs.File.ReadAllText(SidecarPath))!.AsObject();
        Assert.Equal(TypeName, (string?)written["_type"]);
        Assert.Equal("aetswg-1.0.0", (string?)written["_typeVersion"]);
        Assert.Equal("one", (string?)written["title"]);
    }

    [Fact]
    public void Load_RoundTripsWhatSaveWrote()
    {
        var fs = new MockFileSystem();
        var store = Store(fs, "aetswg-1.0.0");
        store.TrySave(new TestDocument { Title = "one", Entries = ["a", "b"] }, out _);

        var loaded = Store(fs, "aetswg-1.0.0").Load();

        Assert.Equal(SidecarStatus.Loaded, loaded.Status);
        Assert.Equal("one", loaded.Value.Title);
        Assert.Equal(["a", "b"], loaded.Value.Entries);
    }

    // ── migration ────────────────────────────────────────────────────────────

    // A file written before any of this existed has no envelope at all. It is version zero, which
    // is what the first migration declares as its From - the whole reason that migration exists.
    [Fact]
    public void Load_DocumentWithNoEnvelope_IsMigratedFromZero()
    {
        var fs = WithFile("""{ "title": "legacy", "entries": ["a"] }""");
        var store = Store(fs, "aetswg-1.0.0", SidecarPath,
            new InlineMigration("aetswg-0.0.0", "aetswg-1.0.0", document =>
            {
                document["title"] = "migrated:" + (string?)document["title"];
                return document;
            }));

        var loaded = store.Load();

        Assert.Equal(SidecarStatus.Migrated, loaded.Status);
        Assert.Equal("migrated:legacy", loaded.Value.Title);
    }

    // The shape that forced this: a document whose root is an ARRAY cannot carry an envelope at
    // all, so the only way it ever gets one is a migration that sees it as it is. Both real
    // sidecars are bare collections today, which is the whole reason their v0 migration exists.
    [Fact]
    public void Load_DocumentWhoseRootIsAnArray_IsMigratedIntoAnObject()
    {
        var fs = WithFile("""["a", "b"]""");
        var store = Store(fs, "aetswg-1.0.0", SidecarPath,
            new InlineMigration("aetswg-0.0.0", "aetswg-1.0.0", document =>
                new JsonObject { ["title"] = "wrapped", ["entries"] = document.DeepClone() }));

        var loaded = store.Load();

        Assert.Equal(SidecarStatus.Migrated, loaded.Status);
        Assert.Equal("wrapped", loaded.Value.Title);
        Assert.Equal(["a", "b"], loaded.Value.Entries);
    }

    // ...and having been wrapped once, it is an ordinary versioned document from then on.
    [Fact]
    public void Load_ArrayRootedDocument_IsRewrittenWithItsEnvelope()
    {
        var fs = WithFile("""["a"]""");
        Store(fs, "aetswg-1.0.0", SidecarPath,
            new InlineMigration("aetswg-0.0.0", "aetswg-1.0.0", document =>
                new JsonObject { ["entries"] = document.DeepClone() })).Load();

        var written = JsonNode.Parse(fs.File.ReadAllText(SidecarPath))!.AsObject();
        Assert.Equal(TypeName, (string?)written["_type"]);
        Assert.Equal("aetswg-1.0.0", (string?)written["_typeVersion"]);
        Assert.Equal(["a"], written["entries"]!.AsArray().Select(n => (string?)n));
    }

    // A document carrying no identity is NOT a document at version zero waiting to be migrated.
    // It is only read as one during the initial typing of that document - the window in which a
    // migration from zero exists, because that migration is what the unversioned file is for. Once
    // that handler is retired, a file with no identity is a file we cannot place, and adopting it
    // would mean guessing which shape it holds.
    [Fact]
    public void Load_NoEnvelopeAndNoMigrationFromZero_IsNotMigratedAtAll()
    {
        var fs = WithFile("""{ "title": "unidentified", "entries": [] }""");
        var store = Store(fs, "aetswg-2.0.0", SidecarPath,
            new InlineMigration("aetswg-1.0.0", "aetswg-2.0.0", document => document));

        var loaded = store.Load();

        Assert.Equal(SidecarStatus.Defaulted, loaded.Status);
        Assert.Equal(string.Empty, loaded.Value.Title);
        // And it says what is actually wrong: the file carries no identity. Calling it "version
        // 0.0.0 with no migration from there" would describe it as a document at a version, which
        // is the reading this rule rejects.
        Assert.NotNull(loaded.Message);
        Assert.DoesNotContain("0.0.0", loaded.Message, StringComparison.Ordinal);
        Assert.Contains("_typeVersion", loaded.Message, StringComparison.Ordinal);
    }

    // ...and the file it declined to read is left exactly as it was.
    [Fact]
    public void Load_NoEnvelopeAndNoMigrationFromZero_LeavesTheFileAlone()
    {
        const string original = """{ "title": "unidentified", "entries": [] }""";
        var fs = WithFile(original);
        Store(fs, "aetswg-2.0.0", SidecarPath,
            new InlineMigration("aetswg-1.0.0", "aetswg-2.0.0", document => document)).Load();

        Assert.Equal(original, fs.File.ReadAllText(SidecarPath));
    }

    [Fact]
    public void Load_ChainOfTwo_RunsThemInOrder()
    {
        var fs = WithFile("""{ "_type": "aetswg.TestDocument", "_typeVersion": "aetswg-1.0.0", "title": "start" }""");
        var store = Store(fs, "aetswg-3.0.0", SidecarPath,
            // Deliberately registered out of order: the chain follows the versions, not the list.
            new InlineMigration("aetswg-2.0.0", "aetswg-3.0.0", document =>
            {
                document["title"] = (string?)document["title"] + "-second";
                return document;
            }),
            new InlineMigration("aetswg-1.0.0", "aetswg-2.0.0", document =>
            {
                document["title"] = (string?)document["title"] + "-first";
                return document;
            }));

        var loaded = store.Load();

        Assert.Equal(SidecarStatus.Migrated, loaded.Status);
        Assert.Equal("start-first-second", loaded.Value.Title);
    }

    [Fact]
    public void Load_MigratedDocument_IsWrittenBackAtTheCurrentVersion()
    {
        var fs = WithFile("""{ "title": "legacy" }""");
        Store(fs, "aetswg-1.0.0", SidecarPath,
            new InlineMigration("aetswg-0.0.0", "aetswg-1.0.0", document => document)).Load();

        var written = JsonNode.Parse(fs.File.ReadAllText(SidecarPath))!.AsObject();
        Assert.Equal("aetswg-1.0.0", (string?)written["_typeVersion"]);
    }

    [Fact]
    public void Load_MigrationWithANotice_SurfacesItToTheCaller()
    {
        var fs = WithFile("""{ "title": "legacy" }""");
        var store = Store(fs, "aetswg-1.0.0", SidecarPath,
            new InlineMigration("aetswg-0.0.0", "aetswg-1.0.0", document => document,
                "Event positions now key on the project-relative path."));

        var loaded = store.Load();

        Assert.Contains("project-relative path", Assert.Single(loaded.UserNotices));
    }

    // A gap in the chain is a bug in our registration, not a fact about the file - it must not be
    // papered over by loading the document at a version whose shape it does not have.
    [Fact]
    public void Load_GapInTheChain_DoesNotPretendTheDocumentIsCurrent()
    {
        var fs = WithFile("""{ "_type": "aetswg.TestDocument", "_typeVersion": "aetswg-1.0.0", "title": "start" }""");
        var store = Store(fs, "aetswg-3.0.0", SidecarPath,
            new InlineMigration("aetswg-1.0.0", "aetswg-2.0.0", document => document));

        var loaded = store.Load();

        Assert.Equal(SidecarStatus.Defaulted, loaded.Status);
        Assert.NotNull(loaded.Message);
    }

    // ── refuse newer ─────────────────────────────────────────────────────────

    // Upgrading is enforced: an older extension meeting a newer document declines it rather than
    // reading it as best it can.
    [Fact]
    public void Load_VersionAboveOurs_IsRefusedWithAMessage()
    {
        var fs = WithFile("""{ "_type": "aetswg.TestDocument", "_typeVersion": "aetswg-2.0.0", "title": "future" }""");

        var loaded = Store(fs, "aetswg-1.0.0").Load();

        Assert.Equal(SidecarStatus.RefusedNewer, loaded.Status);
        Assert.NotNull(loaded.Message);
        Assert.Contains("2.0.0", loaded.Message);
    }

    // Refuse-newer is only half a rule. Without the other half: read fails, caller falls back to
    // defaults, caller saves - and the newer document is gone.
    [Fact]
    public void Save_AfterRefusingANewerDocument_IsBlockedAndLeavesTheFileIntact()
    {
        const string original =
            """{ "_type": "aetswg.TestDocument", "_typeVersion": "aetswg-2.0.0", "title": "future" }""";
        var fs = WithFile(original);
        var store = Store(fs, "aetswg-1.0.0");
        store.Load();

        var saved = store.TrySave(new TestDocument { Title = "defaults" }, out var error);

        Assert.False(saved);
        Assert.NotNull(error);
        Assert.Equal(original, fs.File.ReadAllText(SidecarPath));
    }

    // A patch bump refuses exactly like a major one - enforced upgrading is not tiered.
    [Fact]
    public void Load_PatchAboveOurs_IsRefusedToo()
    {
        var fs = WithFile("""{ "_type": "aetswg.TestDocument", "_typeVersion": "aetswg-1.0.1", "title": "future" }""");

        Assert.Equal(SidecarStatus.RefusedNewer, Store(fs, "aetswg-1.0.0").Load().Status);
    }

    // ── damage and absence ───────────────────────────────────────────────────

    [Fact]
    public void Load_CorruptFile_GivesDefaultsAndSaysSo()
    {
        var fs = WithFile("{ not json at all");

        var loaded = Store(fs, "aetswg-1.0.0").Load();

        Assert.Equal(SidecarStatus.Defaulted, loaded.Status);
        Assert.Equal(string.Empty, loaded.Value.Title);
        Assert.NotNull(loaded.Message);
    }

    // A file holding some OTHER document is not this document at an odd version; reading it as one
    // would silently adopt whatever fields happen to match.
    [Fact]
    public void Load_DifferentTypeName_IsTreatedAsDamage()
    {
        var fs = WithFile("""{ "_type": "aetswg.SomethingElse", "_typeVersion": "aetswg-1.0.0", "title": "x" }""");

        var loaded = Store(fs, "aetswg-1.0.0").Load();

        Assert.Equal(SidecarStatus.Defaulted, loaded.Status);
        Assert.Equal(string.Empty, loaded.Value.Title);
    }

    // The version's namespace names who owns the document's shape. A file carrying someone else's
    // namespace cannot be ordered against ours, so it is damage rather than an older or newer us.
    [Fact]
    public void Load_ForeignNamespace_IsTreatedAsDamage()
    {
        var fs = WithFile("""{ "_type": "aetswg.TestDocument", "_typeVersion": "scout-1.0.0", "title": "x" }""");

        var loaded = Store(fs, "aetswg-1.0.0").Load();

        Assert.Equal(SidecarStatus.Defaulted, loaded.Status);
        Assert.NotNull(loaded.Message);
    }

    [Fact]
    public void Load_UnreadableVersion_IsTreatedAsDamage()
    {
        var fs = WithFile("""{ "_type": "aetswg.TestDocument", "_typeVersion": "banana", "title": "x" }""");

        Assert.Equal(SidecarStatus.Defaulted, Store(fs, "aetswg-1.0.0").Load().Status);
    }

    [Fact]
    public void Load_NoFileYet_GivesDefaultsWithoutComplaining()
    {
        var loaded = Store(new MockFileSystem(), "aetswg-1.0.0").Load();

        Assert.Equal(SidecarStatus.Defaulted, loaded.Status);
        Assert.Null(loaded.Message);
    }

    // ── no project ───────────────────────────────────────────────────────────

    // Without a .pgproj there is nowhere to put the file, so the store keeps the value for the
    // session rather than failing - the behaviour all three existing sidecars already have.
    [Fact]
    public void NoProject_KeepsTheValueInMemoryAndWritesNothing()
    {
        var fs = new MockFileSystem();
        var store = Store(fs, "aetswg-1.0.0", null);

        Assert.True(store.TrySave(new TestDocument { Title = "session only" }, out _));

        Assert.Empty(fs.AllFiles);
        Assert.Equal("session only", store.Load().Value.Title);
    }
}

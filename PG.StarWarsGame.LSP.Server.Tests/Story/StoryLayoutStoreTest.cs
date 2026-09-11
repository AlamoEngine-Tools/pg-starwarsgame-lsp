// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions.TestingHelpers;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Core.Persistence;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Server.Persistence;
using PG.StarWarsGame.LSP.Server.Project;
using PG.StarWarsGame.LSP.Server.Story;

namespace PG.StarWarsGame.LSP.Server.Tests.Story;

public sealed class StoryLayoutStoreTest
{
    private static readonly string ProjectDir = Rooted("ws");
    private static readonly string PgprojPath = Path.Combine(ProjectDir, "mod.pgproj");
    private static readonly string XmlDir = ProjectDir.Replace('\\', '/') + "/data/xml";

    /// <summary>The graph these fixtures arrange. A layout belongs to a campaign FACTION.</summary>
    private static readonly StoryModelKey Rebel = new("GC", "Rebel");

    private static readonly StoryModelKey Empire = new("GC", "Empire");

    /// <summary>A thread of the campaign, as the server holds it: an absolute document URI.</summary>
    private static string Thread(string name)
    {
        return XmlDir + "/" + name;
    }

    /// <summary>A node the graph is holding - what a stored key is named against.</summary>
    private static StoryLayoutNode Node(string threadName, string eventName)
    {
        return new StoryLayoutNode(Thread(threadName), eventName);
    }

    private static string Rooted(string sub)
    {
        return Path.Combine(Path.GetPathRoot(Path.GetFullPath("."))!, sub);
    }

    private static WorkspaceConfiguration Config(bool withProject = true)
    {
        var layers = withProject
            ? new[]
            {
                new ProjectLayer(1, "Mod", [XmlDir], [], [], [], null, PgprojPath.Replace('\\', '/'))
            }
            : [];
        return WorkspaceConfiguration.Empty with { Layers = layers };
    }

    private static (StoryLayoutStore Store, MockFileSystem Fs) Build(
        bool withProject = true, params string[] threadFiles)
    {
        var files = new Dictionary<string, MockFileData> { [PgprojPath] = new("{}") };
        foreach (var thread in threadFiles) files[thread] = new MockFileData("<Story/>");

        var fs = new MockFileSystem(files);
        return (NewStore(fs, withProject), fs);
    }

    private static StoryLayoutStore NewStore(MockFileSystem fs, bool withProject = true)
    {
        return new StoryLayoutStore(
            new StubReloadService(Config(withProject)), new FileHelper(fs),
            NullLogger<StoryLayoutStore>.Instance);
    }

    private static string Sidecar(MockFileSystem fs)
    {
        return fs.AllFiles.Single(f => f.EndsWith("story-layout.json", StringComparison.Ordinal));
    }

    // ── round trip ───────────────────────────────────────────────────────────

    [Fact]
    public void Get_UnknownCampaign_ReturnsEmpty()
    {
        var (store, _) = Build();

        Assert.Empty(store.Get(Rebel, [Node("story_main.xml", "Start")]));
    }

    [Fact]
    public void Set_PersistsToTheAetswgSidecar()
    {
        var (store, fs) = Build();

        store.Set(Rebel, [new StoryLayoutEntry(Thread("story_main.xml"), "Start", 10, 20)]);

        var entry = Assert.Single(store.Get(Rebel, [Node("story_main.xml", "Start")]));
        Assert.Equal(10, entry.X);
        Assert.Equal(Thread("story_main.xml"), entry.ThreadUri);
        Assert.True(fs.File.Exists(Sidecar(fs)));
    }

    [Fact]
    public void Set_UpsertsByThreadAndEventName_KeepsOthers()
    {
        var (store, _) = Build();
        store.Set(Rebel, [
            new StoryLayoutEntry(Thread("story_main.xml"), "Start", 1, 1),
            new StoryLayoutEntry(Thread("story_main.xml"), "Next", 2, 2)
        ]);

        store.Set(Rebel, [new StoryLayoutEntry(Thread("story_main.xml"), "START", 9, 9)]);

        var entries = store.Get(Rebel, [Node("story_main.xml", "Start"), Node("story_main.xml", "Next")]);
        Assert.Equal(2, entries.Count);
        Assert.Equal(9, entries.Single(e => e.EventName.Equals("Start", StringComparison.OrdinalIgnoreCase)).X);
        Assert.Equal(2, entries.Single(e => e.EventName == "Next").X);
    }

    [Fact]
    public void RoundTrip_SurvivesAFreshStoreInstance()
    {
        var (store, fs) = Build();
        store.Set(Rebel, [new StoryLayoutEntry(Thread("story_main.xml"), "Start", 5, 6)]);

        var fresh = NewStore(fs);

        Assert.Equal(5, Assert.Single(fresh.Get(Rebel, [Node("story_main.xml", "Start")])).X);
    }

    // A thread the graph no longer holds cannot be named from its key, and does not come back.
    // The entry stays in the file: an event that returns keeps the position it had.
    [Fact]
    public void Get_ThreadNotInTheGraph_IsNotReturnedButIsKept()
    {
        var (store, fs) = Build();
        store.Set(Rebel, [new StoryLayoutEntry(Thread("story_main.xml"), "Start", 5, 6)]);

        Assert.Empty(store.Get(Rebel, [Node("other.xml", "Start")]));
        Assert.Equal(5, Assert.Single(NewStore(fs).Get(Rebel, [Node("story_main.xml", "Start")])).X);
    }

    [Fact]
    public void NoProject_DegradesToInMemory()
    {
        var (store, fs) = Build(false);

        store.Set(Rebel, [new StoryLayoutEntry(Thread("story_main.xml"), "Start", 3, 4)]);

        Assert.DoesNotContain(fs.AllFiles, f => f.EndsWith("story-layout.json", StringComparison.Ordinal));
    }

    [Fact]
    public void CorruptSidecar_StartsEmptyInsteadOfThrowing()
    {
        var (store, fs) = Build();
        store.Set(Rebel, [new StoryLayoutEntry(Thread("story_main.xml"), "Start", 1, 1)]);
        fs.File.WriteAllText(Sidecar(fs), "{ not json");

        Assert.Empty(NewStore(fs).Get(Rebel, [Node("story_main.xml", "Start")]));
    }

    // ── faction scoping ──────────────────────────────────────────────────────

    [Fact]
    public void Get_OtherFactionOfTheSameCampaign_SeesNothing()
    {
        // Keyed by the campaign alone, arranging the Rebel chain moved the Empire chain's nodes -
        // and the two graphs share neither events nor a canvas.
        var (store, _) = Build();

        store.Set(Rebel, [new StoryLayoutEntry(Thread("story_rebel.xml"), "Opening", 10, 20)]);

        Assert.Empty(store.Get(Empire, [Node("story_rebel.xml", "Opening")]));
    }

    [Fact]
    public void Set_EachFactionKeepsItsOwnPositions()
    {
        var (store, _) = Build();

        store.Set(Rebel, [new StoryLayoutEntry(Thread("T.xml"), "E", 1, 1)]);
        store.Set(Empire, [new StoryLayoutEntry(Thread("T.xml"), "E", 2, 2)]);

        Assert.Equal(1, Assert.Single(store.Get(Rebel, [Node("T.xml", "E")])).X);
        Assert.Equal(2, Assert.Single(store.Get(Empire, [Node("T.xml", "E")])).X);
    }

    // ── version zero: the file as it shipped ─────────────────────────────────

    [Fact]
    public void LegacySidecar_IsMigratedFromBaseNamesToKeys()
    {
        var (_, fs) = Build(threadFiles: [Thread("story_main.xml")]);
        fs.AddFile(Path.Combine(ProjectDir, ".aetswg", "story-layout.json"), new MockFileData(
            """{"GC/Rebel":[{"file":"story_main.xml","eventName":"Start","x":7,"y":8}]}"""));

        var entry = Assert.Single(NewStore(fs).Get(Rebel, [Node("story_main.xml", "Start")]));

        Assert.Equal(7, entry.X);
        Assert.Equal(Thread("story_main.xml"), entry.ThreadUri);
    }

    [Fact]
    public void LegacySidecar_IsRewrittenWithItsEnvelopeAndKeys()
    {
        var (_, fs) = Build(threadFiles: [Thread("story_main.xml")]);
        fs.AddFile(Path.Combine(ProjectDir, ".aetswg", "story-layout.json"), new MockFileData(
            """{"GC/Rebel":[{"file":"story_main.xml","eventName":"Start","x":7,"y":8}]}"""));

        NewStore(fs).Get(Rebel, [Node("story_main.xml", "Start")]);

        var written = JsonNode.Parse(fs.File.ReadAllText(Sidecar(fs)))!.AsObject();
        Assert.Equal("aetswg.StoryLayout", (string?)written["_type"]);
        Assert.Equal(StoryLayoutDocument.Version.ToString(), (string?)written["_typeVersion"]);

        var bucket = ProjectDocumentKeys.GraphKey("GC", "Rebel").ToString();
        var migrated = written["graphs"]![bucket]!.AsArray().Single()!.AsObject();
        Assert.Equal(
            DocumentKey.Composite("data/xml/story_main.xml", "Start").ToString(), (string?)migrated["key"]);
        // The event name is part of the key now, not a field beside it.
        Assert.Null(migrated["eventName"]);
        // Nothing in the file names a path any more.
        Assert.DoesNotContain("story_main", fs.File.ReadAllText(Sidecar(fs)), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Get_LegacySidecarWrittenBeforeFactionScoping_IsStillRead()
    {
        // Those sidecars key by the campaign alone. Both factions read it once - an entry is
        // (thread, eventName), so each graph picks up only the positions of nodes it actually has -
        // and the next Set writes the scoped key.
        var (_, fs) = Build(threadFiles: [Thread("story_main.xml")]);
        fs.AddFile(Path.Combine(ProjectDir, ".aetswg", "story-layout.json"), new MockFileData(
            """{"GC":[{"file":"story_main.xml","eventName":"Start","x":7,"y":8}]}"""));

        Assert.Equal(7, Assert.Single(NewStore(fs).Get(Rebel, [Node("story_main.xml", "Start")])).X);
    }

    // Two threads of that name, so the base name names neither. Dropped rather than guessed: the
    // alternative is moving somebody's nodes onto the wrong graph.
    [Fact]
    public void LegacySidecar_AmbiguousFileName_DropsTheEntryRatherThanGuessing()
    {
        var (_, fs) = Build(threadFiles:
            [XmlDir + "/story/story_main.xml", XmlDir + "/campaign/story_main.xml"]);
        fs.AddFile(Path.Combine(ProjectDir, ".aetswg", "story-layout.json"), new MockFileData(
            """{"GC/Rebel":[{"file":"story_main.xml","eventName":"Start","x":7,"y":8}]}"""));

        Assert.Empty(NewStore(fs).Get(Rebel, [new StoryLayoutNode(XmlDir + "/story/story_main.xml", "Start")]));
    }

    // ── the shape guard ──────────────────────────────────────────────────────

    [Fact]
    public void Document_StillHasThePinnedShape()
    {
        Assert.True(
            StoryLayoutDocument.Shape.Matches(typeof(StoryLayoutDocument.Payload),
                StoryLayoutDocument.Version),
            $"'{StoryLayoutDocument.TypeName}' changed shape. Bump its version, add a migration "
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

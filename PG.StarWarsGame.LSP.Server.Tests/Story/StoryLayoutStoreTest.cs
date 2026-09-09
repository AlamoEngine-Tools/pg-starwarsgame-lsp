// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Server.Project;
using PG.StarWarsGame.LSP.Server.Story;

namespace PG.StarWarsGame.LSP.Server.Tests.Story;

public sealed class StoryLayoutStoreTest
{
    private static readonly string PgprojPath = Path.Combine(Rooted("ws"), "mod.pgproj");

    /// <summary>The graph these fixtures arrange. A layout belongs to a campaign FACTION.</summary>
    private static readonly StoryModelKey Rebel = new("GC", "Rebel");

    private static readonly StoryModelKey Empire = new("GC", "Empire");

    private static string Rooted(string sub)
    {
        return Path.Combine(Path.GetPathRoot(Path.GetFullPath("."))!, sub);
    }

    private static (StoryLayoutStore Store, MockFileSystem Fs) Build(bool withProject = true)
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [PgprojPath] = new("{}")
        });
        var layers = withProject
            ? new[] { new ProjectLayer(1, "Mod", [], [], [], [], null, PgprojPath.Replace('\\', '/')) }
            : [];
        var config = WorkspaceConfiguration.Empty with { Layers = layers };
        var store = new StoryLayoutStore(
            new StubReloadService(config), new FileHelper(fs), NullLogger<StoryLayoutStore>.Instance);
        return (store, fs);
    }

    [Fact]
    public void Get_UnknownCampaign_ReturnsEmpty()
    {
        var (store, _) = Build();

        Assert.Empty(store.Get(Rebel));
    }

    [Fact]
    public void Set_PersistsToTheAetswgSidecar()
    {
        var (store, fs) = Build();

        store.Set(Rebel, [new StoryLayoutEntry("story_main.xml", "Start", 10, 20)]);

        var sidecar = fs.AllFiles.Single(f => f.EndsWith("story-layout.json", StringComparison.Ordinal));
        Assert.Contains(".aetswg", sidecar);
        var entry = Assert.Single(store.Get(Rebel));
        Assert.Equal(("story_main.xml", "Start", 10d, 20d), (entry.File, entry.EventName, entry.X, entry.Y));
    }

    [Fact]
    public void Set_UpsertsByFileAndEventName_KeepsOthers()
    {
        var (store, _) = Build();
        store.Set(Rebel, [
            new StoryLayoutEntry("story_main.xml", "Start", 1, 1),
            new StoryLayoutEntry("story_main.xml", "Next", 2, 2)
        ]);

        store.Set(Rebel, [new StoryLayoutEntry("story_main.xml", "START", 9, 9)]);

        var entries = store.Get(Rebel);
        Assert.Equal(2, entries.Count);
        Assert.Equal(9, entries.Single(e => e.EventName.Equals("Start", StringComparison.OrdinalIgnoreCase)).X);
        Assert.Equal(2, entries.Single(e => e.EventName == "Next").X);
    }

    [Fact]
    public void RoundTrip_SurvivesAFreshStoreInstance()
    {
        var (store, fs) = Build();
        store.Set(Rebel, [new StoryLayoutEntry("story_main.xml", "Start", 5, 6)]);

        var config = WorkspaceConfiguration.Empty with
        {
            Layers = [new ProjectLayer(1, "Mod", [], [], [], [], null, PgprojPath.Replace('\\', '/'))]
        };
        var fresh = new StoryLayoutStore(
            new StubReloadService(config), new FileHelper(fs), NullLogger<StoryLayoutStore>.Instance);

        var entry = Assert.Single(fresh.Get(Rebel));
        Assert.Equal(5, entry.X);
    }

    [Fact]
    public void NoProject_DegradesToInMemory()
    {
        var (store, fs) = Build(false);

        store.Set(Rebel, [new StoryLayoutEntry("story_main.xml", "Start", 3, 4)]);

        Assert.Single(store.Get(Rebel));
        Assert.DoesNotContain(fs.AllFiles, f => f.EndsWith("story-layout.json", StringComparison.Ordinal));
    }

    [Fact]
    public void CorruptSidecar_StartsEmptyInsteadOfThrowing()
    {
        var (store, fs) = Build();
        store.Set(Rebel, [new StoryLayoutEntry("story_main.xml", "Start", 1, 1)]);
        var sidecar = fs.AllFiles.Single(f => f.EndsWith("story-layout.json", StringComparison.Ordinal));
        fs.File.WriteAllText(sidecar, "{ not json");

        var config = WorkspaceConfiguration.Empty with
        {
            Layers = [new ProjectLayer(1, "Mod", [], [], [], [], null, PgprojPath.Replace('\\', '/'))]
        };
        var fresh = new StoryLayoutStore(
            new StubReloadService(config), new FileHelper(fs), NullLogger<StoryLayoutStore>.Instance);

        Assert.Empty(fresh.Get(Rebel));
    }

    // ── faction scoping ──────────────────────────────────────────────────────

    [Fact]
    public void Get_OtherFactionOfTheSameCampaign_SeesNothing()
    {
        // Keyed by the campaign alone, arranging the Rebel chain moved the Empire chain's nodes -
        // and the two graphs share neither events nor a canvas.
        var (store, _) = Build();

        store.Set(Rebel, [new StoryLayoutEntry("story_rebel.xml", "Opening", 10, 20)]);

        Assert.Empty(store.Get(Empire));
    }

    [Fact]
    public void Set_EachFactionKeepsItsOwnPositions()
    {
        var (store, _) = Build();

        store.Set(Rebel, [new StoryLayoutEntry("T.xml", "E", 1, 1)]);
        store.Set(Empire, [new StoryLayoutEntry("T.xml", "E", 2, 2)]);

        Assert.Equal(1, Assert.Single(store.Get(Rebel)).X);
        Assert.Equal(2, Assert.Single(store.Get(Empire)).X);
    }

    [Fact]
    public void Get_SidecarWrittenBeforeFactionScoping_IsStillRead()
    {
        // Those sidecars key by the campaign alone. Both factions read it once - an entry is
        // (file, eventName), so each graph picks up only the positions of nodes it actually has -
        // and the next Set writes the scoped key.
        var (store, fs) = Build();
        store.Set(Rebel, [new StoryLayoutEntry("story_main.xml", "Start", 7, 8)]);
        var sidecar = fs.AllFiles.Single(f => f.EndsWith("story-layout.json", StringComparison.Ordinal));
        fs.File.WriteAllText(sidecar,
            "{\"GC\":[{\"file\":\"story_main.xml\",\"eventName\":\"Start\",\"x\":7,\"y\":8}]}");

        var config = WorkspaceConfiguration.Empty with
        {
            Layers = [new ProjectLayer(1, "Mod", [], [], [], [], null, PgprojPath.Replace('\\', '/'))]
        };
        var fresh = new StoryLayoutStore(
            new StubReloadService(config), new FileHelper(fs), NullLogger<StoryLayoutStore>.Instance);

        Assert.Equal(7, Assert.Single(fresh.Get(Rebel)).X);
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

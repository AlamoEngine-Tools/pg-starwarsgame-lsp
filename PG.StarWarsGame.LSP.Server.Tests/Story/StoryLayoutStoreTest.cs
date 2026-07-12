// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Server.Project;
using PG.StarWarsGame.LSP.Server.Story;

namespace PG.StarWarsGame.LSP.Server.Tests.Story;

public sealed class StoryLayoutStoreTest
{
    private static string Rooted(string sub)
    {
        return Path.Combine(Path.GetPathRoot(Path.GetFullPath("."))!, sub);
    }

    private static readonly string PgprojPath = Path.Combine(Rooted("ws"), "mod.pgproj");

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

        Assert.Empty(store.Get("GC"));
    }

    [Fact]
    public void Set_PersistsToTheAetswgSidecar()
    {
        var (store, fs) = Build();

        store.Set("GC", [new StoryLayoutEntry("story_main.xml", "Start", 10, 20)]);

        var sidecar = fs.AllFiles.Single(f => f.EndsWith("story-layout.json", StringComparison.Ordinal));
        Assert.Contains(".aetswg", sidecar);
        var entry = Assert.Single(store.Get("GC"));
        Assert.Equal(("story_main.xml", "Start", 10d, 20d), (entry.File, entry.EventName, entry.X, entry.Y));
    }

    [Fact]
    public void Set_UpsertsByFileAndEventName_KeepsOthers()
    {
        var (store, _) = Build();
        store.Set("GC", [
            new StoryLayoutEntry("story_main.xml", "Start", 1, 1),
            new StoryLayoutEntry("story_main.xml", "Next", 2, 2)
        ]);

        store.Set("GC", [new StoryLayoutEntry("story_main.xml", "START", 9, 9)]);

        var entries = store.Get("GC");
        Assert.Equal(2, entries.Count);
        Assert.Equal(9, entries.Single(e => e.EventName.Equals("Start", StringComparison.OrdinalIgnoreCase)).X);
        Assert.Equal(2, entries.Single(e => e.EventName == "Next").X);
    }

    [Fact]
    public void RoundTrip_SurvivesAFreshStoreInstance()
    {
        var (store, fs) = Build();
        store.Set("GC", [new StoryLayoutEntry("story_main.xml", "Start", 5, 6)]);

        var config = WorkspaceConfiguration.Empty with
        {
            Layers = [new ProjectLayer(1, "Mod", [], [], [], [], null, PgprojPath.Replace('\\', '/'))]
        };
        var fresh = new StoryLayoutStore(
            new StubReloadService(config), new FileHelper(fs), NullLogger<StoryLayoutStore>.Instance);

        var entry = Assert.Single(fresh.Get("GC"));
        Assert.Equal(5, entry.X);
    }

    [Fact]
    public void NoProject_DegradesToInMemory()
    {
        var (store, fs) = Build(withProject: false);

        store.Set("GC", [new StoryLayoutEntry("story_main.xml", "Start", 3, 4)]);

        Assert.Single(store.Get("GC"));
        Assert.DoesNotContain(fs.AllFiles, f => f.EndsWith("story-layout.json", StringComparison.Ordinal));
    }

    [Fact]
    public void CorruptSidecar_StartsEmptyInsteadOfThrowing()
    {
        var (store, fs) = Build();
        store.Set("GC", [new StoryLayoutEntry("story_main.xml", "Start", 1, 1)]);
        var sidecar = fs.AllFiles.Single(f => f.EndsWith("story-layout.json", StringComparison.Ordinal));
        fs.File.WriteAllText(sidecar, "{ not json");

        var config = WorkspaceConfiguration.Empty with
        {
            Layers = [new ProjectLayer(1, "Mod", [], [], [], [], null, PgprojPath.Replace('\\', '/'))]
        };
        var fresh = new StoryLayoutStore(
            new StubReloadService(config), new FileHelper(fs), NullLogger<StoryLayoutStore>.Instance);

        Assert.Empty(fresh.Get("GC"));
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

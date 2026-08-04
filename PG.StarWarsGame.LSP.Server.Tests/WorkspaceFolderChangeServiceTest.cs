// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Server.Project;

namespace PG.StarWarsGame.LSP.Server.Tests;

public sealed class WorkspaceFolderChangeServiceTest
{
    private static WorkspaceFolderChangeService Build(RecordingReloadService reload)
    {
        return new WorkspaceFolderChangeService(reload, NullLogger.Instance);
    }

    [Fact]
    public async Task ApplyAsync_FolderAdded_ReloadsWithTheAddedRootIncluded()
    {
        var reload = new RecordingReloadService { LastWorkspaceRoots = ["/ws/moda"] };
        var service = Build(reload);

        await service.ApplyAsync(["/ws/modb"], [], CancellationToken.None);

        Assert.Equal(["/ws/moda", "/ws/modb"], reload.LoadedRoots);
    }

    [Fact]
    public async Task ApplyAsync_FolderRemoved_ReloadsWithoutIt()
    {
        var reload = new RecordingReloadService { LastWorkspaceRoots = ["/ws/moda", "/ws/modb"] };
        var service = Build(reload);

        await service.ApplyAsync([], ["/ws/modb"], CancellationToken.None);

        Assert.Equal(["/ws/moda"], reload.LoadedRoots);
    }

    [Fact]
    public async Task ApplyAsync_AddAndRemoveInOneNotification_AppliesBoth()
    {
        var reload = new RecordingReloadService { LastWorkspaceRoots = ["/ws/moda", "/ws/modb"] };
        var service = Build(reload);

        await service.ApplyAsync(["/ws/modc"], ["/ws/moda"], CancellationToken.None);

        Assert.Equal(["/ws/modb", "/ws/modc"], reload.LoadedRoots);
    }

    [Fact]
    public async Task ApplyAsync_AddingAFolderAlreadyKnown_DoesNotDuplicateIt()
    {
        // modb makes the set genuinely change, so a reload does happen - and moda must appear once.
        var reload = new RecordingReloadService { LastWorkspaceRoots = ["/ws/moda"] };
        var service = Build(reload);

        await service.ApplyAsync(["/ws/moda", "/ws/modb"], [], CancellationToken.None);

        Assert.Equal(["/ws/moda", "/ws/modb"], reload.LoadedRoots);
    }

    [Fact]
    public async Task ApplyAsync_NothingActuallyChanges_SkipsTheReload()
    {
        // A rescan of every project is expensive; a notification that leaves the root set identical
        // must not trigger one.
        var reload = new RecordingReloadService { LastWorkspaceRoots = ["/ws/moda"] };
        var service = Build(reload);

        await service.ApplyAsync(["/ws/moda"], ["/ws/unknown"], CancellationToken.None);

        Assert.Null(reload.LoadedRoots);
    }

    [Fact]
    public async Task ApplyAsync_BeforeTheFirstLoad_StillLoadsTheAddedFolders()
    {
        var reload = new RecordingReloadService { LastWorkspaceRoots = null };
        var service = Build(reload);

        await service.ApplyAsync(["/ws/moda"], [], CancellationToken.None);

        Assert.Equal(["/ws/moda"], reload.LoadedRoots);
    }

    [Fact]
    public async Task ApplyAsync_RemovalIsCaseInsensitive()
    {
        var reload = new RecordingReloadService { LastWorkspaceRoots = [@"C:\ws\ModA"] };
        var service = Build(reload);

        await service.ApplyAsync([], [@"c:\ws\moda"], CancellationToken.None);

        Assert.Empty(reload.LoadedRoots!);
    }

    private sealed class RecordingReloadService : IModProjectReloadService
    {
        public IReadOnlyList<string>? LoadedRoots { get; private set; }

        public IReadOnlyList<string>? LastAssetRoots => null;
        public WorkspaceConfiguration? LastWorkspaceConfig => null;
        public IReadOnlyList<string>? LastWorkspaceRoots { get; init; }

        public Task LoadAsync(IEnumerable<string> workspaceRoots, CancellationToken ct)
        {
            LoadedRoots = workspaceRoots.ToList();
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

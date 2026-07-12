// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Server;
using PG.StarWarsGame.LSP.Server.Tests.Story;

namespace PG.StarWarsGame.LSP.Server.Tests;

public sealed class WorkspaceSettingsStoreTest
{
    private static string Rooted(string sub)
    {
        return Path.Combine(Path.GetPathRoot(Path.GetFullPath("."))!, sub);
    }

    private static WorkspaceSettingsStore NewStore(MockFileSystem fs)
    {
        var config = WorkspaceConfiguration.Empty with
        {
            Layers = [new ProjectLayer(1, "Mod", [], [], [], [], null,
                ProjectPath: Path.Combine(Rooted("proj"), "mod.pgproj"))]
        };
        return new WorkspaceSettingsStore(
            new StoryCommandTestFixtures.StubReloadService(config),
            new FileHelper(fs),
            NullLogger<WorkspaceSettingsStore>.Instance);
    }

    [Fact]
    public void Defaults_ToFalse()
    {
        Assert.False(NewStore(new MockFileSystem()).Get().SkipStoryDeleteConfirmation);
    }

    [Fact]
    public void Set_PersistsAcrossStoreInstances()
    {
        var fs = new MockFileSystem();
        NewStore(fs).Set(new WorkspaceSettings { SkipStoryDeleteConfirmation = true });

        // A fresh store (empty cache) reads the value back from .aetswg/settings/workspace.settings.json.
        Assert.True(NewStore(fs).Get().SkipStoryDeleteConfirmation);
    }

    [Fact]
    public void NoProjectPath_DegradesToInMemory()
    {
        var store = new WorkspaceSettingsStore(
            new StoryCommandTestFixtures.StubReloadService(WorkspaceConfiguration.Empty),
            new FileHelper(new MockFileSystem()),
            NullLogger<WorkspaceSettingsStore>.Instance);

        store.Set(new WorkspaceSettings { SkipStoryDeleteConfirmation = true });
        Assert.True(store.Get().SkipStoryDeleteConfirmation); // same instance keeps it in-session
    }

    [Fact]
    public async Task Handlers_SetThenGet_RoundTrip()
    {
        var store = NewStore(new MockFileSystem());

        await new SetWorkspaceSettingsHandler(store)
            .Handle(new SetWorkspaceSettingsParams(true), CancellationToken.None);
        var result = await new GetWorkspaceSettingsHandler(store)
            .Handle(new GetWorkspaceSettingsParams(), CancellationToken.None);

        Assert.True(result.SkipStoryDeleteConfirmation);
    }

    [Fact]
    public async Task Handlers_PartialUpdate_LeavesOtherFieldsUntouched()
    {
        var store = NewStore(new MockFileSystem());

        await new SetWorkspaceSettingsHandler(store)
            .Handle(new SetWorkspaceSettingsParams(SkipStoryDeleteConfirmation: true), CancellationToken.None);
        // A later set touching only the lane toggle must not clobber the delete-confirm preference.
        await new SetWorkspaceSettingsHandler(store)
            .Handle(new SetWorkspaceSettingsParams(ShowThreadLanes: true), CancellationToken.None);
        var result = await new GetWorkspaceSettingsHandler(store)
            .Handle(new GetWorkspaceSettingsParams(), CancellationToken.None);

        Assert.True(result.SkipStoryDeleteConfirmation);
        Assert.True(result.ShowThreadLanes);
        Assert.False(result.ShowChapterLanes);
    }
}

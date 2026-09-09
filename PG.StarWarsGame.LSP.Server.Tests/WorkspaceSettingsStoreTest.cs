// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions.TestingHelpers;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
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
            Layers =
            [
                new ProjectLayer(1, "Mod", [], [], [], [], null,
                    Path.Combine(Rooted("proj"), "mod.pgproj"))
            ]
        };
        return new WorkspaceSettingsStore(
            new StoryCommandTestFixtures.StubReloadService(config),
            new FileHelper(fs),
            NullLogger<WorkspaceSettingsStore>.Instance);
    }

    private static string SidecarPath()
    {
        return Path.Combine(Rooted("proj"), ".aetswg", "settings", "workspace.settings.json");
    }

    [Fact]
    public void Defaults_ToFalse()
    {
        Assert.False(NewStore(new MockFileSystem()).Get().SkipStoryDeleteConfirmation);
    }

    // ── the envelope ─────────────────────────────────────────────────────────

    // The settings file was the one we agreed could simply be dropped, on the assumption that
    // versioning it meant restructuring it. Measured, its shape does not change at all - the
    // envelope sits beside the same three properties - so the migration is an adoption and the
    // user keeps the toggles they set. Discarding them would have bought nothing.
    [Fact]
    public void LegacyFileWithNoEnvelope_KeepsItsValues()
    {
        var fs = new MockFileSystem();
        fs.AddFile(SidecarPath(), new MockFileData(
            """{ "skipStoryDeleteConfirmation": true, "showThreadLanes": true, "showChapterLanes": false }"""));

        var settings = NewStore(fs).Get();

        Assert.True(settings.SkipStoryDeleteConfirmation);
        Assert.True(settings.ShowThreadLanes);
        Assert.False(settings.ShowChapterLanes);
    }

    [Fact]
    public void LegacyFile_IsRewrittenWithItsEnvelope()
    {
        var fs = new MockFileSystem();
        fs.AddFile(SidecarPath(), new MockFileData("""{ "showThreadLanes": true }"""));

        NewStore(fs).Get();

        var written = JsonNode.Parse(fs.File.ReadAllText(SidecarPath()))!.AsObject();
        Assert.Equal("aetswg.WorkspaceSettings", (string?)written["_type"]);
        Assert.Equal(WorkspaceSettingsDocument.Version.ToString(), (string?)written["_typeVersion"]);
        Assert.True((bool?)written["showThreadLanes"]);
    }

    [Fact]
    public void NewerFile_FallsBackToDefaultsAndIsLeftIntact()
    {
        const string newer =
            """{ "_type": "aetswg.WorkspaceSettings", "_typeVersion": "aetswg-99.0.0", "showThreadLanes": true }""";
        var fs = new MockFileSystem();
        fs.AddFile(SidecarPath(), new MockFileData(newer));
        var store = NewStore(fs);

        Assert.False(store.Get().ShowThreadLanes);
        store.Set(new WorkspaceSettings { ShowThreadLanes = false });

        Assert.Equal(newer, fs.File.ReadAllText(SidecarPath()));
    }

    // ── the shape guard ──────────────────────────────────────────────────────

    [Fact]
    public void Document_StillHasThePinnedShape()
    {
        Assert.True(
            WorkspaceSettingsDocument.Shape.Matches(typeof(WorkspaceSettings),
                WorkspaceSettingsDocument.Version),
            $"'{WorkspaceSettingsDocument.TypeName}' changed shape. Bump its version, add a migration "
            + "from the old one, and re-pin the signature.");
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
            .Handle(new SetWorkspaceSettingsParams(true), CancellationToken.None);
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
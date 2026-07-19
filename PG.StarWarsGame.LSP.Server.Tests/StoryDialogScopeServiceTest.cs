// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions.TestingHelpers;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Server.Project;

namespace PG.StarWarsGame.LSP.Server.Tests;

public sealed class StoryDialogScopeServiceTest
{
    private static readonly string DialogRoot = Root("ws/data/scripts/story");
    private static readonly string DepDialogRoot = Root("dep/data/scripts/story");

    private static string Root(string sub)
    {
        return Path.Combine(Path.GetPathRoot(Path.GetFullPath("."))!, sub).Replace('\\', '/').ToLowerInvariant();
    }

    private static (StoryDialogScopeService Scope, FileHelper FileHelper) Build(
        MockFileSystem? fs = null,
        IReadOnlyList<string>? roots = null,
        bool flagOn = true)
    {
        var fileSystem = fs ?? new MockFileSystem();
        var fileHelper = new FileHelper(fileSystem);
        var config = WorkspaceConfiguration.Empty with { StoryDialogRoots = roots ?? [DialogRoot] };
        var provider = FakeLspConfigurationProvider.WithFeatures(
            new FeatureFlags { Dialog = new DialogFeatureFlags { Diagnostics = flagOn } });
        var scope = new StoryDialogScopeService(new StubReloadService(config), provider, fileHelper);
        return (scope, fileHelper);
    }

    // ── Enabled ──────────────────────────────────────────────────────────────

    [Fact]
    public void Enabled_FlagOnAndRootsConfigured_IsTrue()
    {
        var (scope, _) = Build();

        Assert.True(scope.Enabled);
    }

    [Fact]
    public void Enabled_NoRootsConfigured_IsFalse()
    {
        var (scope, _) = Build(roots: []);

        Assert.False(scope.Enabled);
    }

    [Fact]
    public void Enabled_FlagOff_IsFalse()
    {
        var (scope, _) = Build(flagOn: false);

        Assert.False(scope.Enabled);
    }

    // ── IsInScope ────────────────────────────────────────────────────────────

    [Fact]
    public void IsInScope_FileUnderDialogRoot_IsTrue()
    {
        var (scope, fh) = Build();

        Assert.True(scope.IsInScope(fh.NormalizeUri(DialogRoot + "/dialog_x.txt")));
        Assert.True(scope.IsInScope(fh.NormalizeUri(DialogRoot + "/sub/dialog_y.txt")));
    }

    [Fact]
    public void IsInScope_FileOutsideDialogRoot_IsFalse()
    {
        var (scope, fh) = Build();

        Assert.False(scope.IsInScope(fh.NormalizeUri(Root("ws/readme.txt"))));
        // Sibling directory with the root as a name prefix must not match.
        Assert.False(scope.IsInScope(fh.NormalizeUri(DialogRoot + "_other/dialog_x.txt")));
    }

    // ── ResolveDialogFile ────────────────────────────────────────────────────

    [Fact]
    public void ResolveDialogFile_FindsFileCaseInsensitively()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [DialogRoot + "/Dialog_Mission_One.txt"] = new("[CHAPTER 0]")
        });
        var (scope, fh) = Build(fs);

        var resolved = scope.ResolveDialogFile("DIALOG_MISSION_ONE");

        Assert.Equal(fh.NormalizeUri(DialogRoot + "/Dialog_Mission_One.txt"), resolved);
    }

    [Fact]
    public void ResolveDialogFile_HighestLayerWins()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [DepDialogRoot + "/dialog_shared.txt"] = new("[CHAPTER 0]"),
            [DialogRoot + "/dialog_shared.txt"] = new("[CHAPTER 0]")
        });
        // Roots arrive dependencies-first, root project last.
        var (scope, fh) = Build(fs, [DepDialogRoot, DialogRoot]);

        var resolved = scope.ResolveDialogFile("dialog_shared");

        Assert.Equal(fh.NormalizeUri(DialogRoot + "/dialog_shared.txt"), resolved);
    }

    [Fact]
    public void ResolveDialogFile_Missing_ReturnsNull()
    {
        var (scope, _) = Build();

        Assert.Null(scope.ResolveDialogFile("dialog_gone"));
    }

    // ── GetChapters ──────────────────────────────────────────────────────────

    [Fact]
    public void GetChapters_ParsesChapterIndicesFromFile()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [DialogRoot + "/dialog_x.txt"] = new("[CHAPTER 0]\nTEXT A\n[CHAPTER 2]\nTEXT B")
        });
        var (scope, fh) = Build(fs);

        var chapters = scope.GetChapters(fh.NormalizeUri(DialogRoot + "/dialog_x.txt"));

        Assert.Equal([0, 2], chapters.Order());
    }

    [Fact]
    public void GetChapters_MissingFile_ReturnsEmpty()
    {
        var (scope, fh) = Build();

        Assert.Empty(scope.GetChapters(fh.NormalizeUri(DialogRoot + "/dialog_gone.txt")));
    }

    [Fact]
    public void GetChapters_FileChangedOnDisk_ReturnsFreshChapters()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [DialogRoot + "/dialog_x.txt"] = new("[CHAPTER 0]")
        });
        var (scope, fh) = Build(fs);
        var uri = fh.NormalizeUri(DialogRoot + "/dialog_x.txt");
        Assert.Equal([0], scope.GetChapters(uri));

        fs.File.SetLastWriteTimeUtc(DialogRoot + "/dialog_x.txt", DateTime.UtcNow.AddMinutes(1));
        fs.File.WriteAllText(DialogRoot + "/dialog_x.txt", "[CHAPTER 0]\n[CHAPTER 1]");
        fs.File.SetLastWriteTimeUtc(DialogRoot + "/dialog_x.txt", DateTime.UtcNow.AddMinutes(2));

        Assert.Equal([0, 1], scope.GetChapters(uri).Order());
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
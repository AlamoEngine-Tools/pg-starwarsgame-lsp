// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Server.Localisation;
using PG.StarWarsGame.LSP.Server.Project;

namespace PG.StarWarsGame.LSP.Server.Tests.Localisation;

public sealed class DeleteLocalisationEntryHandlerTest
{
    private const string Path = "/mod/f.csv";

    // ── feature flag ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_LocalisationFlagOff_FailsWithDisabledMessageWithoutWriting()
    {
        var config = FakeLspConfigurationProvider.WithFeatures(
            new FeatureFlags { Tools = new ToolsFeatureFlags { Localisation = false } });
        var (handler, fs, reload) = Build("key,ENGLISH\nTEXT_A,Hello\n", config);
        var hash = LocalisationContentHash.Compute(fs.File.ReadAllText(Path));

        var result = await handler.Handle(
            new DeleteLocalisationEntryParams(Path, "TEXT_A", hash), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(LocalisationFeatureDisabled.Message, result.Error);
        Assert.Contains("TEXT_A", fs.File.ReadAllText(Path));
        Assert.False(reload.LocalisationOnlyReloaded);
    }

    [Fact]
    public async Task Handle_ExistingKey_RemovesEntry_ReturnsSuccess()
    {
        var (handler, fs, reload) = Build("key,ENGLISH\nTEXT_A,Hello\nTEXT_B,World\n");
        var hash = LocalisationContentHash.Compute(fs.File.ReadAllText(Path));

        var result = await handler.Handle(
            new DeleteLocalisationEntryParams(Path, "TEXT_A", hash), CancellationToken.None);

        Assert.True(result.Success);
        Assert.DoesNotContain("TEXT_A", fs.File.ReadAllText(Path));
        Assert.Contains("TEXT_B,World", fs.File.ReadAllText(Path));
        Assert.True(reload.LocalisationOnlyReloaded);
    }

    [Fact]
    public async Task Handle_KeyNotFound_ReturnsError_FileUnchanged()
    {
        var (handler, fs, reload) = Build("key,ENGLISH\nTEXT_A,Hello\n");
        var original = fs.File.ReadAllText(Path);
        var hash = LocalisationContentHash.Compute(original);

        var result = await handler.Handle(
            new DeleteLocalisationEntryParams(Path, "TEXT_MISSING", hash), CancellationToken.None);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Equal(original, fs.File.ReadAllText(Path));
        Assert.False(reload.LocalisationOnlyReloaded);
    }

    [Fact]
    public async Task Handle_StaleContentHash_RejectedWithoutDeleting()
    {
        var (handler, fs, reload) = Build("key,ENGLISH\nTEXT_A,Hello\n");
        var staleHash = LocalisationContentHash.Compute("key,ENGLISH\nTEXT_A,Hello\n");
        fs.File.WriteAllText(Path, "key,ENGLISH\nTEXT_A,Hello\nTEXT_C,Concurrent\n");

        var result = await handler.Handle(
            new DeleteLocalisationEntryParams(Path, "TEXT_A", staleHash), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("TEXT_A", fs.File.ReadAllText(Path));
        Assert.False(reload.LocalisationOnlyReloaded);
    }

    [Fact]
    public async Task Handle_MissingContentHash_Rejected()
    {
        var (handler, _, _) = Build("key,ENGLISH\nTEXT_A,Hello\n");

        var result = await handler.Handle(
            new DeleteLocalisationEntryParams(Path, "TEXT_A", null), CancellationToken.None);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task Handle_FileDoesNotExist_ReturnsError()
    {
        var (handler, _, _) = Build(null);

        var result = await handler.Handle(
            new DeleteLocalisationEntryParams("/nonexistent.csv", "TEXT_A", "anyhash"), CancellationToken.None);

        Assert.False(result.Success);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static (DeleteLocalisationEntryHandler handler, MockFileSystem fs, SpyReloadService reload) Build(
        string? initialContent, ILspConfigurationProvider? config = null)
    {
        var files = initialContent is null
            ? new Dictionary<string, MockFileData>()
            : new Dictionary<string, MockFileData> { [Path] = new(initialContent) };
        var fs = new MockFileSystem(files);
        var fileHelper = new FileHelper(fs);
        var entryWriter = new LocalisationEntryWriter(fileHelper, NullLogger<LocalisationEntryWriter>.Instance);
        var reload = new SpyReloadService();
        var handler = new DeleteLocalisationEntryHandler(
            entryWriter, fileHelper, reload, NullLogger<DeleteLocalisationEntryHandler>.Instance,
            config ?? new FakeLspConfigurationProvider());
        return (handler, fs, reload);
    }

    private sealed class SpyReloadService : IModProjectReloadService
    {
        public bool LocalisationOnlyReloaded { get; private set; }
        public IReadOnlyList<string>? LastAssetRoots => null;
        public WorkspaceConfiguration? LastWorkspaceConfig => null;
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
            LocalisationOnlyReloaded = true;
            return Task.CompletedTask;
        }
    }
}

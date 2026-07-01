// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Server.Localisation;
using PG.StarWarsGame.LSP.Server.Project;

namespace PG.StarWarsGame.LSP.Server.Tests.Localisation;

public sealed class SetLocalisationEntryHandlerTest
{
    private const string Path = "/mod/f.csv";

    [Fact]
    public async Task Handle_NewKey_WritesEntry_ReturnsSuccessWithNewHash()
    {
        var (handler, fs, reload) = Build("key,ENGLISH\nTEXT_A,Hello\n");
        var hash = LocalisationContentHash.Compute(fs.File.ReadAllText(Path));

        var result = await handler.Handle(
            new SetLocalisationEntryParams(Path, "TEXT_B", new Dictionary<string, string> { ["ENGLISH"] = "World" }, hash),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Null(result.Error);
        Assert.NotNull(result.NewContentHash);
        Assert.NotEqual(hash, result.NewContentHash);
        Assert.Contains("TEXT_B,World", fs.File.ReadAllText(Path));
    }

    [Fact]
    public async Task Handle_ExistingKey_UpdatesInPlace()
    {
        var (handler, fs, _) = Build("key,ENGLISH\nTEXT_A,Old\n");
        var hash = LocalisationContentHash.Compute(fs.File.ReadAllText(Path));

        await handler.Handle(
            new SetLocalisationEntryParams(Path, "TEXT_A", new Dictionary<string, string> { ["ENGLISH"] = "New" }, hash),
            CancellationToken.None);

        var lines = fs.File.ReadAllText(Path).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.Contains("TEXT_A,New", lines);
    }

    [Fact]
    public async Task Handle_SuccessfulWrite_TriggersLocalisationOnlyReload()
    {
        var (handler, fs, reload) = Build("key,ENGLISH\nTEXT_A,Hello\n");
        var hash = LocalisationContentHash.Compute(fs.File.ReadAllText(Path));

        await handler.Handle(
            new SetLocalisationEntryParams(Path, "TEXT_B", null, hash), CancellationToken.None);

        Assert.True(reload.LocalisationOnlyReloaded);
    }

    [Fact]
    public async Task Handle_MissingContentHash_RejectedWithoutWriting()
    {
        var (handler, fs, reload) = Build("key,ENGLISH\nTEXT_A,Hello\n");
        var original = fs.File.ReadAllText(Path);

        var result = await handler.Handle(
            new SetLocalisationEntryParams(Path, "TEXT_B", null, null), CancellationToken.None);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Equal(original, fs.File.ReadAllText(Path));
        Assert.False(reload.LocalisationOnlyReloaded);
    }

    [Fact]
    public async Task Handle_StaleContentHash_RejectedWithoutWriting()
    {
        var (handler, fs, reload) = Build("key,ENGLISH\nTEXT_A,Hello\n");

        // File changes on disk after the client would have fetched it.
        fs.File.WriteAllText(Path, "key,ENGLISH\nTEXT_A,Hello\nTEXT_C,Concurrent Edit\n");
        var staleHash = LocalisationContentHash.Compute("key,ENGLISH\nTEXT_A,Hello\n");

        var result = await handler.Handle(
            new SetLocalisationEntryParams(Path, "TEXT_B", null, staleHash), CancellationToken.None);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.DoesNotContain("TEXT_B", fs.File.ReadAllText(Path));
        Assert.Contains("TEXT_C,Concurrent Edit", fs.File.ReadAllText(Path));
        Assert.False(reload.LocalisationOnlyReloaded);
    }

    [Fact]
    public async Task Handle_FileDoesNotExist_ReturnsError()
    {
        var (handler, _, _) = Build(null);

        var result = await handler.Handle(
            new SetLocalisationEntryParams("/nonexistent.csv", "TEXT_A", null, "anyhash"), CancellationToken.None);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task Handle_MissingKey_ReturnsError()
    {
        var (handler, fs, _) = Build("key,ENGLISH\n");
        var hash = LocalisationContentHash.Compute(fs.File.ReadAllText(Path));

        var result = await handler.Handle(
            new SetLocalisationEntryParams(Path, "", null, hash), CancellationToken.None);

        Assert.False(result.Success);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static (SetLocalisationEntryHandler handler, MockFileSystem fs, SpyReloadService reload) Build(
        string? initialContent)
    {
        var files = initialContent is null
            ? new Dictionary<string, MockFileData>()
            : new Dictionary<string, MockFileData> { [Path] = new(initialContent) };
        var fs = new MockFileSystem(files);
        var fileHelper = new FileHelper(fs);
        var entryWriter = new LocalisationEntryWriter(fileHelper, NullLogger<LocalisationEntryWriter>.Instance);
        var reload = new SpyReloadService();
        var handler = new SetLocalisationEntryHandler(
            entryWriter, fileHelper, reload, NullLogger<SetLocalisationEntryHandler>.Instance);
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

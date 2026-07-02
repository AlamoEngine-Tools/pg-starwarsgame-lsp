// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Server.Localisation;
using PG.StarWarsGame.LSP.Server.Project;

namespace PG.StarWarsGame.LSP.Server.Tests.Localisation;

public sealed class AddLocalisationLanguageHandlerTest
{
    private const string Path = "/mod/f.csv";

    [Fact]
    public async Task Handle_NewLanguage_AddsColumn_ReturnsSuccess()
    {
        var (handler, fs, reload) = Build("key,ENGLISH\nTEXT_A,Hello\n");
        var hash = LocalisationContentHash.Compute(fs.File.ReadAllText(Path));

        var result = await handler.Handle(
            new AddLocalisationLanguageParams(Path, "GERMAN", hash), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("key,ENGLISH,GERMAN", fs.File.ReadAllText(Path).Split('\n')[0]);
        Assert.True(reload.LocalisationOnlyReloaded);
    }

    [Fact]
    public async Task Handle_LanguageAlreadyPresent_ReturnsError_FileUnchanged()
    {
        var (handler, fs, reload) = Build("key,ENGLISH,GERMAN\nTEXT_A,Hello,Hallo\n");
        var original = fs.File.ReadAllText(Path);
        var hash = LocalisationContentHash.Compute(original);

        var result = await handler.Handle(
            new AddLocalisationLanguageParams(Path, "GERMAN", hash), CancellationToken.None);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Equal(original, fs.File.ReadAllText(Path));
        Assert.False(reload.LocalisationOnlyReloaded);
    }

    [Fact]
    public async Task Handle_NlsFormat_NotApplicable_ReturnsError()
    {
        const string nlsPath = "/mod/f.properties";
        var fs = new MockFileSystem(new Dictionary<string, MockFileData> { [nlsPath] = new("TEXT_A=Hello\n") });
        var fileHelper = new FileHelper(fs);
        var writer = new LocalisationEntryWriter(fileHelper, NullLogger<LocalisationEntryWriter>.Instance);
        var reload = new SpyReloadService();
        var handler = new AddLocalisationLanguageHandler(
            writer, fileHelper, reload, NullLogger<AddLocalisationLanguageHandler>.Instance);
        var hash = LocalisationContentHash.Compute(fs.File.ReadAllText(nlsPath));

        var result = await handler.Handle(
            new AddLocalisationLanguageParams(nlsPath, "GERMAN", hash), CancellationToken.None);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task Handle_MissingContentHash_Rejected()
    {
        var (handler, _, _) = Build("key,ENGLISH\nTEXT_A,Hello\n");

        var result = await handler.Handle(
            new AddLocalisationLanguageParams(Path, "GERMAN", null), CancellationToken.None);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task Handle_StaleContentHash_Rejected()
    {
        var (handler, fs, reload) = Build("key,ENGLISH\nTEXT_A,Hello\n");
        var staleHash = LocalisationContentHash.Compute("key,ENGLISH\nTEXT_A,Hello\n");
        fs.File.WriteAllText(Path, "key,ENGLISH\nTEXT_A,Hello\nTEXT_C,Concurrent\n");

        var result = await handler.Handle(
            new AddLocalisationLanguageParams(Path, "GERMAN", staleHash), CancellationToken.None);

        Assert.False(result.Success);
        Assert.False(reload.LocalisationOnlyReloaded);
    }

    [Fact]
    public async Task Handle_MissingLanguage_ReturnsError()
    {
        var (handler, fs, _) = Build("key,ENGLISH\n");
        var hash = LocalisationContentHash.Compute(fs.File.ReadAllText(Path));

        var result = await handler.Handle(
            new AddLocalisationLanguageParams(Path, "", hash), CancellationToken.None);

        Assert.False(result.Success);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static (AddLocalisationLanguageHandler handler, MockFileSystem fs, SpyReloadService reload) Build(
        string initialContent)
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData> { [Path] = new(initialContent) });
        var fileHelper = new FileHelper(fs);
        var entryWriter = new LocalisationEntryWriter(fileHelper, NullLogger<LocalisationEntryWriter>.Instance);
        var reload = new SpyReloadService();
        var handler = new AddLocalisationLanguageHandler(
            entryWriter, fileHelper, reload, NullLogger<AddLocalisationLanguageHandler>.Instance);
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

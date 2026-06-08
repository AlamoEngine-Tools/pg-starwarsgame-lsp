// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Server.Localisation;
using PG.StarWarsGame.LSP.Server.Project;
using PG.StarWarsGame.LSP.Server.Startup;

namespace PG.StarWarsGame.LSP.Server.Tests.Project;

public sealed class ModProjectReloadServiceTest
{
    private static readonly WorkspaceConfiguration SampleConfig =
        new(["/ws/data/xml"], ["/ws/data/scripts"], [], ["/ws/data/art", "/ws/data/audio"], null);

    private static (ModProjectReloadService Service, RecordingIndexer Indexer, ListLogger Logger) Build(
        WorkspaceConfiguration? resolved)
    {
        var indexer = new RecordingIndexer();
        var logger = new ListLogger();
        var service = new ModProjectReloadService(
            new FakeResolver(resolved), indexer, new NullLocalisationLoader(), logger);
        return (service, indexer, logger);
    }

    [Fact]
    public async Task LoadAsync_NoProjectFile_DoesNotIndex()
    {
        var (service, indexer, _) = Build(null);

        await service.LoadAsync(["/ws"], CancellationToken.None);

        Assert.Null(indexer.LastConfig);
        Assert.Equal(0, indexer.IndexCallCount);
    }

    [Fact]
    public async Task LoadAsync_ProjectFileFound_IndexesResolvedConfig()
    {
        var (service, indexer, _) = Build(SampleConfig);

        await service.LoadAsync(["/ws"], CancellationToken.None);

        Assert.NotNull(indexer.LastConfig);
        Assert.Equal(["/ws/data/xml"], indexer.LastConfig!.XmlDirectories);
        Assert.Equal(1, indexer.IndexCallCount);
        Assert.True(indexer.AssetCatalogApplied);
        Assert.True(indexer.BonesApplied);
    }

    [Fact]
    public async Task LoadAsync_TracksLastAssetRoots()
    {
        var (service, _, _) = Build(SampleConfig);

        await service.LoadAsync(["/ws"], CancellationToken.None);

        Assert.Equal(["/ws/data/art", "/ws/data/audio"], service.LastAssetRoots);
    }

    [Fact]
    public async Task ReloadAsync_BeforeLoadAsync_NoOpWithWarning()
    {
        var (service, indexer, logger) = Build(SampleConfig);

        await service.ReloadAsync(CancellationToken.None);

        Assert.Null(indexer.LastConfig);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task ReloadAsync_AfterLoad_ReindexesSameRoots()
    {
        var (service, indexer, _) = Build(SampleConfig);
        await service.LoadAsync(["/ws/a", "/ws/b"], CancellationToken.None);

        await service.ReloadAsync(CancellationToken.None);

        Assert.Equal(2, indexer.IndexCallCount);
        Assert.Equal(["/ws/a", "/ws/b"], indexer.LastRoots);
    }

    // ── fakes ────────────────────────────────────────────────────────────────

    private sealed class FakeResolver : IProjectConfigurationResolver
    {
        private readonly WorkspaceConfiguration? _config;

        public FakeResolver(WorkspaceConfiguration? config)
        {
            _config = config;
        }

        public WorkspaceConfiguration? Resolve(IReadOnlyList<string> roots)
        {
            return _config;
        }
    }

    private sealed class RecordingIndexer : IWorkspaceIndexer
    {
        public WorkspaceConfiguration? LastConfig { get; private set; }
        public IReadOnlyList<string>? LastRoots { get; private set; }
        public int IndexCallCount { get; private set; }
        public bool AssetCatalogApplied { get; private set; }
        public bool BonesApplied { get; private set; }

        public void PreScanMetafiles(WorkspaceConfiguration config, IReadOnlyList<string> roots)
        {
            LastConfig = config;
            LastRoots = roots;
        }

        public Task<int> IndexDocumentsAsync(WorkspaceConfiguration config, CancellationToken ct,
            Action<int, int>? progress = null)
        {
            IndexCallCount++;
            return Task.FromResult(0);
        }

        public void ApplyAssetCatalog(IReadOnlyList<string> roots)
        {
            AssetCatalogApplied = true;
        }

        public void ApplyModelBoneCatalog(IReadOnlyList<string> roots)
        {
            BonesApplied = true;
        }
    }

    private sealed class NullLocalisationLoader : ILocalisationLoader
    {
        public Task LoadAsync(WorkspaceConfiguration workspaceConfig, CancellationToken ct)
        {
            return Task.CompletedTask;
        }
    }

    private sealed record LogEntry(LogLevel Level, string Message);

    private sealed class ListLogger : ILogger<ModProjectReloadService>
    {
        public ConcurrentBag<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new LogEntry(logLevel, formatter(state, exception)));
        }
    }
}
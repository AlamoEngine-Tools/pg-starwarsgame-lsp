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
            new FakeResolver(resolved), indexer, new NullLocalisationLoader(),
            new RecordingLayerMap(), logger);
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

    [Fact]
    public async Task ReloadLocalisationAsync_BeforeLoadAsync_NoOpWithWarning()
    {
        var (service, _, logger) = Build(SampleConfig);

        await service.ReloadLocalisationAsync(CancellationToken.None);

        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task ReloadLocalisationAsync_AfterLoad_ReloadsLocalisationOnly()
    {
        var localisation = new RecordingLocalisationLoader();
        var indexer = new RecordingIndexer();
        var service = new ModProjectReloadService(
            new FakeResolver(SampleConfig), indexer, localisation, new RecordingLayerMap(), new ListLogger());
        await service.LoadAsync(["/ws"], CancellationToken.None);
        var indexCallsAfterLoad = indexer.IndexCallCount;
        var localisationCallsAfterLoad = localisation.LoadCallCount;

        await service.ReloadLocalisationAsync(CancellationToken.None);

        // Only the localisation loader ran again - the full XML/Lua/asset/bone/enum indexer did not.
        Assert.Equal(indexCallsAfterLoad, indexer.IndexCallCount);
        Assert.Equal(localisationCallsAfterLoad + 1, localisation.LoadCallCount);
        Assert.Same(SampleConfig, localisation.LastConfig);
    }

    [Fact]
    public async Task ReloadLocalisationAsync_AfterLoad_AsksClientToRefreshDerivedState()
    {
        // #45: localisation-backed inlay hints (loca-key annotations) and code lenses are computed
        // once, when the client asks. A localisation-only reload changes their content without any
        // document edit, so unless the server pushes a refresh the client keeps rendering the stale
        // set until something else makes it re-request - e.g. opening another file.
        var refresh = new RecordingClientRefreshNotifier();
        var service = new ModProjectReloadService(
            new FakeResolver(SampleConfig), new RecordingIndexer(), new RecordingLocalisationLoader(),
            new RecordingLayerMap(), new ListLogger(), refresh);
        await service.LoadAsync(["/ws"], CancellationToken.None);
        var before = refresh.CallCount;

        await service.ReloadLocalisationAsync(CancellationToken.None);

        Assert.Equal(before + 1, refresh.CallCount);
    }

    [Fact]
    public async Task ReloadLocalisationAsync_BeforeLoadAsync_DoesNotRefreshClient()
    {
        var refresh = new RecordingClientRefreshNotifier();
        var service = new ModProjectReloadService(
            new FakeResolver(SampleConfig), new RecordingIndexer(), new RecordingLocalisationLoader(),
            new RecordingLayerMap(), new ListLogger(), refresh);

        await service.ReloadLocalisationAsync(CancellationToken.None);

        Assert.Equal(0, refresh.CallCount);
    }

    private sealed class RecordingClientRefreshNotifier : IClientRefreshNotifier
    {
        public int CallCount { get; private set; }

        public void RefreshDerivedState()
        {
            CallCount++;
        }
    }

    [Fact]
    public async Task LoadAsync_PublishesConfigLayersToLayerMapBeforeIndexing()
    {
        var layerMap = new RecordingLayerMap();
        var indexer = new RecordingIndexer();
        var layers = new List<ProjectLayer>
        {
            new(0, "Core", ["/ws/dep/xml"], [], [], [], null),
            new(1, "Root", ["/ws/data/xml"], [], [], [], null)
        };
        var config = SampleConfig with { Layers = layers };
        var service = new ModProjectReloadService(
            new FakeResolver(config), indexer, new NullLocalisationLoader(), layerMap, new ListLogger());

        await service.LoadAsync(["/ws"], CancellationToken.None);

        Assert.NotNull(layerMap.LastLayers);
        Assert.Equal(["Core", "Root"], layerMap.LastLayers!.Select(l => l.Name));
    }

    // ── fakes ────────────────────────────────────────────────────────────────

    private sealed class RecordingLayerMap : IProjectLayerMap
    {
        public IReadOnlyList<ProjectLayer>? LastLayers { get; private set; }

        public void SetLayers(IReadOnlyList<ProjectLayer> layers)
        {
            LastLayers = layers;
        }

        public int GetRank(string fileUri)
        {
            return 0;
        }

        public string? GetLayerName(int rank)
        {
            return null;
        }
    }

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
        public bool DynamicEnumCatalogApplied { get; private set; }

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

        public void ApplyDynamicEnumCatalog(IReadOnlyList<string> xmlRoots)
        {
            DynamicEnumCatalogApplied = true;
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

    private sealed class RecordingLocalisationLoader : ILocalisationLoader
    {
        public int LoadCallCount { get; private set; }
        public WorkspaceConfiguration? LastConfig { get; private set; }

        public Task LoadAsync(WorkspaceConfiguration workspaceConfig, CancellationToken ct)
        {
            LoadCallCount++;
            LastConfig = workspaceConfig;
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
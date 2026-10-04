// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Abstractions.TestingHelpers;
using System.Reflection;
using PG.StarWarsGame.LSP.Core.Caching;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Schema.Versioning;
using PG.StarWarsGame.LSP.Server.Project;
using PG.StarWarsGame.LSP.Server.Startup;
using PG.StarWarsGame.LSP.Server.Status;

namespace PG.StarWarsGame.LSP.Server.Tests.Status;

/// <summary>
///     <c>aet/getServerStatus</c>: the bug report's server half. Outcomes only - it goes into
///     public issues, so nothing here may carry a path or a name.
/// </summary>
public sealed class GetServerStatusHandlerTest
{
    private static readonly BaselineIndex LoadedBaseline = BaselineIndex.Empty with
    {
        Symbols = ImmutableDictionary<string, GameSymbol>.Empty.Add("UNIT_A",
            new GameSymbol("UNIT_A", GameSymbolKind.XmlObject, "GameObjectType",
                new FileOrigin("file:///units.xml", 0, 0), null)),
        BuiltAt = new DateTimeOffset(2026, 9, 16, 10, 0, 0, TimeSpan.Zero),
        SourceManifestHash = "ab12"
    };

    private static ServerStatusRecorder HealthyRecorder()
    {
        var recorder = new ServerStatusRecorder();
        recorder.RecordSchema(StatusSchemaSource.Release,
            new SchemaVersionCheck(SchemaVersionCompatibility.Supported, "2.3.0", ">=2.0.0 <3.0.0", ""));
        recorder.RecordBaseline(StatusAssetSource.Cache);
        recorder.RecordIconPack(StatusAssetSource.Network);
        recorder.RecordProject(true, ProjectProblem.None);
        recorder.RecordIndexFinished(false, TimeSpan.FromSeconds(3));
        return recorder;
    }

    private static Task<ServerStatusDto> Status(
        ServerStatusRecorder recorder,
        BaselineIndex? baseline = null,
        LspConfiguration? configuration = null,
        MockFileSystem? fileSystem = null,
        IReadOnlyList<string>? roots = null,
        GameIndex? index = null)
    {
        // A healthy project by default - files and models indexed - so a warning a test did not ask
        // for cannot pass silently through every other test.
        index ??= LayeredIndex() with { Baseline = baseline ?? LoadedBaseline };
        var handler = new GetServerStatusHandler(
            recorder,
            new FakeLspConfigurationProvider { Current = configuration ?? new LspConfiguration() },
            new FakeGameIndexService(index),
            new RootsOnly(roots ?? []),
            new FileHelper(fileSystem ?? new MockFileSystem()));
        return handler.Handle(new GetServerStatusParams(), CancellationToken.None);
    }

    [Fact]
    public async Task ReportsWhatStartupRecorded_ByName()
    {
        var status = await Status(HealthyRecorder());

        Assert.Equal(new ServerStatusSchemaDto("2.3.0", "Supported", "Release"), status.Schema);
        Assert.Equal("Cache", status.Baseline.Source);
        Assert.Equal("Network", status.IconPack.Source);
        Assert.Equal("Complete", status.Index.State);
        Assert.Equal(new ServerStatusWorkspaceDto(true, true, null, 0, 0), status.Workspace);
    }

    [Fact]
    public async Task ReportsTheFullServerVersion_CommitIncluded()
    {
        // serverInfo strips everything after '+', which is exactly the part a bug report needs.
        var expected = typeof(GetServerStatusHandler).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;

        Assert.Equal(expected, (await Status(HealthyRecorder())).ServerVersion);
    }

    [Fact]
    public async Task ReportsTheLoadedBaselinesBuildAndManifest()
    {
        var status = await Status(HealthyRecorder());

        Assert.Equal("2026-09-16T10:00:00Z", status.Baseline.BuiltAt);
        Assert.Equal("ab12", status.Baseline.ManifestHash);
    }

    [Theory]
    [InlineData(BaselineSourceType.Http, null, true)]
    [InlineData(BaselineSourceType.Http, "https://example.org/my-baseline.aet", false)]
    [InlineData(BaselineSourceType.Local, null, false)]
    public async Task BaselineIsOfficialOnlyFromTheDefaultUrl(BaselineSourceType type, string? url, bool official)
    {
        var source = new BaselineSourceConfig { Type = type };
        if (url is not null) source = source with { Url = url };

        var status = await Status(HealthyRecorder(),
            configuration: new LspConfiguration { BaselineSource = source });

        Assert.Equal(official, status.Baseline.Official);
        Assert.Equal(official, status.IconPack.Official);
    }

    [Fact]
    public async Task NamesTheProjectProblem_WithoutTheMessage()
    {
        var recorder = HealthyRecorder();
        recorder.RecordProject(true, ProjectProblem.Unparseable);

        var status = await Status(recorder);

        Assert.Equal(new ServerStatusWorkspaceDto(true, false, "Unparseable", 0, 0), status.Workspace);
    }

    [Fact]
    public async Task CountsTheWorkspaceFoldersWithNoProjectBelowThem()
    {
        // The multi-root case: the detector takes the first folder with a .pgproj and indexes
        // nothing else, so the others are what a multi-root report needs to show.
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [@"C:\mod\sub\mod.pgproj"] = new("{}"),
            [@"C:\other\readme.txt"] = new(""),
            [@"C:\third\data\x.xml"] = new("")
        });

        var status = await Status(HealthyRecorder(), fileSystem: fs,
            roots: [@"C:\mod", @"C:\other", @"C:\third"]);

        Assert.Equal(2, status.Workspace.FoldersWithoutProject);
    }

    [Fact]
    public async Task HealthyStartup_HasNoWarnings()
    {
        Assert.Empty((await Status(HealthyRecorder())).Warnings);
    }

    [Fact]
    public async Task WarnsAboutAnEmptyBaseline_AFailedIndexAndAMissingSchema()
    {
        var recorder = new ServerStatusRecorder();
        recorder.RecordBaseline(StatusAssetSource.Empty);
        recorder.RecordProject(false, ProjectProblem.Missing);
        recorder.RecordIndexFinished(true, TimeSpan.FromSeconds(1));

        var status = await Status(recorder, BaselineIndex.Empty);

        Assert.Contains("Schema: Not loaded", status.Warnings);
        Assert.Contains("Baseline: Empty", status.Warnings);
        Assert.Contains("Index: Failed", status.Warnings);
        Assert.Contains("Project: Missing", status.Warnings);
    }

    [Fact]
    public async Task WarnsAboutARefusedSchema()
    {
        var recorder = HealthyRecorder();
        recorder.RecordSchema(StatusSchemaSource.Release,
            new SchemaVersionCheck(SchemaVersionCompatibility.Unsupported, "3.0.0", ">=2.0.0 <3.0.0", "x"));

        Assert.Contains("Schema: Unsupported", (await Status(recorder)).Warnings);
    }

    // ── extended tier ────────────────────────────────────────────────────────

    private static GameSymbol Symbol(string id, string? typeName, GameSymbolKind kind = GameSymbolKind.XmlObject)
    {
        return new GameSymbol(id, kind, typeName, new FileOrigin("file:///x", 0, 0), null);
    }

    private static DocumentIndex Doc(string uri, int rank, params GameSymbol[] symbols)
    {
        return new DocumentIndex(uri, 0, [.. symbols], [], LayerRank: rank);
    }

    /// <summary>A root project on one dependency: two project files, one dependency file.</summary>
    private static GameIndex LayeredIndex()
    {
        var docs = new[]
        {
            Doc("file:///mod/data/xml/units.xml", 1, Symbol("A", "SpaceUnit"), Symbol("B", "SpaceUnit")),
            Doc("file:///mod/data/scripts/story.lua", 1, Symbol("G", null, GameSymbolKind.LuaGlobal)),
            Doc("file:///dep/data/xml/infantry.xml", 0, Symbol("C", "GroundInfantry"))
        };
        return GameIndex.Empty with
        {
            Baseline = LoadedBaseline,
            Documents = docs.ToImmutableDictionary(d => d.DocumentUri, d => d),
            AssetFiles = MergedAssetFileIndex.Merge(
                ["data/art/models/shipped.alo"],
                ["data/art/models/mine.alo", "data/audio/mine.wav"])
        };
    }

    private static Task<ServerStatusDto> Extended(ServerStatusRecorder recorder, GameIndex index,
        IWorkspaceIndexer? indexer = null, IEnumerable<ICacheStatisticsSource>? caches = null)
    {
        var config = WorkspaceConfiguration.Empty with
        {
            Layers =
            [
                new ProjectLayer(0, "dep", [], [], [], [], null),
                new ProjectLayer(1, "mod", [], [], [], [], null)
            ],
            Dependencies = new ProjectDependencyShape(1, 1, 1, 0)
        };
        var handler = new GetServerStatusHandler(
            recorder,
            new FakeLspConfigurationProvider(),
            new FakeGameIndexService(index),
            new RootsOnly([], config),
            new FileHelper(new MockFileSystem()),
            indexer ?? new StatsOnly(new IndexCacheStats(1, 1, 5, 2), new BoneCatalogStats(1, 2)),
            caches);
        return handler.Handle(new GetServerStatusParams { Extended = true }, CancellationToken.None);
    }

    [Fact]
    public async Task BasicRequest_CarriesNoExtendedSection()
    {
        Assert.Null((await Status(HealthyRecorder())).Extended);
    }

    /// <summary>
    ///     A root with several project files loads one and reports the rest as a COUNT - never
    ///     their names, the status is pasted into public issues.
    /// </summary>
    [Fact]
    public async Task ReportsTheOtherProjectFilesAsACount()
    {
        var recorder = HealthyRecorder();
        recorder.RecordOtherProjectFiles(2);

        Assert.Equal(2, (await Status(recorder)).Workspace.OtherProjectFiles);
        Assert.Equal(0, (await Status(HealthyRecorder())).Workspace.OtherProjectFiles);
    }

    // ── memory ───────────────────────────────────────────────────────────────
    //
    // MEASURED 2026-10-04: a cold start held about 300 MB more than a warm start of the same
    // workspace, and the status could not say where. This section is the answer's raw material:
    // the process figures, what the index holds per layer, and what every cache holds.

    [Fact]
    public async Task Extended_ReportsTheProcessMemoryFigures()
    {
        var memory = (await Extended(HealthyRecorder(), LayeredIndex())).Extended!.Memory;

        Assert.True(memory.HeapBytes > 0);
        Assert.True(memory.CommittedBytes >= memory.HeapBytes);
        Assert.True(memory.WorkingSetBytes > 0);
        Assert.True(memory.FragmentedBytes >= 0);
        Assert.True(memory.LargeObjectHeapBytes >= 0);
        Assert.True(memory.Gen2Collections >= 0);
    }

    [Fact]
    public async Task Extended_CountsDocumentsSymbolsAndReferencesPerLayerRank()
    {
        // By RANK, never by layer name: the status is pasted into public issues.
        var layers = (await Extended(HealthyRecorder(), LayeredIndex())).Extended!.Memory.Layers;

        Assert.Equal([0, 1], layers.Select(l => l.Rank));
        var dep = layers.Single(l => l.Rank == 0);
        var mod = layers.Single(l => l.Rank == 1);
        Assert.Equal((1, 1), (dep.Documents, dep.Symbols));
        Assert.Equal((2, 3), (mod.Documents, mod.Symbols));
        Assert.Equal(3, layers.Sum(l => l.Documents));
    }

    [Fact]
    public async Task Extended_ListsEveryRegisteredCache_InRegistrationOrder()
    {
        var caches = new ICacheStatisticsSource[]
        {
            new FixedCache(new CacheStatistics("xml-parse", 16, null, 120, 40, 3)),
            new FixedCache(new CacheStatistics("lua-parser-state", 1432, 2_100_000))
        };

        var listed = (await Extended(HealthyRecorder(), LayeredIndex(), caches: caches)).Extended!.Memory.Caches;

        Assert.Equal(["xml-parse", "lua-parser-state"], listed.Select(c => c.Name));
        Assert.Equal(16, listed[0].Entries);
        Assert.Equal((120L, 40L, 3L), (listed[0].Hits, listed[0].Misses, listed[0].Evictions));
        Assert.Null(listed[0].ApproximateBytes);
        Assert.Equal(2_100_000L, listed[1].ApproximateBytes);
        Assert.Null(listed[1].Hits);
    }

    [Fact]
    public async Task Extended_WithNoCachesRegistered_ListsNone()
    {
        Assert.Empty((await Extended(HealthyRecorder(), LayeredIndex())).Extended!.Memory.Caches);
    }

    private sealed class FixedCache(CacheStatistics statistics) : ICacheStatisticsSource
    {
        public CacheStatistics Snapshot()
        {
            return statistics;
        }
    }

    [Fact]
    public async Task Extended_ReportsTheDependencyShapeAndLayerCount()
    {
        var extended = (await Extended(HealthyRecorder(), LayeredIndex())).Extended!;

        Assert.Equal(new ServerStatusDependenciesDto(1, 1, 1, 0, 2), extended.Dependencies);
    }

    [Fact]
    public async Task Extended_CountsFilesBySourceAndLanguage()
    {
        var extended = (await Extended(HealthyRecorder(), LayeredIndex())).Extended!;

        Assert.Equal(new ServerStatusFilesDto(ProjectFiles: 2, DependencyFiles: 1, XmlFiles: 2, LuaFiles: 1),
            extended.Files);
    }

    [Fact]
    public async Task Extended_CountsSymbolsByLanguage_AndTheBaselines()
    {
        var extended = (await Extended(HealthyRecorder(), LayeredIndex())).Extended!;

        Assert.Equal(new ServerStatusSymbolsDto(Xml: 3, Lua: 1, Baseline: 1, LocalisationKeys: 0),
            extended.Symbols);
    }

    [Fact]
    public async Task Extended_BreaksSymbolsDownByType_MostFirst()
    {
        var extended = (await Extended(HealthyRecorder(), LayeredIndex())).Extended!;

        Assert.Equal(
            [new("SpaceUnit", 2), new("GroundInfantry", 1), new("LuaGlobal", 1)],
            extended.SymbolTypes);
    }

    [Fact]
    public async Task Extended_CountsAssetsPerKnownExtension_IncludingTheEmptyOnes()
    {
        // The empty rows are the point: "no .ala at all" is the answer that explains a preview
        // with no animations.
        var assets = (await Extended(HealthyRecorder(), LayeredIndex())).Extended!.Assets;

        Assert.Equal(new ServerStatusAssetCountDto(".alo", 2, 1), assets.Single(a => a.Extension == ".alo"));
        Assert.Equal(new ServerStatusAssetCountDto(".wav", 1, 0), assets.Single(a => a.Extension == ".wav"));
        Assert.Equal(new ServerStatusAssetCountDto(".ala", 0, 0), assets.Single(a => a.Extension == ".ala"));
    }

    [Fact]
    public async Task Extended_ReportsEveryCachesOutcome()
    {
        var recorder = HealthyRecorder();
        recorder.RecordSchema(StatusSchemaSource.Release, recorder.SchemaCheck, fromCache: true);

        var caches = (await Extended(recorder, LayeredIndex())).Extended!.Caches;

        Assert.Equal(new ServerStatusCachesDto("Hit", 1, 1, 5, 2, 1, 2), caches);
    }

    [Fact]
    public async Task Extended_ReportsTheIndexDuration()
    {
        var extended = (await Extended(HealthyRecorder(), LayeredIndex())).Extended!;

        Assert.Equal(3000, extended.IndexDurationMs);
    }

    // ── zero-count warnings, in the basic tier ───────────────────────────────

    [Fact]
    public async Task WarnsWhenAValidProjectIndexedNoFiles()
    {
        // Complete, valid, and empty: the state that reads as "everything is fine" everywhere else.
        var status = await Status(HealthyRecorder(), index: GameIndex.Empty with { Baseline = LoadedBaseline });

        Assert.Contains("Project files: 0", status.Warnings);
    }

    [Fact]
    public async Task WarnsWhenTheAssetCatalogHoldsNoModels()
    {
        var status = await Status(HealthyRecorder(), index: GameIndex.Empty with { Baseline = LoadedBaseline });

        Assert.Contains("Asset catalog: No models", status.Warnings);
    }

    private sealed class StatsOnly(IndexCacheStats index, BoneCatalogStats bones) : IWorkspaceIndexer
    {
        public IndexCacheStats LastIndexCache => index;
        public BoneCatalogStats LastBoneCatalog => bones;

        public void PreScanMetafiles(WorkspaceConfiguration config, IReadOnlyList<string> roots)
        {
        }

        public Task<int> IndexDocumentsAsync(WorkspaceConfiguration config, CancellationToken ct,
            Action<int, int>? progress = null)
        {
            return Task.FromResult(0);
        }

        public void ApplyAssetCatalog(IReadOnlyList<string> roots)
        {
        }

        public void ApplyModelBoneCatalog(WorkspaceConfiguration config)
        {
        }

        public void ApplyDynamicEnumCatalog(IReadOnlyList<string> xmlRoots)
        {
        }
    }

    private sealed class RootsOnly(IReadOnlyList<string> roots, WorkspaceConfiguration? config = null)
        : IModProjectReloadService
    {
        public IReadOnlyList<string>? LastAssetRoots => null;
        public WorkspaceConfiguration? LastWorkspaceConfig => config;
        public IReadOnlyList<string>? LastWorkspaceRoots => roots;

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
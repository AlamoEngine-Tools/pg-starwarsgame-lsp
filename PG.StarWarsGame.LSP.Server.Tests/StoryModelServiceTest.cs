// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Localisation;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Server.Project;

namespace PG.StarWarsGame.LSP.Server.Tests;

public sealed class StoryModelServiceTest
{
    private static string Root(string sub)
    {
        return Path.Combine(Path.GetPathRoot(Path.GetFullPath("."))!, sub);
    }

    private static readonly string XmlDir = Path.Combine(Root("ws"), "data", "xml");
    private static readonly string DepXmlDir = Path.Combine(Root("dep"), "data", "xml");

    private static Dictionary<string, MockFileData> Fixture()
    {
        return new Dictionary<string, MockFileData>
        {
            [Path.Combine(XmlDir, "campaignfiles.xml")] =
                new("<Campaign_Files><File>Campaigns_Test.xml</File></Campaign_Files>"),
            [Path.Combine(XmlDir, "Campaigns_Test.xml")] = new(
                "<Campaigns><Campaign Name=\"GC_One\">" +
                "<Rebel_Story_Name>Story_Plots_R.xml</Rebel_Story_Name>" +
                "</Campaign></Campaigns>"),
            [Path.Combine(XmlDir, "Story_Plots_R.xml")] = new(
                "<Story_Mode_Plots><Active_Plot>Story_Act_I.xml</Active_Plot>" +
                "<Suspended_Plot>Story_Act_II.xml</Suspended_Plot></Story_Mode_Plots>"),
            [Path.Combine(XmlDir, "Story_Act_I.xml")] = new(
                "<Story><Event Name=\"Opening\"/></Story>"),
            [Path.Combine(XmlDir, "Story_Act_II.xml")] = new(
                "<Story><Event Name=\"Later\"/></Story>")
        };
    }

    private static (StoryModelService Service, MockFileSystem Fs, MutableIndexService Index,
        FakeHost Host, FileHelper FileHelper, StubReloadService Reload) Build(
            Dictionary<string, MockFileData>? files = null, IReadOnlyList<string>? xmlDirs = null,
            bool configured = true)
    {
        var fs = new MockFileSystem(files ?? Fixture());
        var fileHelper = new FileHelper(fs);
        var index = new MutableIndexService();
        var host = new FakeHost();
        var config = WorkspaceConfiguration.Empty with { XmlDirectories = xmlDirs ?? [XmlDir] };
        var reload = new StubReloadService(configured ? config : null);
        var service = new StoryModelService(
            reload,
            index,
            new SpecialDefSchemaProvider(),
            fileHelper,
            new DocumentTextSource(host, fileHelper, NullLogger<DocumentTextSource>.Instance),
            NullLogger<StoryModelService>.Instance);
        return (service, fs, index, host, fileHelper, reload);
    }

    private static WorkspaceConfiguration DefaultConfig()
    {
        return WorkspaceConfiguration.Empty with { XmlDirectories = [XmlDir] };
    }

    [Fact]
    public void GetCampaignNames_ComesFromTheChainScan()
    {
        var (service, _, _, _, _, _) = Build();

        Assert.Equal(["GC_One"], service.GetCampaignNames());
    }

    [Fact]
    public void GetCampaignModel_ParsesThreadsAndMarksSuspension()
    {
        var (service, _, _, _, fh, _) = Build();

        var model = service.GetCampaignModel("GC_One")!;

        Assert.Equal(2, model.Threads.Count);
        Assert.Contains(model.Threads, t => t.Events.Any(e => e.Name == "Opening"));
        var suspended = Assert.Single(model.SuspendedThreadUris);
        Assert.Equal(fh.NormalizeUri(Path.Combine(XmlDir, "Story_Act_II.xml")), suspended);
        Assert.Contains(model.Graph.Nodes, n => n.Label == "Opening");
    }

    [Fact]
    public void GetCampaignModel_UnknownCampaign_ReturnsNull()
    {
        var (service, _, _, _, _, _) = Build();

        Assert.Null(service.GetCampaignModel("No_Such_Campaign"));
    }

    [Fact]
    public void GetCampaignModel_NothingChanged_ReturnsCachedInstance()
    {
        var (service, _, _, _, _, _) = Build();

        var first = service.GetCampaignModel("GC_One");
        var second = service.GetCampaignModel("GC_One");

        Assert.Same(first, second);
    }

    [Fact]
    public void GetCampaignModel_ThreadEdited_RebuildsWithOpenBufferText()
    {
        var (service, _, index, host, fh, _) = Build();
        var threadUri = fh.NormalizeUri(Path.Combine(XmlDir, "Story_Act_I.xml"));
        var stale = service.GetCampaignModel("GC_One")!;

        // didChange: the host gets the new text, the index a new version for the document.
        host.AddOrUpdate(threadUri, "<Story><Event Name=\"Rewritten\"/></Story>", 2);
        index.SetDocumentVersion(threadUri, 2);

        var fresh = service.GetCampaignModel("GC_One")!;

        Assert.NotSame(stale, fresh);
        Assert.Contains(fresh.Graph.Nodes, n => n.Label == "Rewritten");
        Assert.DoesNotContain(fresh.Graph.Nodes, n => n.Label == "Opening");
    }

    [Fact]
    public void GetCampaignModel_LayeredThread_HighestRankRootWins()
    {
        var files = Fixture();
        // The dependency also ships Story_Act_I.xml with different content; the root project's
        // copy (last xml root = highest rank) must win.
        files[Path.Combine(DepXmlDir, "Story_Act_I.xml")] =
            new MockFileData("<Story><Event Name=\"DependencyVersion\"/></Story>");
        var (service, _, _, _, _, _) = Build(files, [DepXmlDir, XmlDir]);

        var model = service.GetCampaignModel("GC_One")!;

        Assert.Contains(model.Graph.Nodes, n => n.Label == "Opening");
        Assert.DoesNotContain(model.Graph.Nodes, n => n.Label == "DependencyVersion");
    }

    [Fact]
    public void GetModelsContaining_FindsCampaignsUsingTheThread()
    {
        var (service, _, _, _, fh, _) = Build();
        var threadUri = fh.NormalizeUri(Path.Combine(XmlDir, "Story_Act_I.xml"));

        var models = service.GetModelsContaining(threadUri);

        Assert.Equal(["GC_One"], models.Select(m => m.CampaignName));
        Assert.Empty(service.GetModelsContaining("file:///elsewhere.xml"));
    }

    [Fact]
    public void GetChainResult_ScanBeforeWorkspaceConfigLoads_IsNotCached()
    {
        // Startup window: a client request arrives before the pipeline has published the
        // workspace config. The scan reads nothing — that empty result must not stick.
        var (service, _, _, _, _, reload) = Build(configured: false);

        Assert.Empty(service.GetChainResult().Campaigns);

        reload.LastWorkspaceConfig = DefaultConfig();

        Assert.Equal(["GC_One"], service.GetCampaignNames());
    }

    [Fact]
    public void GetInvalidatedCampaigns_AfterStartupWindowScan_ReportsCampaignsOnceResolvable()
    {
        // The change notifier must be able to announce campaigns that only became resolvable
        // after startup, so the navigator's early empty answer gets corrected.
        var (service, _, _, _, _, reload) = Build(configured: false);
        _ = service.GetChainResult();

        reload.LastWorkspaceConfig = DefaultConfig();

        Assert.Equal(["GC_One"], service.GetInvalidatedCampaigns());
    }

    // ── fakes ────────────────────────────────────────────────────────────────

    private sealed class StubReloadService(WorkspaceConfiguration? config) : IModProjectReloadService
    {
        public IReadOnlyList<string>? LastAssetRoots => null;
        public WorkspaceConfiguration? LastWorkspaceConfig { get; set; } = config;
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

    private sealed class FakeHost : IGameWorkspaceHost
    {
        private readonly Dictionary<string, TrackedDocument> _docs = new(StringComparer.OrdinalIgnoreCase);

        public IEnumerable<TrackedDocument> All => _docs.Values;

        public void AddOrUpdate(string uri, string text, int version, bool publishDiagnostics = true)
        {
            _docs[uri] = new TrackedDocument(uri, text, version, publishDiagnostics);
        }

        public void Remove(string uri)
        {
            _docs.Remove(uri);
        }

        public bool TryGet(string uri, out TrackedDocument doc)
        {
            return _docs.TryGetValue(uri, out doc!);
        }
    }

    private sealed class MutableIndexService : IGameIndexService
    {
        public GameIndex Current { get; private set; } = GameIndex.Empty;

        public event Action<GameIndex>? IndexChanged
        {
            add { }
            remove { }
        }

        public event Action<ILocalisationIndex>? LocalisationChanged
        {
            add { }
            remove { }
        }

        public event Action<GameIndex>? DynamicEnumChanged
        {
            add { }
            remove { }
        }

        public void SetDocumentVersion(string uri, int version)
        {
            var doc = new DocumentIndex(uri, version,
                ImmutableArray<GameSymbol>.Empty, ImmutableArray<GameReference>.Empty);
            Current = Current with { Documents = Current.Documents.SetItem(uri, doc) };
        }

        public Task UpdateDocumentAsync(string uri, string text, int version, CancellationToken ct)
        {
            return Task.CompletedTask;
        }

        public Task OpenDocumentAsync(string uri, string text, int version, CancellationToken ct)
        {
            return Task.CompletedTask;
        }

        public void InjectDocument(DocumentIndex document)
        {
        }

        public void RemoveDocument(string uri)
        {
        }

        public void ApplyBaseline(BaselineIndex baseline)
        {
        }

        public void ApplyLocalisation(ILocalisationIndex index)
        {
        }

        public void ApplyAssetFiles(IAssetFileIndex index)
        {
        }

        public void ApplyModelBones(ImmutableDictionary<string, ImmutableArray<string>> bones)
        {
        }

        public void ApplyWorkspaceDynamicEnumValues(ImmutableDictionary<string, ImmutableArray<string>> values)
        {
        }

        public void ApplyWorkspaceEnumValueDefinitions(
            ImmutableDictionary<string, ImmutableDictionary<string, FileOrigin>> definitions)
        {
        }

        public IDisposable BeginBulkUpdate()
        {
            return new Noop();
        }

        private sealed class Noop : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }

    private sealed class SpecialDefSchemaProvider : ISchemaProvider
    {
        public event EventHandler? SchemaRefreshed
        {
            add { }
            remove { }
        }

        public IReadOnlyList<XmlTagDefinition> AllTags => [];
        public IReadOnlyList<GameObjectTypeDefinition> AllObjectTypes => [];
        public IReadOnlyList<EnumDefinition> AllEnums => [];
        public IReadOnlyList<HardcodedReferenceSet> AllHardcodedSets => [];

        public IReadOnlyList<MetafileDefinition> AllMetafiles =>
        [
            new("data/xml/campaignfiles.xml", MetafileType.Special,
                ["Campaign", "StoryParser", "StoryPlotManifest"])
        ];

        public XmlTagDefinition? GetTag(string t)
        {
            return null;
        }

        public IReadOnlyList<XmlTagDefinition> GetAllTagDefinitions(string t)
        {
            return [];
        }

        public IReadOnlyList<XmlTagDefinition> GetTagsForType(string t)
        {
            return [];
        }

        public EnumDefinition? GetEnum(string e)
        {
            return null;
        }

        public GameObjectTypeDefinition? GetObjectType(string t)
        {
            return null;
        }
    }
}

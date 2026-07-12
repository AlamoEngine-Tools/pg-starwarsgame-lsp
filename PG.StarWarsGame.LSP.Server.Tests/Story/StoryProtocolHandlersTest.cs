// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Localisation;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Server.Story;
using PG.StarWarsGame.LSP.Story.Discovery;
using PG.StarWarsGame.LSP.Story.Graph;
using PG.StarWarsGame.LSP.Story.Model;

namespace PG.StarWarsGame.LSP.Server.Tests.Story;

public sealed class StoryProtocolHandlersTest
{
    private const string ThreadUri = "file:///ws/data/xml/story_act_i.xml";
    private const string SuspendedUri = "file:///ws/data/xml/story_act_ii.xml";

    private static ILspConfigurationProvider Config(bool storyEditor = true, bool discovery = true)
    {
        return FakeLspConfigurationProvider.WithFeatures(new FeatureFlags
        {
            Tools = new ToolsFeatureFlags { StoryEditor = storyEditor },
            Story = new StoryFeatureFlags { Discovery = discovery }
        });
    }

    private static StubModelService Models()
    {
        return new StubModelService();
    }

    // ── aet/getStoryPlots ────────────────────────────────────────────────────

    [Fact]
    public async Task GetStoryPlots_ReturnsCampaignFactionManifestTree()
    {
        var result = await new GetStoryPlotsHandler(Models(), Config())
            .Handle(new GetStoryPlotsParams(), CancellationToken.None);

        Assert.Null(result.Error);
        var campaign = Assert.Single(result.Campaigns);
        Assert.Equal("GC", campaign.Name);
        var faction = Assert.Single(campaign.Factions);
        Assert.Equal("Rebel", faction.Faction);
        Assert.Equal(["Story_Lua"], faction.LuaScripts);
        Assert.Equal([("Story_Act_I.xml", false), ("Story_Act_II.xml", true)],
            faction.Threads.Select(t => (t.File, t.Suspended)));
    }

    [Fact]
    public async Task GetStoryPlots_StoryEditorOff_ReturnsDisabledMessage()
    {
        var result = await new GetStoryPlotsHandler(Models(), Config(storyEditor: false))
            .Handle(new GetStoryPlotsParams(), CancellationToken.None);

        Assert.Equal(StoryEditorFeature.DisabledMessage, result.Error);
        Assert.Empty(result.Campaigns);
    }

    [Fact]
    public async Task GetStoryPlots_DiscoveryOff_NamesTheMissingPrerequisite()
    {
        var result = await new GetStoryPlotsHandler(Models(), Config(discovery: false))
            .Handle(new GetStoryPlotsParams(), CancellationToken.None);

        Assert.Equal(StoryEditorFeature.DiscoveryMissingMessage, result.Error);
    }

    // ── aet/getStoryGraph ────────────────────────────────────────────────────

    [Fact]
    public async Task GetStoryGraph_ReturnsNodesWithLifecycleAndEdges()
    {
        var result = await new GetStoryGraphHandler(Models(), Config())
            .Handle(new GetStoryGraphParams("GC"), CancellationToken.None);

        Assert.Null(result.Error);
        var start = Assert.Single(result.Nodes, n => n.Label == "Start");
        Assert.Equal("Armed", start.Lifecycle);
        Assert.True(start.Reachable);
        var next = Assert.Single(result.Nodes, n => n.Label == "Next");
        Assert.Equal("Waiting", next.Lifecycle);
        Assert.Contains(result.Edges, e => e.Kind == "Prereq");
        // The suspended thread's event reports Inactive.
        Assert.Equal("Inactive", Assert.Single(result.Nodes, n => n.Label == "Later").Lifecycle);
    }

    [Fact]
    public async Task GetStoryGraph_NameFilter_KeepsMatchingEventsOnly()
    {
        var result = await new GetStoryGraphHandler(Models(), Config())
            .Handle(new GetStoryGraphParams("GC", NameFilter: "sta"), CancellationToken.None);

        Assert.Equal(["Start"], result.Nodes.Where(n => n.Kind == "Event").Select(n => n.Label));
    }

    [Fact]
    public async Task GetStoryGraph_BranchFilter_KeepsBranchMembers()
    {
        var result = await new GetStoryGraphHandler(Models(), Config())
            .Handle(new GetStoryGraphParams("GC", Branch: "Act1"), CancellationToken.None);

        Assert.Equal(["Next"], result.Nodes.Where(n => n.Kind == "Event").Select(n => n.Label));
    }

    [Fact]
    public async Task GetStoryGraph_ReachableFromFilter_KeepsDownstreamOnly()
    {
        var startId = $"{ThreadUri}#start";

        var result = await new GetStoryGraphHandler(Models(), Config())
            .Handle(new GetStoryGraphParams("GC", ReachableFrom: startId), CancellationToken.None);

        var labels = result.Nodes.Where(n => n.Kind == "Event").Select(n => n.Label).ToList();
        Assert.Contains("Start", labels);
        Assert.Contains("Next", labels);
        Assert.DoesNotContain("Later", labels);
    }

    [Fact]
    public async Task GetStoryGraph_UnknownCampaign_ReturnsError()
    {
        var result = await new GetStoryGraphHandler(Models(), Config())
            .Handle(new GetStoryGraphParams("Nope"), CancellationToken.None);

        Assert.Contains("Nope", result.Error);
    }

    // ── aet/getStoryNodeDetail ───────────────────────────────────────────────

    [Fact]
    public async Task GetStoryNodeDetail_ReturnsFullEventPayload()
    {
        var result = await new GetStoryNodeDetailHandler(Models(), Config())
            .Handle(new GetStoryNodeDetailParams("GC", $"{ThreadUri}#next"), CancellationToken.None);

        Assert.Null(result.Error);
        var node = result.Node!;
        Assert.Equal("Next", node.Name);
        Assert.Equal("STORY_TRIGGER", node.EventType);
        Assert.Equal([["Start"]], node.PrereqGroups);
        Assert.Equal("Act1", node.Branch);
        Assert.Contains(node.Tags, t => t.Name == "Event_Type");
    }

    [Fact]
    public async Task GetStoryNodeDetail_UnknownNode_ReturnsError()
    {
        var result = await new GetStoryNodeDetailHandler(Models(), Config())
            .Handle(new GetStoryNodeDetailParams("GC", "ghost"), CancellationToken.None);

        Assert.NotNull(result.Error);
        Assert.Null(result.Node);
    }

    // ── aet/getStorySchema ───────────────────────────────────────────────────

    [Fact]
    public async Task GetStorySchema_ProjectsEventAndRewardEnums()
    {
        var result = await new GetStorySchemaHandler(new ProtocolSchemaProvider(), Config())
            .Handle(new GetStorySchemaParams(), CancellationToken.None);

        Assert.Null(result.Error);
        var trigger = Assert.Single(result.RewardTypes, t => t.Name == "TRIGGER_EVENT");
        var param = Assert.Single(trigger.Params);
        Assert.Equal("StoryEventName", param.ReferenceType);
        Assert.True(Assert.Single(result.EventTypes, t => t.Name == "STORY_UNTESTED").Untested);
    }

    // ── aet/storyGraphChanged ────────────────────────────────────────────────

    [Fact]
    public void Notifier_InvalidatedCampaigns_SendsNotification()
    {
        var index = new FiringIndexService();
        var sent = new List<StoryGraphChangedParams>();
        _ = new StoryGraphChangeNotifier(index, Models(invalidated: ["GC"]), Config(),
            sent.Add, NullLogger<StoryGraphChangeNotifier>.Instance, debounceMs: 0);

        index.Fire();

        Assert.Equal(["GC"], Assert.Single(sent).Campaigns);
    }

    [Fact]
    public void Notifier_NothingInvalidated_StaysQuiet()
    {
        var index = new FiringIndexService();
        var sent = new List<StoryGraphChangedParams>();
        _ = new StoryGraphChangeNotifier(index, Models(), Config(),
            sent.Add, NullLogger<StoryGraphChangeNotifier>.Instance, debounceMs: 0);

        index.Fire();

        Assert.Empty(sent);
    }

    [Fact]
    public void Notifier_StoryEditorOff_StaysQuiet()
    {
        var index = new FiringIndexService();
        var sent = new List<StoryGraphChangedParams>();
        _ = new StoryGraphChangeNotifier(index, Models(invalidated: ["GC"]), Config(storyEditor: false),
            sent.Add, NullLogger<StoryGraphChangeNotifier>.Instance, debounceMs: 0);

        index.Fire();

        Assert.Empty(sent);
    }

    private static StubModelService Models(IReadOnlyList<string>? invalidated = null)
    {
        return new StubModelService { Invalidated = invalidated ?? [] };
    }

    // ── fakes ────────────────────────────────────────────────────────────────

    private sealed class StubModelService : IStoryModelService
    {
        public IReadOnlyList<string> Invalidated { get; init; } = [];

        // Active thread: Start → (prereq) Next[Branch=Act1]; suspended thread: Later.
        private static readonly StoryCampaignModel Model = BuildModel();

        private static StoryCampaignModel BuildModel()
        {
            var active = StoryThreadParser.Parse(
                "<Story><Event Name=\"Start\"><Event_Type>STORY_ELAPSED</Event_Type></Event>" +
                "<Event Name=\"Next\"><Event_Type>STORY_TRIGGER</Event_Type>" +
                "<Prereq>Start</Prereq><Branch>Act1</Branch></Event></Story>", ThreadUri);
            var suspended = StoryThreadParser.Parse(
                "<Story><Event Name=\"Later\"/></Story>", SuspendedUri);
            return new StoryCampaignModel("GC", [active, suspended],
                new HashSet<string>(StringComparer.Ordinal) { SuspendedUri },
                new StoryGraphBuilder(new ProtocolSchemaProvider()).Build([active, suspended]));
        }

        public IReadOnlyList<string> GetCampaignNames()
        {
            return ["GC"];
        }

        public StoryCampaignModel? GetCampaignModel(string campaignName)
        {
            return campaignName == "GC" ? Model : null;
        }

        public IReadOnlyList<StoryCampaignModel> GetModelsContaining(string canonicalUri)
        {
            return Model.Threads.Any(t => t.DocumentUri == canonicalUri) ? [Model] : [];
        }

        public StoryChainScanResult GetChainResult()
        {
            return StoryChainScanResult.Empty with
            {
                Campaigns = [new StoryCampaignChain("GC", [new StoryFactionManifest("Rebel", "M.xml")])],
                Manifests =
                [
                    new StoryManifestContents("M.xml", ["Story_Act_I.xml"], ["Story_Act_II.xml"], ["Story_Lua"])
                ]
            };
        }

        public IReadOnlyList<string> GetInvalidatedCampaigns()
        {
            return Invalidated;
        }
    }

    private sealed class FiringIndexService : IGameIndexService
    {
        public GameIndex Current => GameIndex.Empty;
        public event Action<GameIndex>? IndexChanged;

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

        public void Fire()
        {
            IndexChanged?.Invoke(GameIndex.Empty);
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

        public void ApplyAssetFiles(Core.Assets.IAssetFileIndex index)
        {
        }

        public void ApplyModelBones(
            System.Collections.Immutable.ImmutableDictionary<string, System.Collections.Immutable.ImmutableArray<string>> bones)
        {
        }

        public void ApplyWorkspaceDynamicEnumValues(
            System.Collections.Immutable.ImmutableDictionary<string, System.Collections.Immutable.ImmutableArray<string>> values)
        {
        }

        public void ApplyWorkspaceEnumValueDefinitions(
            System.Collections.Immutable.ImmutableDictionary<string,
                System.Collections.Immutable.ImmutableDictionary<string, FileOrigin>> definitions)
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

    private sealed class ProtocolSchemaProvider : ISchemaProvider
    {
        private static readonly EnumDefinition Events = new()
        {
            Name = "StoryEventType",
            Values =
            [
                new EnumValueDefinition { Name = "STORY_ELAPSED" },
                new EnumValueDefinition { Name = "STORY_TRIGGER" },
                new EnumValueDefinition { Name = "STORY_UNTESTED", Untested = true }
            ]
        };

        private static readonly EnumDefinition Rewards = new()
        {
            Name = "StoryRewardType",
            Values =
            [
                new EnumValueDefinition
                {
                    Name = "TRIGGER_EVENT",
                    Params =
                    [
                        new ParamDefinition
                        {
                            Position = 0, ValueType = XmlValueType.NameReference,
                            ReferenceTypeName = "StoryEventName"
                        }
                    ]
                }
            ]
        };

        public event EventHandler? SchemaRefreshed
        {
            add { }
            remove { }
        }

        public IReadOnlyList<XmlTagDefinition> AllTags => [];
        public IReadOnlyList<GameObjectTypeDefinition> AllObjectTypes => [];
        public IReadOnlyList<EnumDefinition> AllEnums => [Events, Rewards];
        public IReadOnlyList<HardcodedReferenceSet> AllHardcodedSets => [];
        public IReadOnlyList<MetafileDefinition> AllMetafiles => [];

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

        public EnumDefinition? GetEnum(string name)
        {
            if (string.Equals(name, Events.Name, StringComparison.OrdinalIgnoreCase)) return Events;
            if (string.Equals(name, Rewards.Name, StringComparison.OrdinalIgnoreCase)) return Rewards;
            return null;
        }

        public GameObjectTypeDefinition? GetObjectType(string t)
        {
            return null;
        }
    }
}

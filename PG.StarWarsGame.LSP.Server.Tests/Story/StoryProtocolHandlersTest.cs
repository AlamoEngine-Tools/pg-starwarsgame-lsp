// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging.Abstractions;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Localisation;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Server.Story;
using PG.StarWarsGame.LSP.Story.Discovery;
using PG.StarWarsGame.LSP.Story.Graph;
using PG.StarWarsGame.LSP.Story.Model;
using PG.StarWarsGame.LSP.Xml;

namespace PG.StarWarsGame.LSP.Server.Tests.Story;

public sealed class StoryProtocolHandlersTest
{
    private const string ThreadUri = "file:///ws/data/xml/story_act_i.xml";
    private const string SuspendedUri = "file:///ws/data/xml/story_act_ii.xml";
    private const string LuaUri = "file:///ws/data/scripts/story/story_lua.lua";

    private static FiringIndexService Index()
    {
        var documents = GameIndex.Empty.Documents.Add(
            LuaUri, new DocumentIndex(LuaUri, 1, [], []));
        return new FiringIndexService { Current = GameIndex.Empty with { Documents = documents } };
    }

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
        var result = await new GetStoryPlotsHandler(Models(), Index(), Config())
            .Handle(new GetStoryPlotsParams(), CancellationToken.None);

        Assert.Null(result.Error);
        var campaign = Assert.Single(result.Campaigns);
        Assert.Equal("GC", campaign.Name);
        var faction = Assert.Single(campaign.Factions);
        Assert.Equal("Rebel", faction.Faction);
        Assert.Equal(["Story_Lua"], faction.LuaScripts.Select(s => s.Name));
        Assert.Equal([("Story_Act_I.xml", false), ("Story_Act_II.xml", true)],
            faction.Threads.Select(t => (t.File, t.Suspended)));
    }

    [Fact]
    public async Task GetStoryPlots_ResolvesLuaScriptUrisFromTheIndex()
    {
        // Manifest Lua_Script entries are extensionless engine-cased names ("Story_Lua"); the
        // indexed document is lowercase with extension. Unindexed scripts resolve to null.
        var result = await new GetStoryPlotsHandler(Models(), Index(), Config())
            .Handle(new GetStoryPlotsParams(), CancellationToken.None);

        var faction = Assert.Single(Assert.Single(result.Campaigns).Factions);
        Assert.Equal([LuaUri], faction.LuaScripts.Select(s => s.Uri));
    }

    [Fact]
    public async Task GetStoryPlots_ResolvesThreadUrisCaseInsensitively()
    {
        // Manifest entries carry engine casing ("Story_Act_I.xml"); canonical thread URIs are
        // lowercase. The client must receive the resolved URI — it cannot search case-insensitively.
        var result = await new GetStoryPlotsHandler(Models(), Index(), Config())
            .Handle(new GetStoryPlotsParams(), CancellationToken.None);

        var faction = Assert.Single(Assert.Single(result.Campaigns).Factions);
        Assert.Equal([ThreadUri, SuspendedUri], faction.Threads.Select(t => t.Uri));
    }

    [Fact]
    public async Task GetStoryPlots_StoryEditorOff_ReturnsDisabledMessage()
    {
        var result = await new GetStoryPlotsHandler(Models(), Index(), Config(storyEditor: false))
            .Handle(new GetStoryPlotsParams(), CancellationToken.None);

        Assert.Equal(StoryEditorFeature.DisabledMessage, result.Error);
        Assert.Empty(result.Campaigns);
    }

    [Fact]
    public async Task GetStoryPlots_DiscoveryOff_NamesTheMissingPrerequisite()
    {
        var result = await new GetStoryPlotsHandler(Models(), Index(), Config(discovery: false))
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
    public async Task GetStoryGraph_EventNodes_CarryFullParamDataForInlineRendering()
    {
        // The graph fetch must be self-sufficient for the node body (no per-node detail round
        // trip needed) — same fields aet/getStoryNodeDetail already projects for "Next".
        var result = await new GetStoryGraphHandler(Models(), Config())
            .Handle(new GetStoryGraphParams("GC"), CancellationToken.None);

        var next = Assert.Single(result.Nodes, n => n.Label == "Next");
        Assert.Equal("Act1", next.Branch);
        Assert.False(next.Perpetual);
        Assert.Null(next.StoryDialog);
        Assert.NotNull(next.EventParams);
        Assert.NotNull(next.RewardParams);
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

    [Fact]
    public async Task GetStorySchema_EnumParams_ShipEnumValuesInline()
    {
        var result = await new GetStorySchemaHandler(new ProtocolSchemaProvider(), Config())
            .Handle(new GetStorySchemaParams(), CancellationToken.None);

        var battle = Assert.Single(result.RewardTypes, t => t.Name == "LINK_BATTLE");
        var modeParam = Assert.Single(battle.Params, p => p.EnumName == "StoryBattleMode");
        Assert.Equal(["GROUND", "SPACE"], modeParam.EnumValues);
        // Non-enum params carry no values list.
        var trigger = Assert.Single(result.RewardTypes, t => t.Name == "TRIGGER_EVENT");
        Assert.Null(Assert.Single(trigger.Params).EnumValues);
    }

    // ── aet/getStoryParamOptions ─────────────────────────────────────────────

    private static GetStoryParamOptionsHandler OptionsHandler(
        FiringIndexService? index = null, bool storyEditor = true)
    {
        return new GetStoryParamOptionsHandler(Models(), index ?? Index(), new ProtocolSchemaProvider(),
            new Xml.Completion.StoryParamValueProposalProvider(), Config(storyEditor: storyEditor));
    }

    private static FiringIndexService IndexWith(params (string Id, string TypeName)[] symbols)
    {
        var definitions = GameIndex.Empty.WorkspaceDefinitions;
        foreach (var (id, typeName) in symbols)
            definitions = definitions.Add(id, [
                new GameSymbol(id, GameSymbolKind.XmlObject, typeName,
                    new FileOrigin($"file:///ws/data/xml/{typeName.ToLowerInvariant()}s.xml", 7, 2), null)
            ]);
        return new FiringIndexService { Current = GameIndex.Empty with { WorkspaceDefinitions = definitions } };
    }

    [Fact]
    public async Task GetStoryParamOptions_StoryEventNameParam_ReturnsCampaignEventNames()
    {
        var result = await OptionsHandler()
            .Handle(new GetStoryParamOptionsParams("GC", "reward", "TRIGGER_EVENT", 0),
                CancellationToken.None);

        Assert.Null(result.Error);
        Assert.Equal(["Later", "Next", "Start"], result.Options.Select(o => o.Value));
    }

    [Fact]
    public async Task GetStoryParamOptions_PrefixFiltersCampaignNames()
    {
        var result = await OptionsHandler()
            .Handle(new GetStoryParamOptionsParams("GC", "reward", "TRIGGER_EVENT", 0, Prefix: "ne"),
                CancellationToken.None);

        Assert.Equal(["Next"], result.Options.Select(o => o.Value));
    }

    [Fact]
    public async Task GetStoryParamOptions_ReferenceParam_UsesIndexSymbols()
    {
        var index = IndexWith(("Coruscant", "Planet"), ("Tatooine", "Planet"), ("X_Wing", "SpaceUnit"));

        var result = await OptionsHandler(index)
            .Handle(new GetStoryParamOptionsParams("GC", "event", "STORY_ENTER", 0, Prefix: "c"),
                CancellationToken.None);

        Assert.Equal(["Coruscant"], result.Options.Select(o => o.Value));
    }

    [Fact]
    public async Task GetStoryParamOptions_UnknownTypeOrPosition_ReturnsEmptyWithoutError()
    {
        var result = await OptionsHandler()
            .Handle(new GetStoryParamOptionsParams("GC", "event", "STORY_NOPE", 0), CancellationToken.None);

        Assert.Null(result.Error);
        Assert.Empty(result.Options);
    }

    [Fact]
    public async Task GetStoryParamOptions_LimitCapsTheResult()
    {
        var result = await OptionsHandler()
            .Handle(new GetStoryParamOptionsParams("GC", "reward", "TRIGGER_EVENT", 0, Limit: 1),
                CancellationToken.None);

        Assert.Single(result.Options);
    }

    [Fact]
    public async Task GetStoryParamOptions_StoryEditorOff_ReturnsDisabledMessage()
    {
        var result = await OptionsHandler(storyEditor: false)
            .Handle(new GetStoryParamOptionsParams("GC", "reward", "TRIGGER_EVENT", 0),
                CancellationToken.None);

        Assert.Equal(StoryEditorFeature.DisabledMessage, result.Error);
    }

    // ── aet/getStoryDiagnostics ──────────────────────────────────────────────

    private sealed class FakeCollector : IXmlDiagnosticsCollector
    {
        public List<(string Uri, Diagnostic Diagnostic)> Seed { get; } = [];

        public IReadOnlyList<Diagnostic> Collect(string uri, string text, GameIndex index)
        {
            return Seed.Where(s => s.Uri == uri).Select(s => s.Diagnostic).ToList();
        }
    }

    private sealed class FakeTextSource : IDocumentTextSource
    {
        public Dictionary<string, string> Texts { get; } = new(StringComparer.Ordinal);

        public DocumentText? GetText(string canonicalUri)
        {
            return Texts.TryGetValue(canonicalUri, out var text) ? new DocumentText(text, 0, false) : null;
        }
    }

    private static Diagnostic Diag(int line, int character, DiagnosticSeverity severity, string message)
    {
        return new Diagnostic
        {
            Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(
                line, character, line, character + 4),
            Severity = severity,
            Message = message
        };
    }

    private static (GetStoryDiagnosticsHandler Handler, FakeCollector Collector) DiagnosticsHandler()
    {
        var collector = new FakeCollector();
        var textSource = new FakeTextSource();
        // The stub model's threads were parsed from these exact strings — ranges line up.
        textSource.Texts[ThreadUri] =
            "<Story><Event Name=\"Start\"><Event_Type>STORY_ELAPSED</Event_Type></Event>" +
            "<Event Name=\"Next\"><Event_Type>STORY_TRIGGER</Event_Type>" +
            "<Prereq>Start</Prereq><Branch>Act1</Branch></Event></Story>";
        textSource.Texts[SuspendedUri] = "<Story><Event Name=\"Later\"/></Story>";
        var handler = new GetStoryDiagnosticsHandler(
            Models(), Index(), collector, textSource, Config());
        return (handler, collector);
    }

    [Fact]
    public async Task GetStoryDiagnostics_CorrelatesToNodeAndParamSlot()
    {
        var (handler, collector) = DiagnosticsHandler();
        // The stub thread is a single line; "STORY_ELAPSED" (Start's Event_Param-free type) sits
        // inside Start's event range. Aim at the Event_Type VALUE — event-level, no param slot.
        collector.Seed.Add((ThreadUri, Diag(0, 40, DiagnosticSeverity.Error, "bad value")));

        var result = await handler.Handle(new GetStoryDiagnosticsParams("GC"), CancellationToken.None);

        Assert.Null(result.Error);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal($"{ThreadUri}#start", diagnostic.NodeId);
        Assert.Equal("error", diagnostic.Severity);
        Assert.Equal("bad value", diagnostic.Message);
        Assert.Equal(ThreadUri, diagnostic.Uri);
    }

    [Fact]
    public async Task GetStoryDiagnostics_OutsideAnyEvent_HasNoNodeId()
    {
        var (handler, collector) = DiagnosticsHandler();
        collector.Seed.Add((SuspendedUri, Diag(0, 1, DiagnosticSeverity.Warning, "file-level")));

        var result = await handler.Handle(new GetStoryDiagnosticsParams("GC"), CancellationToken.None);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Null(diagnostic.NodeId); // column 1 = the <Story> root, before any event
        Assert.Equal("warning", diagnostic.Severity);
    }

    [Fact]
    public async Task GetStoryDiagnostics_StoryEditorOff_ReturnsDisabledMessage()
    {
        var collector = new FakeCollector();
        var handler = new GetStoryDiagnosticsHandler(
            Models(), Index(), collector, new FakeTextSource(), Config(storyEditor: false));

        var result = await handler.Handle(new GetStoryDiagnosticsParams("GC"), CancellationToken.None);

        Assert.Equal(StoryEditorFeature.DisabledMessage, result.Error);
    }

    // ── aet/resolveStoryReference ────────────────────────────────────────────

    private static ResolveStoryReferenceHandler ResolveHandler(
        FiringIndexService index, bool storyEditor = true)
    {
        return new ResolveStoryReferenceHandler(index, new ProtocolSchemaProvider(),
            Config(storyEditor: storyEditor));
    }

    [Fact]
    public async Task ResolveStoryReference_WorkspaceSymbol_ReturnsFileLocation()
    {
        var index = IndexWith(("Coruscant", "Planet"));

        var result = await ResolveHandler(index)
            .Handle(new ResolveStoryReferenceParams("Coruscant", "Planet"), CancellationToken.None);

        Assert.Null(result.Error);
        Assert.Equal("file:///ws/data/xml/planets.xml", result.Uri);
        Assert.Equal(7, result.Line);
        Assert.Equal(2, result.Column);
    }

    [Fact]
    public async Task ResolveStoryReference_TypedPreference_PicksTheMatchingType()
    {
        // Same id defined as a StoryEvent and a SpaceUnit — a StoryEventName reference must land
        // on the StoryEvent definition, not whichever layer ranks higher.
        var definitions = GameIndex.Empty.WorkspaceDefinitions.Add("Start", [
            new GameSymbol("Start", GameSymbolKind.XmlObject, "SpaceUnit",
                new FileOrigin("file:///ws/data/xml/units.xml", 1, 0), null),
            new GameSymbol("Start", GameSymbolKind.XmlObject, "StoryEvent",
                new FileOrigin("file:///ws/data/xml/story_act_i.xml", 42, 15), null)
        ]);
        var index = new FiringIndexService
        {
            Current = GameIndex.Empty with { WorkspaceDefinitions = definitions }
        };

        var result = await ResolveHandler(index)
            .Handle(new ResolveStoryReferenceParams("Start", "StoryEventName"), CancellationToken.None);

        Assert.Equal("file:///ws/data/xml/story_act_i.xml", result.Uri);
        Assert.Equal(42, result.Line);
    }

    [Fact]
    public async Task ResolveStoryReference_ScopedAbilityId_ResolvesFromBareName()
    {
        var index = IndexWith(("MY_UNIT$Medic_Healing", "UnitAbility"));

        var result = await ResolveHandler(index)
            .Handle(new ResolveStoryReferenceParams("Medic_Healing", "SpecialAbility"), CancellationToken.None);

        Assert.Null(result.Error);
        Assert.Equal("file:///ws/data/xml/unitabilitys.xml", result.Uri);
    }

    [Fact]
    public async Task ResolveStoryReference_NonFileOrigin_ExplainsWhyNotNavigable()
    {
        var definitions = GameIndex.Empty.WorkspaceDefinitions.Add("Coruscant", [
            new GameSymbol("Coruscant", GameSymbolKind.XmlObject, "Planet", new UnknownOrigin("meg"), null)
        ]);
        var index = new FiringIndexService
        {
            Current = GameIndex.Empty with { WorkspaceDefinitions = definitions }
        };

        var result = await ResolveHandler(index)
            .Handle(new ResolveStoryReferenceParams("Coruscant", "Planet"), CancellationToken.None);

        Assert.Null(result.Uri);
        Assert.Contains("base game", result.Error);
    }

    [Fact]
    public async Task ResolveStoryReference_Unknown_ReturnsError()
    {
        var result = await ResolveHandler(IndexWith())
            .Handle(new ResolveStoryReferenceParams("Ghost", "Planet"), CancellationToken.None);

        Assert.Null(result.Uri);
        Assert.Contains("Ghost", result.Error);
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
        public GameIndex Current { get; init; } = GameIndex.Empty;
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
                new EnumValueDefinition { Name = "STORY_UNTESTED", Untested = true },
                new EnumValueDefinition
                {
                    Name = "STORY_ENTER",
                    Params =
                    [
                        new ParamDefinition
                        {
                            Position = 0, ValueType = XmlValueType.NameReferenceList,
                            ReferenceTypeName = "Planet"
                        }
                    ]
                }
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
                },
                new EnumValueDefinition
                {
                    Name = "LINK_BATTLE",
                    Params =
                    [
                        new ParamDefinition
                        {
                            Position = 0, ValueType = XmlValueType.DynamicEnumValue,
                            Enum = new EnumDefinition
                            {
                                Name = "StoryBattleMode",
                                Values =
                                [
                                    new EnumValueDefinition { Name = "GROUND" },
                                    new EnumValueDefinition { Name = "SPACE" }
                                ]
                            }
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

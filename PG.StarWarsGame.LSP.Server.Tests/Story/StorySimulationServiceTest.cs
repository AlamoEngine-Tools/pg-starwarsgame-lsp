// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Localisation;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Server.Story;
using PG.StarWarsGame.LSP.Story.Discovery;
using PG.StarWarsGame.LSP.Story.Graph;
using PG.StarWarsGame.LSP.Story.Model;

namespace PG.StarWarsGame.LSP.Server.Tests.Story;

public sealed class StorySimulationServiceTest
{
    private const string ThreadUri = "file:///ws/data/xml/story_main.xml";
    private const string LuaUri = "file:///ws/data/scripts/story/story_lua.lua";

    private const string ThreadText =
        "<Story>\n" +
        "\t<Event Name=\"Begin\">\n" +
        "\t\t<Event_Type>STORY_ELAPSED</Event_Type>\n" +
        "\t\t<Event_Param1>0</Event_Param1>\n" +
        "\t</Event>\n" +
        "\t<Event Name=\"Manual\">\n" +
        "\t\t<Event_Type>STORY_GENERIC</Event_Type>\n" +
        "\t\t<Prereq>Begin</Prereq>\n" +
        "\t</Event>\n" +
        "\t<Event Name=\"Later\">\n" +
        "\t\t<Event_Type>STORY_ELAPSED</Event_Type>\n" +
        "\t\t<Event_Param1>2</Event_Param1>\n" +
        "\t</Event>\n" +
        "</Story>\n";

    /// <summary>The campaign faction every fixture here runs as; a session is keyed by both.</summary>
    private static readonly StorySimKey Key = new("GC", "Rebel");

    private static (StorySimulationService Service, List<StorySimKey> Notified) BuildService()
    {
        var notified = new List<StorySimKey>();
        var service = new StorySimulationService(
            new StubModelService(), new StubIndexService(IndexWithLuaSymbol()),
            new SimEnumSchema(), notified.Add);
        return (service, notified);
    }

    private static GameIndex IndexWithLuaSymbol()
    {
        var symbol = new GameSymbol("Alert_From_Lua", GameSymbolKind.XmlObject,
            StoryReferenceTypes.NotificationSymbol, new FileOrigin(LuaUri, 3, 0), null);
        return GameIndex.Empty with
        {
            WorkspaceDefinitions = ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty
                .WithComparers(StringComparer.OrdinalIgnoreCase)
                .Add("Alert_From_Lua", [symbol])
        };
    }

    [Fact]
    public void Start_RunsTheInitialCascade_AndReportsLifecycles()
    {
        var (service, notified) = BuildService();

        var (state, error) = service.Start(Key);

        Assert.Null(error);
        Assert.True(state!.Running);
        Assert.Contains(state.Nodes, n => n.Lifecycle == "Fired"); // Begin (elapsed 0)
        Assert.Contains(state.Nodes, n => n.Lifecycle == "Armed"); // Manual
        Assert.Equal([Key], notified);
    }

    [Fact]
    public void Start_UnknownCampaign_ReturnsError()
    {
        var (service, _) = BuildService();

        var (state, error) = service.Start(new StorySimKey("Nope", "Rebel"));

        Assert.Null(state);
        Assert.Contains("Nope", error);
    }

    [Fact]
    public void GetState_WithoutSession_ReportsNotRunning()
    {
        var (service, _) = BuildService();

        var (state, error) = service.GetState(Key);

        Assert.Null(error);
        Assert.False(state!.Running);
    }

    [Fact]
    public void SatisfyTrigger_AdvancesTheSession_AndNotifies()
    {
        var (service, notified) = BuildService();
        service.Start(Key);
        var armed = service.GetState(Key).State!.Interventions.Single();

        var (state, error) = service.SatisfyTrigger(Key, armed.NodeId);

        Assert.Null(error);
        Assert.Empty(state!.Interventions);
        Assert.Equal(2, notified.Count);
    }

    [Fact]
    public void Mutations_WithoutSession_ReturnError()
    {
        var (service, _) = BuildService();

        var (_, error) = service.SetFlag(Key, "FLAG_X", 1);

        Assert.Contains("No simulation is running", error);
    }

    [Fact]
    public void Stop_EndsTheSession()
    {
        var (service, _) = BuildService();
        service.Start(Key);

        service.Stop(Key);

        Assert.False(service.GetState(Key).State!.Running);
    }

    [Fact]
    public void Start_CollectsLuaNotificationCatalogue_FromCampaignScripts()
    {
        var (service, _) = BuildService();

        var (state, _) = service.Start(Key);

        Assert.Equal(["Alert_From_Lua"], state!.LuaNotifications);
    }

    [Fact]
    public void Tick_ReturnsOnlyTheStepsAfterSinceSeq()
    {
        var (service, _) = BuildService();
        var started = service.Start(Key).State!;

        var (state, error) = service.Tick(Key, 2, started.TotalSteps);

        Assert.Null(error);
        Assert.Equal(2, state!.Tick);
        Assert.Equal(2.0, state.Clock);
        Assert.Equal(1.0, state.ClockStepSeconds);
        Assert.All(state.Steps, s => Assert.True(s.Seq >= started.TotalSteps));
        Assert.Contains(state.Steps, s => s.Cause == "poll" && s.To == "Fired");
        Assert.True(state.TotalSteps > started.TotalSteps);
        Assert.Equal(1, state.Nodes.Single(n => n.NodeId.EndsWith("#later", StringComparison.Ordinal)).FireCount);
    }

    [Fact]
    public void Seek_RestoresTheStateJustAfterThatTick_AndDropsTheFuture()
    {
        var (service, _) = BuildService();
        service.Start(Key);
        service.Tick(Key, 3);
        service.SetFlag(Key, "FLAG_X", 1);
        service.Tick(Key, 2);
        Assert.Equal(5, service.GetState(Key).State!.Tick);

        var (state, error) = service.Seek(Key, 3);

        Assert.Null(error);
        Assert.Equal(3, state!.Tick);
        Assert.Empty(state.Flags);

        // The future is gone: ticking again continues from tick 3.
        Assert.Equal(4, service.Tick(Key, 1).State!.Tick);
        Assert.Empty(service.GetState(Key).State!.Flags);
    }

    [Fact]
    public void Breakpoints_HaltATickRun_AndAreReportedOnTheState()
    {
        var (service, _) = BuildService();
        service.Start(Key);
        var later = service.GetState(Key).State!.Nodes
            .Single(n => n.NodeId.EndsWith("#later", StringComparison.Ordinal)).NodeId;

        var (state, error) = service.SetBreakpoints(Key, [later], false);
        Assert.Null(error);
        Assert.Equal([later], state!.Breakpoints);

        var ticked = service.Tick(Key, 5).State!;
        Assert.Equal(2, ticked.Tick);
        Assert.Equal(later, ticked.HaltedAt);
    }

    [Fact]
    public void WorldChange_WritesTheFact_AndTheStateCarriesTheWorld()
    {
        var (service, _) = BuildService();
        service.Start(Key);

        var (state, error) = service.ApplyWorldChange(Key,
            new StorySimWorldChangeDto("capturePlanet") { Planet = "Kuat", Faction = "Rebel" });

        Assert.Null(error);
        var kuat = Assert.Single(state!.World.Planets, p => p.Name == "Kuat");
        Assert.Equal("Rebel", kuat.Owner);
        Assert.Contains(state.Steps, s => s.Cause == "fact");
        Assert.Equal(1, state.World.Counters.Single(c => c.Name == "conquered|Rebel").Value);
    }

    [Fact]
    public void Interventions_CarryFacetAndSuggestedChange_OnTheWire()
    {
        var (service, _) = BuildService();
        service.Start(Key);

        var manual = service.GetState(Key).State!.Interventions.Single(i => i.EventName == "Manual");

        // STORY_GENERIC with no sub-type: a generic facet with no candidate, so no suggestion.
        Assert.Equal("generic", manual.Facet);
        Assert.Null(manual.Suggested);
    }

    [Fact]
    public void State_CarriesEachScriptsMachine_WithItsOwedEmissions()
    {
        var (service, _) = BuildService();
        service.Start(Key);
        var manual = service.GetState(Key).State!.Interventions.Single(i => i.EventName == "Manual");

        var fired = service.SatisfyTrigger(Key, manual.NodeId).State!;
        var script = Assert.Single(fired.LuaStates);
        Assert.Equal("story_lua", script.ScriptName);
        Assert.Null(script.Current);
        Assert.Equal("Manual", script.Next);
        Assert.Contains(fired.Steps, s => s.Cause == "luaTrigger");

        var entered = service.Tick(Key, 1).State!;
        script = Assert.Single(entered.LuaStates);
        Assert.Equal("Manual", script.Current);
        var owed = Assert.Single(script.Pending);
        Assert.Equal("Alert_From_Lua", owed.Id);
        Assert.Equal(6.0, owed.DueClock);
    }

    [Fact]
    public void RunToDecision_StopsAtTheFirstIntervention()
    {
        var (service, _) = BuildService();
        service.Start(Key);

        var (state, error) = service.RunToDecision(Key);

        Assert.Null(error);
        Assert.Equal(1, state!.Tick);
        Assert.Single(state.Interventions);
    }

    [Fact]
    public void State_CarriesWhatTheClockAloneCanStillChange()
    {
        var (service, _) = BuildService();
        var started = service.Start(Key).State!;
        Assert.True(started.ClockPending > 0, "a timer is armed at start");

        // Manual is the new decision at tick 1, but Later's 2 s timer is still running.
        var ran = service.RunToDecision(Key).State!;
        Assert.Equal(1, ran.ClockPending);

        // Once Later has fired nothing but the author can move the story.
        var settled = service.Tick(Key, 5).State!;
        Assert.Equal(0, settled.ClockPending);
        Assert.Equal(0, StorySimStateDto.NotRunning.ClockPending);
    }

    // ── Handler gating ───────────────────────────────────────────────────────

    [Fact]
    public async Task Handlers_SimulatorFlagOff_ReturnDisabledMessage()
    {
        var (service, _) = BuildService();
        var config = FakeLspConfigurationProvider.WithFeatures(new FeatureFlags
        {
            Tools = new ToolsFeatureFlags { StorySimulator = false }
        });

        var result = await new StorySimStartHandler(service, config)
            .Handle(new StorySimStartParams("GC", "Rebel"), CancellationToken.None);

        Assert.Equal(StorySimFeature.DisabledMessage, result.Error);
    }

    [Fact]
    public async Task Handlers_FlagOn_PassThrough()
    {
        var (service, _) = BuildService();
        var config = FakeLspConfigurationProvider.WithFeatures(new FeatureFlags());

        var started = await new StorySimStartHandler(service, config)
            .Handle(new StorySimStartParams("GC", "Rebel"), CancellationToken.None);
        var advanced = await new StorySimAdvanceClockHandler(service, config)
            .Handle(new StorySimAdvanceClockParams("GC", "Rebel", 5), CancellationToken.None);
        var ticked = await new StorySimTickHandler(service, config)
            .Handle(new StorySimTickParams("GC", "Rebel", 1, advanced.State!.TotalSteps), CancellationToken.None);

        Assert.Null(started.Error);
        Assert.Equal(5, advanced.State!.Clock);
        Assert.Equal(6, ticked.State!.Tick);
        Assert.All(ticked.State.Steps, s => Assert.True(s.Seq >= advanced.State.TotalSteps));
    }

    // ── fakes ────────────────────────────────────────────────────────────────

    private sealed class StubModelService : IStoryModelService
    {
        private static readonly StoryCampaignModel Model = BuildModel();

        public IReadOnlyList<string> GetCampaignNames()
        {
            return ["GC"];
        }

        public IReadOnlyList<StoryModelKey> GetModelKeys()
        {
            return GetCampaignNames()
                .Select(c => new StoryModelKey(c, "Rebel")).ToList();
        }

        public StoryCampaignModel? GetCampaignModel(string campaignName, string faction)
        {
            return campaignName == "GC" ? Model : null;
        }

        public IReadOnlyList<StoryCampaignModel> GetModelsContaining(string canonicalUri)
        {
            return [];
        }

        public StoryChainScanResult GetChainResult()
        {
            return StoryChainScanResult.Empty;
        }

        public IReadOnlyList<string> GetInvalidatedCampaigns()
        {
            return [];
        }

        private static StoryCampaignModel BuildModel()
        {
            var thread = StoryThreadParser.Parse(ThreadText, ThreadUri);
            // The script answers to the Manual event and, five seconds in, calls Story_Event.
            var machine = new LuaStoryMachine(LuaUri, "story_lua",
            [
                new LuaStoryState("Manual", "State_Manual",
                    new LuaStoryPhase([new LuaStoryEmission("Alert_From_Lua", 5)], [], [], ["Talk"]),
                    LuaStoryPhase.Empty, LuaStoryPhase.Empty)
            ]);
            return new StoryCampaignModel("GC", "Rebel", [thread],
                new HashSet<string>(StringComparer.Ordinal),
                new StoryGraphBuilder(new SimEnumSchema()).Build([thread], null, [machine]))
            {
                LuaScripts = ["Story_Lua"],
                LuaMachines = [machine]
            };
        }
    }

    private sealed class SimEnumSchema : ISchemaProvider
    {
        private static readonly EnumDefinition Events = new()
        {
            Name = "StoryEventType",
            Values =
            [
                new EnumValueDefinition { Name = "STORY_ELAPSED" },
                new EnumValueDefinition { Name = "STORY_GENERIC" }
            ]
        };

        public event EventHandler? SchemaRefreshed
        {
            add { }
            remove { }
        }

        public IReadOnlyList<XmlTagDefinition> AllTags => [];
        public IReadOnlyList<GameObjectTypeDefinition> AllObjectTypes => [];
        public IReadOnlyList<EnumDefinition> AllEnums => [Events];
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
            return string.Equals(name, Events.Name, StringComparison.OrdinalIgnoreCase) ? Events : null;
        }

        public GameObjectTypeDefinition? GetObjectType(string t)
        {
            return null;
        }
    }

    private sealed class StubIndexService(GameIndex index) : IGameIndexService
    {
        public GameIndex Current => index;

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
}
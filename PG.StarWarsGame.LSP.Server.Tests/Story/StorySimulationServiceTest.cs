// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
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
        "</Story>\n";

    private static (StorySimulationService Service, List<string> Notified) BuildService()
    {
        var notified = new List<string>();
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

        var (state, error) = service.Start("GC");

        Assert.Null(error);
        Assert.True(state!.Running);
        Assert.Contains(state.Nodes, n => n.Lifecycle == "Fired");   // Begin (elapsed 0)
        Assert.Contains(state.Nodes, n => n.Lifecycle == "Armed");   // Manual
        Assert.Equal(["GC"], notified);
    }

    [Fact]
    public void Start_UnknownCampaign_ReturnsError()
    {
        var (service, _) = BuildService();

        var (state, error) = service.Start("Nope");

        Assert.Null(state);
        Assert.Contains("Nope", error);
    }

    [Fact]
    public void GetState_WithoutSession_ReportsNotRunning()
    {
        var (service, _) = BuildService();

        var (state, error) = service.GetState("GC");

        Assert.Null(error);
        Assert.False(state!.Running);
    }

    [Fact]
    public void SatisfyTrigger_AdvancesTheSession_AndNotifies()
    {
        var (service, notified) = BuildService();
        service.Start("GC");
        var armed = service.GetState("GC").State!.Interventions.Single();

        var (state, error) = service.SatisfyTrigger("GC", armed.NodeId);

        Assert.Null(error);
        Assert.Empty(state!.Interventions);
        Assert.Equal(2, notified.Count);
    }

    [Fact]
    public void Mutations_WithoutSession_ReturnError()
    {
        var (service, _) = BuildService();

        var (_, error) = service.SetFlag("GC", "FLAG_X", 1);

        Assert.Contains("No simulation is running", error);
    }

    [Fact]
    public void Stop_EndsTheSession()
    {
        var (service, _) = BuildService();
        service.Start("GC");

        service.Stop("GC");

        Assert.False(service.GetState("GC").State!.Running);
    }

    [Fact]
    public void Start_CollectsLuaNotificationCatalogue_FromCampaignScripts()
    {
        var (service, _) = BuildService();

        var (state, _) = service.Start("GC");

        Assert.Equal(["Alert_From_Lua"], state!.LuaNotifications);
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
            .Handle(new StorySimStartParams("GC"), CancellationToken.None);

        Assert.Equal(StorySimFeature.DisabledMessage, result.Error);
    }

    [Fact]
    public async Task Handlers_FlagOn_PassThrough()
    {
        var (service, _) = BuildService();
        var config = FakeLspConfigurationProvider.WithFeatures(new FeatureFlags());

        var started = await new StorySimStartHandler(service, config)
            .Handle(new StorySimStartParams("GC"), CancellationToken.None);
        var advanced = await new StorySimAdvanceClockHandler(service, config)
            .Handle(new StorySimAdvanceClockParams("GC", 5), CancellationToken.None);

        Assert.Null(started.Error);
        Assert.Equal(5, advanced.State!.Clock);
    }

    // ── fakes ────────────────────────────────────────────────────────────────

    private sealed class StubModelService : IStoryModelService
    {
        private static readonly StoryCampaignModel Model = BuildModel();

        private static StoryCampaignModel BuildModel()
        {
            var thread = StoryThreadParser.Parse(ThreadText, ThreadUri);
            return new StoryCampaignModel("GC", [thread],
                new HashSet<string>(StringComparer.Ordinal),
                new StoryGraphBuilder(new SimEnumSchema()).Build([thread]))
            {
                LuaScripts = ["Story_Lua"]
            };
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

        public void ApplyAssetFiles(Core.Assets.IAssetFileIndex index)
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

// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Story.Graph;
using PG.StarWarsGame.LSP.Story.Model;
using PG.StarWarsGame.LSP.Story.Sim;

namespace PG.StarWarsGame.LSP.Story.Tests.Sim;

/// <summary>
///     A simulator runs ONE scope: the galactic story, or one battle. The game freezes the galaxy
///     while a battle plays and the battle's plot files only ever run there, so a galactic session
///     must not arm a battle's listeners at galactic tick 0 - which the integrated graph did, 543
///     standing decisions on Underworld.
/// </summary>
public sealed class StorySimulatorScopeTest
{
    private const string Galaxy = "file:///ws/data/xml/story_campaign.xml";
    private const string Battle = "file:///ws/data/xml/story_m2_land.xml";
    private const string M2 = "Story_Plots_M2_Land.xml";
    private const string M2Key = "story_plots_m2_land.xml";

    private const string GalaxyText =
        "<Story>" +
        "<Event Name=\"Begin\"><Event_Type>STORY_ELAPSED</Event_Type><Event_Param1>0</Event_Param1></Event>" +
        "<Event Name=\"E\"><Event_Type>STORY_TRIGGER</Event_Type><Prereq>Begin</Prereq>" +
        "<Reward_Type>LINK_TACTICAL</Reward_Type><Reward_Param1>" + M2 + "</Reward_Param1></Event>" +
        "<Event Name=\"Win\"><Event_Type>STORY_VICTORY</Event_Type><Prereq>E</Prereq></Event>" +
        "<Event Name=\"Reader\"><Event_Type>STORY_FLAG</Event_Type><Event_Param1>F</Event_Param1></Event>" +
        "</Story>";

    private const string BattleText =
        "<Story>" +
        "<Event Name=\"Ambush\"><Event_Type>STORY_GENERIC</Event_Type></Event>" +
        "<Event Name=\"Reinforcements\"><Event_Type>STORY_TRIGGER</Event_Type><Prereq>Ambush</Prereq>" +
        "<Reward_Type>SET_FLAG</Reward_Type><Reward_Param1>F</Reward_Param1></Event>" +
        "<Event Name=\"BattleWin\"><Event_Type>STORY_VICTORY</Event_Type><Prereq>Ambush</Prereq></Event>" +
        "</Story>";

    private static readonly ISchemaProvider Schema = new ScopeSchemaProvider();

    private static StoryCampaignModel Model()
    {
        var threads = new List<StoryThread>
        {
            StoryThreadParser.Parse(GalaxyText, Galaxy),
            StoryThreadParser.Parse(BattleText, Battle)
        };
        var manifests = new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [M2] = new HashSet<string> { Battle }
        };
        var graph = new StoryGraphBuilder(Schema).Build(threads, manifests);
        return new StoryCampaignModel("GC", "Empire", threads, new HashSet<string>(StringComparer.Ordinal), graph)
        {
            TacticalManifestThreads = manifests,
            Battles = StoryGraphScoper.Battles(graph, manifests)
        };
    }

    private static string Id(string uri, string name)
    {
        return StoryGraphBuilder.EventNodeId(uri, name);
    }

    [Fact]
    public void GalacticScope_RunsTheGalacticEventsOnly_AndABattleScopeItsOwn()
    {
        var model = Model();

        var galactic = new StorySimulator(model, Schema);
        var battle = new StorySimulator(model, Schema, null, M2Key);

        var galacticIds = galactic.GetLifecycles(galactic.Start()).Keys.Order(StringComparer.Ordinal).ToList();
        var battleIds = battle.GetLifecycles(battle.Start()).Keys.Order(StringComparer.Ordinal).ToList();
        Assert.Equal(
            new[] { Id(Galaxy, "Begin"), Id(Galaxy, "E"), Id(Galaxy, "Reader"), Id(Galaxy, "Win") }.Order(StringComparer
                .Ordinal),
            galacticIds);
        Assert.Equal(
            new[] { Id(Battle, "Ambush"), Id(Battle, "BattleWin"), Id(Battle, "Reinforcements") }.Order(StringComparer
                .Ordinal),
            battleIds);
        Assert.Null(galactic.Scope);
        Assert.Equal(M2Key, battle.Scope);
    }

    [Fact]
    public void Interventions_NameTheBattle_AnOutcomeListenerWaitsOn()
    {
        var model = Model();

        // Galactic: Win waits behind E, which links M2 in - so it is M2's outcome listener.
        var galactic = new StorySimulator(model, Schema);
        var win = Assert.Single(galactic.GetInterventions(galactic.Start()), i => i.EventName == "Win");
        Assert.Equal("tactical", win.Kind);
        Assert.Equal(M2Key, win.BattleKey);

        // Inside the battle every outcome listener is the battle's own.
        var battle = new StorySimulator(model, Schema, null, M2Key);
        var snapshot = battle.SatisfyTrigger(battle.Start(), Id(Battle, "Ambush"));
        var battleWin = Assert.Single(battle.GetInterventions(snapshot), i => i.EventName == "BattleWin");
        Assert.Equal(M2Key, battleWin.BattleKey);
    }

    [Fact]
    public void Start_SeedsFlagsAndWorld_BeforeTheFirstPoll()
    {
        var model = Model();
        var galactic = new StorySimulator(model, Schema);
        var world = StoryWorld.Empty.WithPlanetOwner("Kuat", "Empire");

        // STORY_FLAG's default compare is "equal to 0", and an unset flag never fires: the reader
        // fires at tick 0 only if the seed was in place when the first poll ran.
        var seeded = galactic.Start(ImmutableDictionary<string, int>.Empty.Add("F", 0), world);
        var bare = galactic.Start();

        Assert.Equal(StoryEventLifecycle.Fired, galactic.GetLifecycles(seeded)[Id(Galaxy, "Reader")]);
        Assert.Equal(StoryEventLifecycle.Armed, galactic.GetLifecycles(bare)[Id(Galaxy, "Reader")]);
        Assert.Equal("Empire", seeded.Runtime.World.Planets["Kuat"].Owner);
    }
}

file sealed class ScopeSchemaProvider : ISchemaProvider
{
    private static readonly EnumDefinition Events = new()
    {
        Name = "StoryEventType",
        Values =
        [
            new EnumValueDefinition { Name = "STORY_TRIGGER" },
            new EnumValueDefinition { Name = "STORY_ELAPSED" },
            new EnumValueDefinition { Name = "STORY_GENERIC" },
            new EnumValueDefinition { Name = "STORY_VICTORY" },
            new EnumValueDefinition
            {
                Name = "STORY_FLAG",
                Params =
                [
                    new ParamDefinition
                        { Position = 0, ValueType = XmlValueType.NameReference, ReferenceTypeName = "StoryFlag" }
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
                Name = "LINK_TACTICAL",
                Params =
                [
                    new ParamDefinition
                        { Position = 0, ValueType = XmlValueType.NameReference, ReferenceTypeName = "StoryPlotFile" }
                ]
            },
            new EnumValueDefinition
            {
                Name = "SET_FLAG",
                Params =
                [
                    new ParamDefinition
                        { Position = 0, ValueType = XmlValueType.NameReference, ReferenceTypeName = "StoryFlag" }
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
        return name switch
        {
            "StoryEventType" => Events,
            "StoryRewardType" => Rewards,
            _ => null
        };
    }

    public GameObjectTypeDefinition? GetObjectType(string t)
    {
        return null;
    }
}
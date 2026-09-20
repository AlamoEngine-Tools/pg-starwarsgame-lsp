// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Story.Graph;
using PG.StarWarsGame.LSP.Story.Model;

namespace PG.StarWarsGame.LSP.Story.Tests.Graph;

/// <summary>
///     The links the engine makes between a reward and the listeners it will reach, drawn so a
///     sequence the game plays in order reads in order. Never a prerequisite: the simulator
///     already follows these through its world changes, the graph only shows them. Each rule is
///     one the simulator dispatches today - a speech's completion, a battle's outcome, the summary
///     dialog closing.
/// </summary>
public sealed class StoryGraphImplicitEdgeTest
{
    private const string Galaxy = "file:///ws/data/xml/Story_Campaign.xml";
    private const string Battle = "file:///ws/data/xml/story_m2_land.xml";
    private const string M2 = "Story_Plots_M2_Land.xml";
    private const string M2Key = "story_plots_m2_land.xml";

    private const string GalaxyText =
        "<Story>" +
        "<Event Name=\"Begin\"><Event_Type>STORY_ELAPSED</Event_Type><Event_Param1>0</Event_Param1></Event>" +
        "<Event Name=\"Welcome\"><Event_Type>STORY_TRIGGER</Event_Type><Prereq>Begin</Prereq>" +
        "<Reward_Type>MULTIMEDIA</Reward_Type><Reward_Param1>TEXT_WELCOME</Reward_Param1>" +
        "<Reward_Param8>Speech_Welcome</Reward_Param8></Event>" +
        "<Event Name=\"WelcomeDone\"><Event_Type>STORY_SPEECH_DONE</Event_Type>" +
        "<Event_Param1>SPEECH_WELCOME</Event_Param1><Prereq>Welcome</Prereq></Event>" +
        "<Event Name=\"Briefing\"><Event_Type>STORY_TRIGGER</Event_Type><Prereq>Begin</Prereq>" +
        "<Reward_Type>SPEECH</Reward_Type><Reward_Param1>Speech_Briefing</Reward_Param1></Event>" +
        "<Event Name=\"BriefingDone\"><Event_Type>STORY_SPEECH_DONE</Event_Type>" +
        "<Event_Param1>Speech_Other, Speech_Briefing</Event_Param1></Event>" +
        "<Event Name=\"Intro\"><Event_Type>STORY_TRIGGER</Event_Type><Prereq>Begin</Prereq>" +
        "<Reward_Type>START_MOVIE</Reward_Type><Reward_Param1>Intro_Movie</Reward_Param1></Event>" +
        "<Event Name=\"IntroDone\"><Event_Type>STORY_MOVIE_DONE</Event_Type><Event_Param1>INTRO_MOVIE</Event_Param1></Event>" +
        "<Event Name=\"OtherDone\"><Event_Type>STORY_SPEECH_DONE</Event_Type><Event_Param1>Speech_Nobody</Event_Param1></Event>" +
        "<Event Name=\"E\"><Event_Type>STORY_TRIGGER</Event_Type><Prereq>Begin</Prereq>" +
        "<Reward_Type>LINK_TACTICAL</Reward_Type><Reward_Param1>" + M2 + "</Reward_Param1></Event>" +
        "<Event Name=\"Returned\"><Event_Type>STORY_GENERIC</Event_Type><Event_Param1>battle_end_closed</Event_Param1>" +
        "<Prereq>E</Prereq></Event>" +
        "<Event Name=\"Win\"><Event_Type>STORY_VICTORY</Event_Type><Prereq>E</Prereq></Event>" +
        // Behind the summary listener, as the tutorial writes its failure branch.
        "<Event Name=\"Lost\"><Event_Type>STORY_MISSION_LOST</Event_Type><Prereq>Returned</Prereq></Event>" +
        "<Event Name=\"Later\"><Event_Type>STORY_TRIGGER</Event_Type><Prereq>Returned</Prereq></Event>" +
        "</Story>";

    private const string BattleText =
        "<Story><Event Name=\"Ambush\"><Event_Type>STORY_GENERIC</Event_Type></Event></Story>";

    private static readonly ISchemaProvider Schema = new ImplicitSchemaProvider();

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

    private static string Id(string name)
    {
        return StoryGraphBuilder.EventNodeId(Galaxy, name);
    }

    private static List<(string From, string To, string? Label)> Implicit(StoryGraph graph)
    {
        return graph.Edges.Where(e => e.Kind == StoryEdgeKind.Implicit)
            .Select(e => (e.FromId, e.ToId, e.Label)).ToList();
    }

    [Fact]
    public void MediaStarters_LinkToTheListenersThatWaitOnThem_ByName_CaseAndListInsensitive()
    {
        var edges = Implicit(Model().Graph);

        Assert.Contains((Id("Welcome"), Id("WelcomeDone"), "speech"), edges);
        Assert.Contains((Id("Briefing"), Id("BriefingDone"), "speech"), edges);
        Assert.Contains((Id("Intro"), Id("IntroDone"), "movie"), edges);
        // Nothing starts Speech_Nobody, so nothing leads to its listener.
        Assert.DoesNotContain(edges, e => e.To == Id("OtherDone"));
    }

    [Fact]
    public void GalacticScope_LinksTheBattleStub_ToItsOutcomeAndSummaryListeners()
    {
        var galactic = StoryGraphScoper.Scope(Model(), null);
        var stub = StoryGraphScoper.TacticalNodeId(M2Key);

        var edges = Implicit(galactic);

        Assert.Contains((stub, Id("Returned"), "summary closed"), edges);
        Assert.Contains((stub, Id("Win"), "outcome"), edges);
        // The loss listener sits behind the summary listener, as the tutorial writes it: still the
        // battle's outcome, still reached.
        Assert.Contains((stub, Id("Lost"), "outcome"), edges);
        // An ordinary event behind the summary is the story going on, not an outcome.
        Assert.DoesNotContain(edges, e => e.To == Id("Later"));
    }

    [Fact]
    public void ImplicitEdges_AreNotPrerequisites_ForLifecycleOrReachability()
    {
        var model = Model();
        var evaluator = new StoryEvaluator(model.Graph);

        // Returned waits on E through its prerequisite alone; the implicit edge from the stub adds
        // no second gate and no second way to arm it.
        Assert.Equal(StoryEventLifecycle.Waiting, evaluator.GetLifecycle(Id("Returned"), StoryRuntimeState.Initial));
        var reachable = evaluator.ComputeReachableEvents();
        Assert.Contains(Id("Returned"), reachable);
        Assert.Contains(Id("OtherDone"), reachable);
    }
}

file sealed class ImplicitSchemaProvider : ISchemaProvider
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
            new EnumValueDefinition { Name = "STORY_MISSION_LOST" },
            new EnumValueDefinition
            {
                Name = "STORY_SPEECH_DONE",
                Params = [new ParamDefinition { Position = 0, ValueType = XmlValueType.NameReference, ReferenceTypeName = "SpeechEvent" }]
            },
            new EnumValueDefinition
            {
                Name = "STORY_MOVIE_DONE",
                Params = [new ParamDefinition { Position = 0, ValueType = XmlValueType.NameReference, ReferenceTypeName = "Movie" }]
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
                Params = [new ParamDefinition { Position = 0, ValueType = XmlValueType.NameReference, ReferenceTypeName = "StoryPlotFile" }]
            },
            new EnumValueDefinition
            {
                Name = "MULTIMEDIA",
                Params =
                [
                    new ParamDefinition { Position = 0, ValueType = XmlValueType.NameReference },
                    new ParamDefinition { Position = 7, ValueType = XmlValueType.NameReference, ReferenceTypeName = "SpeechEvent" }
                ]
            },
            new EnumValueDefinition
            {
                Name = "SPEECH",
                Params = [new ParamDefinition { Position = 0, ValueType = XmlValueType.NameReference, ReferenceTypeName = "SpeechEvent" }]
            },
            new EnumValueDefinition
            {
                Name = "START_MOVIE",
                Params = [new ParamDefinition { Position = 0, ValueType = XmlValueType.NameReference, ReferenceTypeName = "Movie" }]
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

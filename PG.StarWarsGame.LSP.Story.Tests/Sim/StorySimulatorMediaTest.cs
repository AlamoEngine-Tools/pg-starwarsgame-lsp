// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Story.Discovery;
using PG.StarWarsGame.LSP.Story.Graph;
using PG.StarWarsGame.LSP.Story.Model;
using PG.StarWarsGame.LSP.Story.Sim;

namespace PG.StarWarsGame.LSP.Story.Tests.Sim;

/// <summary>
///     What the story waits on when media plays, and what a battle's end does to the galaxy.
///     Measured on the shipped corpus: 1485 of 1522 STORY_SPEECH_DONE listeners name a speech a
///     MULTIMEDIA reward starts (parameter 8), 35 one a SPEECH reward starts, 2 none. The engine
///     gives a speech-done listener a 60 s timeout at parse when the XML carries none, and the
///     battle-end dialog fires every active speech-done listener.
/// </summary>
public sealed class StorySimulatorMediaTest
{
    private const string Galaxy = "file:///ws/data/xml/Story_Campaign.xml";
    private const string Battle = "file:///ws/data/xml/story_m2_land.xml";
    private const string M2 = "Story_Plots_M2_Land.xml";
    private const string M2Key = "story_plots_m2_land.xml";

    private const string GalaxyText =
        "<Story>" +
        "<Event Name=\"Begin\"><Event_Type>STORY_ELAPSED</Event_Type><Event_Param1>0</Event_Param1></Event>" +
        // MULTIMEDIA starts the speech in parameter 8; the listener names it in parameter 1.
        "<Event Name=\"Welcome\"><Event_Type>STORY_TRIGGER</Event_Type><Prereq>Begin</Prereq>" +
        "<Reward_Type>MULTIMEDIA</Reward_Type><Reward_Param1>TEXT_WELCOME</Reward_Param1>" +
        "<Reward_Param8>SPEECH_WELCOME</Reward_Param8></Event>" +
        "<Event Name=\"WelcomeDone\"><Event_Type>STORY_SPEECH_DONE</Event_Type><Event_Param1>speech_welcome</Event_Param1>" +
        "<Prereq>Welcome</Prereq></Event>" +
        // A listener nothing starts: only the engine's timeout ends its wait.
        "<Event Name=\"Orphan\"><Event_Type>STORY_SPEECH_DONE</Event_Type><Event_Param1>NEVER_SPOKEN</Event_Param1></Event>" +
        "<Event Name=\"E\"><Event_Type>STORY_TRIGGER</Event_Type><Prereq>Begin</Prereq>" +
        "<Reward_Type>LINK_TACTICAL</Reward_Type><Reward_Param1>" + M2 + "</Reward_Param1></Event>" +
        "<Event Name=\"Win\"><Event_Type>STORY_VICTORY</Event_Type><Prereq>E</Prereq></Event>" +
        "<Event Name=\"Lost\"><Event_Type>STORY_MISSION_LOST</Event_Type><Prereq>E</Prereq></Event>" +
        "<Event Name=\"Returned\"><Event_Type>STORY_GENERIC</Event_Type><Event_Param1>battle_end_closed</Event_Param1>" +
        "<Prereq>E</Prereq></Event>" +
        "</Story>";

    private const string BattleText =
        "<Story><Event Name=\"Ambush\"><Event_Type>STORY_GENERIC</Event_Type></Event></Story>";

    private static readonly ISchemaProvider Schema = new MediaSchemaProvider();

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

    private static StoryEventLifecycle Lifecycle(StorySimulator sim, StorySimSnapshot s, string name)
    {
        return sim.GetLifecycles(s)[Id(name)];
    }

    [Fact]
    public void Multimedia_OwesTheSpeechDone_WhichFiresOnTheNextTick()
    {
        var sim = new StorySimulator(Model(), Schema);

        var start = sim.Start();
        Assert.Equal(StoryEventLifecycle.Fired, Lifecycle(sim, start, "Welcome"));
        Assert.Equal(StoryEventLifecycle.Armed, Lifecycle(sim, start, "WelcomeDone"));
        // Owed, so not a decision.
        Assert.DoesNotContain(sim.GetInterventions(start), i => i.EventName == "WelcomeDone");

        var next = sim.Tick(start);

        Assert.Equal(StoryEventLifecycle.Fired, Lifecycle(sim, next, "WelcomeDone"));
        Assert.Contains(next.Steps, s => s.NodeId == Id("WelcomeDone") && s.Cause == StorySimCause.Speech);
    }

    [Fact]
    public void MediaNotAssumedToComplete_LeavesTheSpeechDoneToTheAuthor_UntilTheTimeout()
    {
        var sim = new StorySimulator(Model(), Schema, options: new StorySimOptions(AssumeMediaCompletes: false));

        var start = sim.Start();
        Assert.Contains(sim.GetInterventions(start), i => i.EventName == "WelcomeDone");
        var later = sim.Tick(start, 59);
        Assert.Equal(StoryEventLifecycle.Armed, Lifecycle(sim, later, "WelcomeDone"));

        var timedOut = sim.Tick(later);

        Assert.Equal(StoryEventLifecycle.Fired, Lifecycle(sim, timedOut, "WelcomeDone"));
    }

    [Fact]
    public void SpeechDoneNothingStarts_IsADecisionWithATimeoutGate_AndFiresAtSixtySeconds()
    {
        var sim = new StorySimulator(Model(), Schema);

        var start = sim.Start();
        var orphan = Assert.Single(sim.GetInterventions(start), i => i.EventName == "Orphan");
        Assert.Equal("manual", orphan.Kind);
        var gate = sim.GetGate(start, Id("Orphan"));
        Assert.NotNull(gate);
        Assert.Equal("0/60 s", gate.Label);

        var timedOut = sim.Tick(start, 60);

        Assert.Equal(StoryEventLifecycle.Fired, Lifecycle(sim, timedOut, "Orphan"));
        Assert.Contains(timedOut.Steps, s => s.NodeId == Id("Orphan") && s.Cause == StorySimCause.Timeout);
    }

    [Fact]
    public void BattleOutcome_MakesTheOtherOutcomesListenerMoot_AndRaisesBattleEndClosed_AndEndsEverySpeech()
    {
        var sim = new StorySimulator(Model(), Schema);
        var start = sim.Start();
        Assert.Contains(sim.GetInterventions(start), i => i.EventName == "Lost" && i.BattleKey == M2Key);

        var resolved = sim.ResolveBattleOutcome(start,
            new StoryWorldChange(StoryWorldChangeKind.BattleWon) { BattleKey = M2Key });

        Assert.Equal("won", resolved.Runtime.World.BattleOutcomes[M2Key]);
        Assert.Equal(StoryEventLifecycle.Fired, Lifecycle(sim, resolved, "Win"));
        // The game never takes the losing route once the battle is won: not a decision any more.
        Assert.DoesNotContain(sim.GetInterventions(resolved), i => i.EventName == "Lost");
        // Closing the summary raises the generic the galaxy listens for...
        Assert.Equal(StoryEventLifecycle.Fired, Lifecycle(sim, resolved, "Returned"));
        // ...and ends every speech still playing (Trigger_All_Speech_Done_Events).
        Assert.Equal(StoryEventLifecycle.Fired, Lifecycle(sim, resolved, "Orphan"));
    }

    [Fact]
    public void FlagWritesOf_ListsWhatABattleCanWrite()
    {
        const string writer =
            "<Story><Event Name=\"Set\"><Event_Type>STORY_GENERIC</Event_Type>" +
            "<Reward_Type>SET_FLAG</Reward_Type><Reward_Param1>M2_Done</Reward_Param1><Reward_Param2>3</Reward_Param2></Event>" +
            "<Event Name=\"Bump\"><Event_Type>STORY_GENERIC</Event_Type>" +
            "<Reward_Type>INCREMENT_FLAG</Reward_Type><Reward_Param1>M2_Score</Reward_Param1></Event></Story>";
        var threads = new List<StoryThread>
        {
            StoryThreadParser.Parse(GalaxyText, Galaxy), StoryThreadParser.Parse(writer, Battle)
        };
        var manifests = new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase)
            { [M2] = new HashSet<string> { Battle } };
        var graph = new StoryGraphBuilder(Schema).Build(threads, manifests);
        var model = new StoryCampaignModel("GC", "Empire", threads, new HashSet<string>(StringComparer.Ordinal), graph)
            { TacticalManifestThreads = manifests };

        var writes = StorySimulator.FlagWritesOf(model, Schema, M2Key);

        Assert.Equal([("M2_Done", 3), ("M2_Score", 1)], writes.Select(w => (w.Flag, w.Value)));
    }
}

file sealed class MediaSchemaProvider : ISchemaProvider
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
                Params =
                [
                    new ParamDefinition
                        { Position = 0, ValueType = XmlValueType.NameReference, ReferenceTypeName = "SpeechEvent" }
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
                Name = "MULTIMEDIA",
                Params =
                [
                    new ParamDefinition { Position = 0, ValueType = XmlValueType.NameReference },
                    new ParamDefinition
                        { Position = 7, ValueType = XmlValueType.NameReference, ReferenceTypeName = "SpeechEvent" }
                ]
            },
            new EnumValueDefinition
            {
                Name = "SET_FLAG",
                Params =
                [
                    new ParamDefinition
                    {
                        Position = 0, ValueType = XmlValueType.NameReference,
                        ReferenceTypeName = StoryReferenceTypes.Flag
                    }
                ]
            },
            new EnumValueDefinition
            {
                Name = "INCREMENT_FLAG",
                Params =
                [
                    new ParamDefinition
                    {
                        Position = 0, ValueType = XmlValueType.NameReference,
                        ReferenceTypeName = StoryReferenceTypes.Flag
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

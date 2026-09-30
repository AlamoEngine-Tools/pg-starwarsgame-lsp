// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Story.Discovery;
using PG.StarWarsGame.LSP.Story.Graph;
using PG.StarWarsGame.LSP.Story.Model;
using PG.StarWarsGame.LSP.Story.Sim;

namespace PG.StarWarsGame.LSP.Story.Tests.Sim;

/// <summary>
///     What LINK_TACTICAL does to the galaxy, measured in the engine: it queues the battle and,
///     from the galactic mode, brings up the pending-battle choice at once, pausing gameplay. A
///     paused galaxy is still serviced every frame with the paused flag set - prerequisite pushes,
///     polled flags, scripts and GUI events all run; only STORY_ELAPSED stops accumulating. The
///     left choice button fights, the right one auto-resolves; FORCE_CLICK_GUI presses either
///     without raising the click event, a real click raises it. A second link while one is
///     pending is refused by the engine and queues nothing. Once the battle runs, the galactic
///     story is not serviced at all until it ends.
/// </summary>
public sealed class StorySimulatorPendingBattleTest
{
    private const string Galaxy = "file:///ws/data/xml/Story_Campaign.xml";
    private const string Battle = "file:///ws/data/xml/story_m2_land.xml";
    private const string Battle2 = "file:///ws/data/xml/story_m5_space.xml";
    private const string M2 = "Story_Plots_M2_Land.xml";
    private const string M5 = "Story_Plots_M5_Space.xml";
    private const string M2Key = "story_plots_m2_land.xml";

    private const string Head =
        "<Story>" +
        "<Event Name=\"Begin\"><Event_Type>STORY_ELAPSED</Event_Type><Event_Param1>0</Event_Param1></Event>" +
        // Armed from the first frame; three seconds of GAMEPLAY time, not of ticks.
        "<Event Name=\"Timer\"><Event_Type>STORY_ELAPSED</Event_Type><Event_Param1>3</Event_Param1><Prereq>Begin</Prereq></Event>" +
        "<Event Name=\"Link\"><Event_Type>STORY_TRIGGER</Event_Type><Prereq>Begin</Prereq>" +
        "<Reward_Type>LINK_TACTICAL</Reward_Type><Reward_Param1>" + M2 + "</Reward_Param1>{LINK_EXTRA}" +
        "<Branch>BR</Branch></Event>";

    private const string Tail =
        "<Event Name=\"Returned\"><Event_Type>STORY_GENERIC</Event_Type><Event_Param1>battle_end_closed</Event_Param1>" +
        "<Prereq>Link</Prereq></Event>" +
        "<Event Name=\"Win\"><Event_Type>STORY_VICTORY</Event_Type><Prereq>Link</Prereq></Event>" +
        // Behind the summary listener, as the tutorial writes its failure branch.
        "<Event Name=\"Lost\"><Event_Type>STORY_MISSION_LOST</Event_Type><Prereq>Returned</Prereq></Event>" +
        "<Event Name=\"Reader\"><Event_Type>STORY_FLAG</Event_Type><Event_Param1>F</Event_Param1><Event_Param2>1</Event_Param2></Event>" +
        "<Event Name=\"Clicked\"><Event_Type>STORY_CLICK_GUI</Event_Type><Event_Param1>choice_button_left</Event_Param1></Event>" +
        // The tutorial's way back: reset the branch, which re-arms and re-fires the link.
        "<Event Name=\"Retry\"><Event_Type>STORY_GENERIC</Event_Type><Event_Param1>retry</Event_Param1>" +
        "<Reward_Type>RESET_BRANCH</Reward_Type><Reward_Param1>BR</Reward_Param1></Event>" +
        // A generic the game does raise: a decision for the author.
        "<Event Name=\"Zoomed\"><Event_Type>STORY_GENERIC</Event_Type><Event_Param1>zoomed_in</Event_Param1></Event>" +
        "</Story>";

    private const string BattleText =
        "<Story><Event Name=\"Ambush\"><Event_Type>STORY_GENERIC</Event_Type></Event></Story>";

    private const string Battle2Text =
        "<Story><Event Name=\"Space\"><Event_Type>STORY_GENERIC</Event_Type></Event></Story>";

    private static readonly ISchemaProvider Schema = new PendingSchemaProvider();

    private static string GalaxyText(string linkExtra = "", string between = "")
    {
        return Head.Replace("{LINK_EXTRA}", linkExtra) + between + Tail;
    }

    private static StoryCampaignModel Model(string galaxyText)
    {
        var threads = new List<StoryThread>
        {
            StoryThreadParser.Parse(galaxyText, Galaxy),
            StoryThreadParser.Parse(BattleText, Battle),
            StoryThreadParser.Parse(Battle2Text, Battle2)
        };
        var manifests = new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [M2] = new HashSet<string> { Battle },
            [M5] = new HashSet<string> { Battle2 }
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

    private static StoryWorldChange Click(string button)
    {
        return new StoryWorldChange(StoryWorldChangeKind.ClickGui) { Name = button };
    }

    [Fact]
    public void LinkTactical_MakesItsBattlePending_AndHoldsGameplayTime()
    {
        var sim = new StorySimulator(Model(GalaxyText()), Schema);

        var start = sim.Start();

        Assert.Equal(M2Key, start.Runtime.World.PendingBattle);
        Assert.Null(start.Runtime.World.PendingBattleChoice);
        Assert.Equal(StoryEventLifecycle.Armed, Lifecycle(sim, start, "Timer"));
        // The summary's generic is the battle's to raise, not the author's while the battle waits.
        Assert.DoesNotContain(sim.GetInterventions(start), i => i.EventName == "Returned");
        // The battle itself is the decision: fight or auto-resolve, on its portal.
        var choice = Assert.Single(sim.GetInterventions(start), i => i.Kind == StorySimIntervention.BattleKind);
        Assert.Equal(M2Key, choice.BattleKey);
        Assert.Equal(StoryGraphScoper.TacticalNodeId(M2Key), choice.NodeId);
        Assert.Equal(["fight", "autoResolve"], choice.Options);
        // No timer is the clock's to end while gameplay stands still.
        Assert.Equal(0, sim.GetClockPending(start));

        // Five ticks pass; gameplay time does not, so the three-second timer never comes due.
        var later = sim.Tick(start, 5);

        Assert.Equal(StoryEventLifecycle.Armed, Lifecycle(sim, later, "Timer"));
        Assert.Equal(5, later.Tick);
        Assert.Equal(0, later.GameplayClock);
    }

    [Fact]
    public void WhilePending_TheStoryIsStillServiced_FlagPollsFire()
    {
        var sim = new StorySimulator(Model(GalaxyText()), Schema);
        var start = sim.Start();

        var polled = sim.Tick(sim.SetFlag(start, "F", 1));

        Assert.Equal(StoryEventLifecycle.Fired, Lifecycle(sim, polled, "Reader"));
        Assert.Equal(M2Key, polled.Runtime.World.PendingBattle);
    }

    [Fact]
    public void ForcedClickOnTheFightButton_TakesTheChoice_WithoutRaisingTheClickEvent()
    {
        var auto =
            "<Event Name=\"AutoBegin\"><Event_Type>STORY_TRIGGER</Event_Type><Prereq>Link</Prereq>" +
            "<Reward_Type>FORCE_CLICK_GUI</Reward_Type><Reward_Param1>choice_button_left</Reward_Param1></Event>";
        var sim = new StorySimulator(Model(GalaxyText(between: auto)), Schema);

        var start = sim.Start();

        Assert.Equal(M2Key, start.Runtime.World.PendingBattle);
        Assert.Equal("fight", start.Runtime.World.PendingBattleChoice);
        // Measured: the reward presses the component directly; the story's click event is not raised.
        Assert.Equal(StoryEventLifecycle.Armed, Lifecycle(sim, start, "Clicked"));
        // The battle runs from here; the author still chooses to play it or to skip it with an outcome.
        var starting = Assert.Single(sim.GetInterventions(start), i => i.Kind == StorySimIntervention.BattleKind);
        Assert.Equal(["enter", "won", "lost"], starting.Options);
    }

    [Fact]
    public void ForcedClickOnTheRightButton_ChoosesAutoResolve()
    {
        var auto =
            "<Event Name=\"AutoBegin\"><Event_Type>STORY_TRIGGER</Event_Type><Prereq>Link</Prereq>" +
            "<Reward_Type>FORCE_CLICK_GUI</Reward_Type><Reward_Param1>CHOICE_BUTTON_RIGHT</Reward_Param1></Event>";
        var sim = new StorySimulator(Model(GalaxyText(between: auto)), Schema);

        var start = sim.Start();

        Assert.Equal("autoResolve", start.Runtime.World.PendingBattleChoice);
        // Auto-resolve leaves the outcome to decide; the galaxy is serviced meanwhile.
        var outcome = Assert.Single(sim.GetInterventions(start), i => i.Kind == StorySimIntervention.BattleKind);
        Assert.Equal(["won", "lost"], outcome.Options);
    }

    [Fact]
    public void TheAuthorsClick_OnTheFightButton_TakesTheChoice_AndRaisesTheClickEvent()
    {
        var sim = new StorySimulator(Model(GalaxyText()), Schema);
        var start = sim.Start();

        var chosen = sim.ApplyWorldChange(start, Click("choice_button_left"));

        Assert.Equal("fight", chosen.Runtime.World.PendingBattleChoice);
        Assert.Equal(StoryEventLifecycle.Fired, Lifecycle(sim, chosen, "Clicked"));
    }

    [Fact]
    public void PendingPanelOff_StartsTheBattleAtOnce()
    {
        var sim = new StorySimulator(Model(GalaxyText("<Reward_Param13>0</Reward_Param13>")), Schema);

        var start = sim.Start();

        Assert.Equal(M2Key, start.Runtime.World.PendingBattle);
        Assert.Equal("fight", start.Runtime.World.PendingBattleChoice);
    }

    [Fact]
    public void ResolveBattleOutcome_ClearsThePendingBattle_AndGameplayTimeRunsAgain()
    {
        var sim = new StorySimulator(Model(GalaxyText()), Schema);
        var start = sim.Tick(sim.Start(), 5);

        var resolved = sim.ResolveBattleOutcome(start,
            new StoryWorldChange(StoryWorldChangeKind.BattleWon) { BattleKey = M2Key });

        Assert.Null(resolved.Runtime.World.PendingBattle);
        Assert.Null(resolved.Runtime.World.PendingBattleChoice);
        Assert.Equal(StoryEventLifecycle.Fired, Lifecycle(sim, resolved, "Returned"));
        // Three seconds of gameplay from arming: the ticks spent pending did not count.
        Assert.Equal(StoryEventLifecycle.Armed, Lifecycle(sim, sim.Tick(resolved, 2), "Timer"));
        Assert.Equal(StoryEventLifecycle.Fired, Lifecycle(sim, sim.Tick(resolved, 3), "Timer"));
    }

    [Fact]
    public void TheLossListenerBehindTheSummary_BelongsToTheBattle_AndGoesMootOnceItIsWon()
    {
        var sim = new StorySimulator(Model(GalaxyText()), Schema);
        var start = sim.Start();
        Assert.Equal(M2Key, sim.BattleOf(Id("Lost")));

        var resolved = sim.ResolveBattleOutcome(start,
            new StoryWorldChange(StoryWorldChangeKind.BattleWon) { BattleKey = M2Key });

        // Returned fired on the summary closing, which armed Lost; the game never raises the
        // loss for a battle it recorded as won, so Lost is no decision.
        Assert.Equal(StoryEventLifecycle.Armed, Lifecycle(sim, resolved, "Lost"));
        Assert.DoesNotContain(sim.GetInterventions(resolved), i => i.EventName == "Lost");
    }

    // Measured: a loss reaches the galactic story as a delayed event, executed at the start of the
    // galaxy's next frame - before the summary closes and arms the listener behind it. The shipped
    // tutorial's loss branch is written that way and never fires; the simulator does the same.
    [Fact]
    public void TheLossListenerBehindTheSummary_MissesTheLoss_AsItDoesInTheGame()
    {
        var sim = new StorySimulator(Model(GalaxyText()), Schema);

        var resolved = sim.ResolveBattleOutcome(sim.Start(),
            new StoryWorldChange(StoryWorldChangeKind.BattleLost) { BattleKey = M2Key });

        Assert.Equal(StoryEventLifecycle.Armed, Lifecycle(sim, resolved, "Lost"));
        Assert.DoesNotContain(sim.GetInterventions(resolved), i => i.EventName == "Lost");
    }

    [Fact]
    public void LinkedAgainAfterAnOutcome_IsANewAttempt()
    {
        var sim = new StorySimulator(Model(GalaxyText()), Schema);
        var lost = sim.ResolveBattleOutcome(sim.Start(),
            new StoryWorldChange(StoryWorldChangeKind.BattleLost) { BattleKey = M2Key });
        Assert.Equal("lost", lost.Runtime.World.BattleOutcomes[M2Key]);

        // The failure branch resets and links the same mission once more.
        var again = sim.ApplyWorldChange(lost, new StoryWorldChange(StoryWorldChangeKind.Generic) { Name = "retry" });

        Assert.Equal(M2Key, again.Runtime.World.PendingBattle);
        Assert.False(again.Runtime.World.BattleOutcomes.ContainsKey(M2Key));
        Assert.Contains(sim.GetInterventions(again), i => i.Kind == StorySimIntervention.BattleKind);
    }

    // Measured: the engine raises a fixed set of generic names; "retry" is not among them, so
    // nothing the player does fires that listener - only a TRIGGER_EVENT push could.
    [Fact]
    public void AGenericTheGameNeverRaises_IsNoDecision_AnEngineRaisedOneIs()
    {
        var sim = new StorySimulator(Model(GalaxyText()), Schema);

        var start = sim.Start();

        Assert.Equal(StoryEventLifecycle.Armed, Lifecycle(sim, start, "Retry"));
        Assert.DoesNotContain(sim.GetInterventions(start), i => i.EventName == "Retry");
        Assert.Contains(sim.GetInterventions(start), i => i.EventName == "Zoomed" && i.Kind == "manual");
    }

    // Measured: the tutorial dialog's Continue button is what raises "Continue_Tutorial"; a
    // TUTORIAL_DIALOG reward puts the dialog up. Inferred: no dialog, no button, so a listener for
    // it is nothing the author can answer until one shows - the tutorial's final listener has no
    // prerequisite and would otherwise wait from tick 0.
    [Fact]
    public void ContinueTutorial_IsADecisionOnlyWhileADialogShows_AndContinuingCloses()
    {
        const string dialog =
            "<Event Name=\"Dialog\"><Event_Type>STORY_TRIGGER</Event_Type><Prereq>Returned</Prereq>" +
            "<Reward_Type>TUTORIAL_DIALOG</Reward_Type><Reward_Param1>TEXT_DONE</Reward_Param1></Event>" +
            "<Event Name=\"Continue\"><Event_Type>STORY_GENERIC</Event_Type><Event_Param1>Continue_Tutorial</Event_Param1></Event>";
        var sim = new StorySimulator(Model(GalaxyText(between: dialog)), Schema);
        var start = sim.Start();

        Assert.Equal(StoryEventLifecycle.Armed, Lifecycle(sim, start, "Continue"));
        Assert.Null(start.Runtime.World.TutorialDialog);
        Assert.DoesNotContain(sim.GetInterventions(start), i => i.EventName == "Continue");

        var shown = sim.ResolveBattleOutcome(start,
            new StoryWorldChange(StoryWorldChangeKind.BattleWon) { BattleKey = M2Key });
        Assert.Equal(StoryEventLifecycle.Fired, Lifecycle(sim, shown, "Dialog"));
        Assert.Equal("TEXT_DONE", shown.Runtime.World.TutorialDialog);
        Assert.Contains(sim.GetInterventions(shown), i => i.EventName == "Continue");

        var continued = sim.ApplyWorldChange(shown,
            new StoryWorldChange(StoryWorldChangeKind.Generic) { Name = "Continue_Tutorial" });
        Assert.Equal(StoryEventLifecycle.Fired, Lifecycle(sim, continued, "Continue"));
        Assert.Null(continued.Runtime.World.TutorialDialog);
    }

    // A flag poll that already holds fires on the next tick with no help: the clock owes it, so
    // the story is not waiting for input while the chain behind a flag write is still landing.
    [Fact]
    public void AFlagListenerWhoseConditionHolds_IsClockWork_NotAWait()
    {
        var sim = new StorySimulator(Model(GalaxyText()), Schema);
        var start = sim.Start();
        var idle = sim.GetClockPending(start);

        var written = sim.SetFlag(start, "F", 1);

        Assert.Equal(idle + 1, sim.GetClockPending(written));
        var polled = sim.Tick(written);
        Assert.Equal(StoryEventLifecycle.Fired, Lifecycle(sim, polled, "Reader"));
        Assert.Equal(idle, sim.GetClockPending(polled));
    }

    // The author's call that a listener will not fire in this run: it stays armed, as in the game,
    // but is no decision until reconsidered - or until something fires it after all.
    [Fact]
    public void RuledOut_IsNoDecision_UntilReconsidered_AndClearsWhenItFires()
    {
        var sim = new StorySimulator(Model(GalaxyText()), Schema);
        var start = sim.Start();
        Assert.Contains(sim.GetInterventions(start), i => i.EventName == "Zoomed");

        var ruledOut = sim.RuleOut(start, Id("Zoomed"), true);

        Assert.Equal(StoryEventLifecycle.Armed, Lifecycle(sim, ruledOut, "Zoomed"));
        Assert.Contains(Id("Zoomed"), ruledOut.Runtime.RuledOut);
        Assert.DoesNotContain(sim.GetInterventions(ruledOut), i => i.EventName == "Zoomed");
        Assert.Contains(ruledOut.Steps, s => s.NodeId == Id("Zoomed") && s.Cause == StorySimCause.RuledOut);

        var reconsidered = sim.RuleOut(ruledOut, Id("Zoomed"), false);
        Assert.Contains(sim.GetInterventions(reconsidered), i => i.EventName == "Zoomed");

        var fired = sim.ApplyWorldChange(sim.RuleOut(reconsidered, Id("Zoomed"), true),
            new StoryWorldChange(StoryWorldChangeKind.Generic) { Name = "zoomed_in" });
        Assert.Equal(StoryEventLifecycle.Fired, Lifecycle(sim, fired, "Zoomed"));
        Assert.DoesNotContain(Id("Zoomed"), fired.Runtime.RuledOut);
    }

    [Fact]
    public void ASecondLink_WhileOneIsPending_QueuesNothing()
    {
        var second =
            "<Event Name=\"Link2\"><Event_Type>STORY_TRIGGER</Event_Type><Prereq>Link</Prereq>" +
            "<Reward_Type>LINK_TACTICAL</Reward_Type><Reward_Param1>" + M5 + "</Reward_Param1></Event>";
        var sim = new StorySimulator(Model(GalaxyText(between: second)), Schema);

        var start = sim.Start();

        Assert.Equal(M2Key, start.Runtime.World.PendingBattle);
        Assert.Contains(start.Steps, s => s.NodeId == Id("Link2") && s.Cause == StorySimCause.Ignored);
    }
}

file sealed class PendingSchemaProvider : ISchemaProvider
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
                Name = "STORY_FLAG",
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
                Name = "STORY_CLICK_GUI",
                Params = [new ParamDefinition { Position = 0, ValueType = XmlValueType.NameReference }]
            },
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
                Name = "FORCE_CLICK_GUI",
                Params = [new ParamDefinition { Position = 0, ValueType = XmlValueType.NameReference }]
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
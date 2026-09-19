// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Story.Discovery;
using PG.StarWarsGame.LSP.Story.Graph;
using PG.StarWarsGame.LSP.Story.Model;
using PG.StarWarsGame.LSP.Story.Sim;

namespace PG.StarWarsGame.LSP.Story.Tests.Sim;

/// <summary>Scenario tests: scripted command sequences asserting lifecycle progressions.</summary>
public sealed class StorySimulatorTest
{
    private const string ThreadAUri = "file:///ws/data/xml/story_a.xml";
    private const string ThreadBUri = "file:///ws/data/xml/story_b.xml";

    private const string ThreadAText =
        "<Story>\n" +
        "\t<Event Name=\"Begin\">\n" +
        "\t\t<Event_Type>STORY_ELAPSED</Event_Type>\n" +
        "\t\t<Event_Param1>0</Event_Param1>\n" +
        "\t\t<Reward_Type>TRIGGER_EVENT</Reward_Type>\n" +
        "\t\t<Reward_Param1>Chained</Reward_Param1>\n" +
        "\t</Event>\n" +
        "\t<Event Name=\"Chained\">\n" +
        "\t\t<Event_Type>STORY_TRIGGER</Event_Type>\n" +
        "\t\t<Prereq>Begin</Prereq>\n" +
        "\t</Event>\n" +
        "\t<Event Name=\"Later\">\n" +
        "\t\t<Event_Type>STORY_ELAPSED</Event_Type>\n" +
        "\t\t<Event_Param1>10</Event_Param1>\n" +
        "\t</Event>\n" +
        "\t<Event Name=\"FlagWatcher\">\n" +
        "\t\t<Event_Type>STORY_FLAG</Event_Type>\n" +
        "\t\t<Event_Param1>FLAG_X</Event_Param1>\n" +
        "\t</Event>\n" +
        "\t<Event Name=\"Setter\">\n" +
        "\t\t<Event_Type>STORY_GENERIC</Event_Type>\n" +
        "\t\t<Reward_Type>SET_FLAG</Reward_Type>\n" +
        "\t\t<Reward_Param1>FLAG_Y</Reward_Param1>\n" +
        "\t</Event>\n" +
        "\t<Event Name=\"Notify\">\n" +
        "\t\t<Event_Type>STORY_AI_NOTIFICATION</Event_Type>\n" +
        "\t\t<Event_Param1>Alert_One</Event_Param1>\n" +
        "\t</Event>\n" +
        "\t<Event Name=\"Victim\">\n" +
        "\t\t<Event_Type>STORY_GENERIC</Event_Type>\n" +
        "\t</Event>\n" +
        "\t<Event Name=\"Disabler\">\n" +
        "\t\t<Event_Type>STORY_GENERIC</Event_Type>\n" +
        "\t\t<Reward_Type>DISABLE_STORY_EVENT</Reward_Type>\n" +
        "\t\t<Reward_Param1>Victim</Reward_Param1>\n" +
        "\t\t<Reward_Param2>1</Reward_Param2>\n" +
        "\t</Event>\n" +
        "\t<Event Name=\"Enabler\">\n" +
        "\t\t<Event_Type>STORY_GENERIC</Event_Type>\n" +
        "\t\t<Reward_Type>DISABLE_STORY_EVENT</Reward_Type>\n" +
        "\t\t<Reward_Param1>Victim</Reward_Param1>\n" +
        "\t\t<Reward_Param2>0</Reward_Param2>\n" +
        "\t</Event>\n" +
        "\t<Event Name=\"Victim2\">\n" +
        "\t\t<Event_Type>STORY_GENERIC</Event_Type>\n" +
        "\t</Event>\n" +
        "\t<Event Name=\"HalfDisabler\">\n" +
        "\t\t<Event_Type>STORY_GENERIC</Event_Type>\n" +
        "\t\t<Reward_Type>DISABLE_STORY_EVENT</Reward_Type>\n" +
        "\t\t<Reward_Param1>Victim2</Reward_Param1>\n" +
        "\t</Event>\n" +
        // Engine fidelity fixtures (chunk 1): a root, its STORY_TRIGGER follower, reset/retrigger
        // controls, a branch, a forced event whose prereq never fires, a speech, and a timer
        // that only starts counting once armed.
        "\t<Event Name=\"Root\">\n" +
        "\t\t<Event_Type>STORY_GENERIC</Event_Type>\n" +
        "\t</Event>\n" +
        "\t<Event Name=\"Follower\">\n" +
        "\t\t<Event_Type>STORY_TRIGGER</Event_Type>\n" +
        "\t\t<Prereq>Root</Prereq>\n" +
        "\t</Event>\n" +
        "\t<Event Name=\"Resetter\">\n" +
        "\t\t<Event_Type>STORY_GENERIC</Event_Type>\n" +
        "\t\t<Reward_Type>RESET_EVENT</Reward_Type>\n" +
        "\t\t<Reward_Param1>Follower</Reward_Param1>\n" +
        "\t</Event>\n" +
        "\t<Event Name=\"Retrigger\">\n" +
        "\t\t<Event_Type>STORY_GENERIC</Event_Type>\n" +
        "\t\t<Reward_Type>TRIGGER_EVENT</Reward_Type>\n" +
        "\t\t<Reward_Param1>Root</Reward_Param1>\n" +
        "\t</Event>\n" +
        "\t<Event Name=\"M1\">\n" +
        "\t\t<Event_Type>STORY_GENERIC</Event_Type>\n" +
        "\t\t<Branch>B</Branch>\n" +
        "\t</Event>\n" +
        "\t<Event Name=\"M2\">\n" +
        "\t\t<Event_Type>STORY_TRIGGER</Event_Type>\n" +
        "\t\t<Prereq>M1</Prereq>\n" +
        "\t\t<Branch>B</Branch>\n" +
        "\t</Event>\n" +
        "\t<Event Name=\"Extra\">\n" +
        "\t\t<Event_Type>STORY_GENERIC</Event_Type>\n" +
        "\t</Event>\n" +
        "\t<Event Name=\"BranchReset\">\n" +
        "\t\t<Event_Type>STORY_GENERIC</Event_Type>\n" +
        "\t\t<Reward_Type>RESET_BRANCH</Reward_Type>\n" +
        "\t\t<Reward_Param1>B</Reward_Param1>\n" +
        "\t\t<Reward_Param2>Extra</Reward_Param2>\n" +
        "\t</Event>\n" +
        "\t<Event Name=\"NeverFires\">\n" +
        "\t\t<Event_Type>STORY_GENERIC</Event_Type>\n" +
        "\t</Event>\n" +
        "\t<Event Name=\"Forced\">\n" +
        "\t\t<Event_Type>STORY_GENERIC</Event_Type>\n" +
        "\t\t<Prereq>NeverFires</Prereq>\n" +
        "\t</Event>\n" +
        "\t<Event Name=\"Forcer\">\n" +
        "\t\t<Event_Type>STORY_GENERIC</Event_Type>\n" +
        "\t\t<Reward_Type>TRIGGER_EVENT</Reward_Type>\n" +
        "\t\t<Reward_Param1>Forced</Reward_Param1>\n" +
        "\t</Event>\n" +
        "\t<Event Name=\"Talker\">\n" +
        "\t\t<Event_Type>STORY_GENERIC</Event_Type>\n" +
        "\t\t<Reward_Type>SPEECH</Reward_Type>\n" +
        "\t\t<Reward_Param1>Line_1</Reward_Param1>\n" +
        "\t</Event>\n" +
        "\t<Event Name=\"AfterTalk\">\n" +
        "\t\t<Event_Type>STORY_SPEECH_DONE</Event_Type>\n" +
        "\t\t<Event_Param1>Line_1</Event_Param1>\n" +
        "\t\t<Prereq>Talker</Prereq>\n" +
        "\t</Event>\n" +
        "\t<Event Name=\"LateTimer\">\n" +
        "\t\t<Event_Type>STORY_ELAPSED</Event_Type>\n" +
        "\t\t<Event_Param1>5</Event_Param1>\n" +
        "\t\t<Prereq>Root</Prereq>\n" +
        "\t</Event>\n" +
        "\t<Event Name=\"Activator\">\n" +
        "\t\t<Event_Type>STORY_GENERIC</Event_Type>\n" +
        "\t\t<Reward_Type>STORY_ELEMENT</Reward_Type>\n" +
        "\t\t<Reward_Param1>story_b</Reward_Param1>\n" +
        "\t</Event>\n" +
        "\t<Event Name=\"PerpFlag\">\n" +
        "\t\t<Event_Type>STORY_FLAG</Event_Type>\n" +
        "\t\t<Event_Param1>FLAG_P</Event_Param1>\n" +
        "\t\t<Perpetual>Yes</Perpetual>\n" +
        "\t</Event>\n" +
        "\t<Event Name=\"EitherWatcher\">\n" +
        "\t\t<Event_Type>STORY_FLAG</Event_Type>\n" +
        "\t\t<Event_Param1>FLAG_A FLAG_B</Event_Param1>\n" +
        "\t</Event>\n" +
        "\t<Event Name=\"CounterWatcher\">\n" +
        "\t\t<Event_Type>STORY_FLAG</Event_Type>\n" +
        "\t\t<Event_Param1>FLAG_C</Event_Param1>\n" +
        "\t\t<Event_Param2>3</Event_Param2>\n" +
        "\t\t<Event_Param3>GREATER_THAN</Event_Param3>\n" +
        "\t</Event>\n" +
        "\t<Event Name=\"Incrementer\">\n" +
        "\t\t<Event_Type>STORY_GENERIC</Event_Type>\n" +
        "\t\t<Reward_Type>INCREMENT_FLAG</Reward_Type>\n" +
        "\t\t<Reward_Param1>FLAG_C</Reward_Param1>\n" +
        "\t\t<Reward_Param2>2</Reward_Param2>\n" +
        "\t\t<Perpetual>Yes</Perpetual>\n" +
        "\t</Event>\n" +
        "</Story>\n";

    private const string ThreadBText =
        "<Story>\n" +
        "\t<Event Name=\"BEvent\">\n" +
        "\t\t<Event_Type>STORY_ELAPSED</Event_Type>\n" +
        "\t\t<Event_Param1>0</Event_Param1>\n" +
        "\t</Event>\n" +
        "</Story>\n";

    private static (StorySimulator Sim, StoryCampaignModel Model) Build()
    {
        var schema = new SimSchemaProvider();
        var threadA = StoryThreadParser.Parse(ThreadAText, ThreadAUri);
        var threadB = StoryThreadParser.Parse(ThreadBText, ThreadBUri);
        var model = new StoryCampaignModel("GC", "Rebel", [threadA, threadB],
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ThreadBUri },
            new StoryGraphBuilder(schema).Build([threadA, threadB]));
        return (new StorySimulator(model, schema), model);
    }

    private static string NodeId(StoryCampaignModel model, string eventName)
    {
        return model.Graph.Nodes.Single(n =>
            n.Kind == StoryNodeKind.Event && n.Event!.Name == eventName).Id;
    }

    private static StoryEventLifecycle LifecycleOf(
        StorySimulator sim, StorySimSnapshot snapshot, StoryCampaignModel model, string eventName)
    {
        return sim.GetLifecycles(snapshot)[NodeId(model, eventName)];
    }

    [Fact]
    public void Start_AutoFiresElapsedZero_AndCascadesControlEdges()
    {
        var (sim, model) = Build();

        var snapshot = sim.Start();

        Assert.Equal(StoryEventLifecycle.Fired, LifecycleOf(sim, snapshot, model, "Begin"));
        Assert.Equal(StoryEventLifecycle.Fired, LifecycleOf(sim, snapshot, model, "Chained"));
        Assert.Equal(StoryEventLifecycle.Armed, LifecycleOf(sim, snapshot, model, "Later"));
        Assert.Equal(StoryEventLifecycle.Inactive, LifecycleOf(sim, snapshot, model, "BEvent"));
    }

    [Fact]
    public void AdvanceClock_FiresElapsedEventsAtTheirTime()
    {
        var (sim, model) = Build();
        var snapshot = sim.Start();

        snapshot = sim.AdvanceClock(snapshot, 9);
        Assert.Equal(StoryEventLifecycle.Armed, LifecycleOf(sim, snapshot, model, "Later"));

        snapshot = sim.AdvanceClock(snapshot, 1);
        Assert.Equal(StoryEventLifecycle.Fired, LifecycleOf(sim, snapshot, model, "Later"));
    }

    [Fact]
    public void StoryFlag_DefaultCompare_IsEqualToZero_AndUnsetNeverFires()
    {
        // Measured: StoryEventFlagClass defaults to EQUAL_TO with value 0, and Get_Flag's unset
        // sentinel never satisfies any comparison - so a bare watcher waits for an explicit 0.
        var (sim, model) = Build();
        var snapshot = sim.Start();
        Assert.Equal(StoryEventLifecycle.Armed, LifecycleOf(sim, snapshot, model, "FlagWatcher"));

        snapshot = sim.Tick(sim.SetFlag(snapshot, "FLAG_X", 1));
        Assert.Equal(StoryEventLifecycle.Armed, LifecycleOf(sim, snapshot, model, "FlagWatcher"));

        snapshot = sim.Tick(sim.SetFlag(snapshot, "FLAG_X", 0));
        Assert.Equal(StoryEventLifecycle.Fired, LifecycleOf(sim, snapshot, model, "FlagWatcher"));
    }

    [Fact]
    public void Tick_AdvancesTheClockByOneSecond_AndCommandsDoNotPoll()
    {
        var (sim, model) = Build();
        var snapshot = sim.Start();
        Assert.Equal(0, snapshot.Tick);

        // A command records the world change; the watcher only sees it on the next tick.
        snapshot = sim.SetFlag(snapshot, "FLAG_X", 0);
        Assert.Equal(StoryEventLifecycle.Armed, LifecycleOf(sim, snapshot, model, "FlagWatcher"));

        snapshot = sim.Tick(snapshot);
        Assert.Equal(1, snapshot.Tick);
        Assert.Equal(1.0, snapshot.Clock);
        Assert.Equal(StoryEventLifecycle.Fired, LifecycleOf(sim, snapshot, model, "FlagWatcher"));
    }

    [Fact]
    public void Start_RecordsLoadArming_AsSteps()
    {
        var (sim, model) = Build();
        var snapshot = sim.Start();

        var rootStep = Assert.Single(snapshot.Steps, s => s.NodeId == NodeId(model, "Root"));
        Assert.Equal(0, rootStep.Tick);
        Assert.Equal(StorySimCause.Load, rootStep.Cause);
        Assert.Equal(StoryEventLifecycle.Armed, rootStep.To);
        Assert.DoesNotContain(snapshot.Steps, s => s.NodeId == NodeId(model, "Follower"));
    }

    [Fact]
    public void Steps_RecordEachTransition_WithSourceAndCause()
    {
        var (sim, model) = Build();
        var snapshot = sim.Start();
        var before = snapshot.Steps.Count;

        snapshot = sim.SatisfyTrigger(snapshot, NodeId(model, "Root"));

        var steps = snapshot.Steps.Skip(before).ToList();
        Assert.Equal(steps.Select(s => s.Seq), steps.Select(s => s.Seq).OrderBy(x => x));
        var fired = steps[0];
        Assert.Equal(NodeId(model, "Root"), fired.NodeId);
        Assert.Equal(StorySimCause.Manual, fired.Cause);
        Assert.Equal(StoryEventLifecycle.Fired, fired.To);

        var armed = Assert.Single(steps,
            s => s.NodeId == NodeId(model, "Follower") && s.To == StoryEventLifecycle.Armed);
        Assert.Equal(StoryEventLifecycle.Waiting, armed.From);
        Assert.Equal(NodeId(model, "Root"), armed.SourceNodeId);
        Assert.Equal(StorySimCause.Prereq, armed.Cause);

        var followerFired = Assert.Single(steps,
            s => s.NodeId == NodeId(model, "Follower") && s.To == StoryEventLifecycle.Fired);
        Assert.Equal(NodeId(model, "Root"), followerFired.SourceNodeId);
        Assert.True(armed.Seq < followerFired.Seq);
    }

    // ── World facts (chunk 3) ────────────────────────────────────────────────

    private const string WorldThreadText =
        "<Story>\n" +
        "\t<Event Name=\"Conq\">\n" +
        "\t\t<Event_Type>STORY_CONQUER</Event_Type>\n" +
        "\t\t<Event_Param1>Kuat Corellia</Event_Param1>\n" +
        "\t</Event>\n" +
        "\t<Event Name=\"Build\">\n" +
        "\t\t<Event_Type>STORY_CONSTRUCT</Event_Type>\n" +
        "\t\t<Event_Param1>X_Wing</Event_Param1>\n" +
        "\t\t<Event_Param2>2</Event_Param2>\n" +
        "\t</Event>\n" +
        "\t<Event Name=\"Tech\">\n" +
        "\t\t<Event_Type>STORY_TECH_LEVEL</Event_Type>\n" +
        "\t\t<Event_Param1>3</Event_Param1>\n" +
        "\t</Event>\n" +
        "\t<Event Name=\"Win\">\n" +
        "\t\t<Event_Type>STORY_VICTORY</Event_Type>\n" +
        "\t\t<Event_Param1>Rebel</Event_Param1>\n" +
        "\t</Event>\n" +
        "\t<Event Name=\"TwoWins\">\n" +
        "\t\t<Event_Type>STORY_WIN_BATTLES</Event_Type>\n" +
        "\t\t<Event_Param1>2</Event_Param1>\n" +
        "\t\t<Event_Param4>Kuat</Event_Param4>\n" +
        "\t</Event>\n" +
        "\t<Event Name=\"Rich\">\n" +
        "\t\t<Event_Type>STORY_ACCUMULATE</Event_Type>\n" +
        "\t\t<Event_Param1>1000</Event_Param1>\n" +
        "\t</Event>\n" +
        "\t<Event Name=\"Wreck\">\n" +
        "\t\t<Event_Type>STORY_TACTICAL_DESTROY</Event_Type>\n" +
        "\t\t<Event_Param1>TIE_Fighter</Event_Param1>\n" +
        "\t\t<Event_Param3>2</Event_Param3>\n" +
        "\t</Event>\n" +
        "\t<Event Name=\"Give\">\n" +
        "\t\t<Event_Type>STORY_GENERIC</Event_Type>\n" +
        "\t\t<Reward_Type>PLANET_FACTION</Reward_Type>\n" +
        "\t\t<Reward_Param1>Kuat</Reward_Param1>\n" +
        "\t\t<Reward_Param2>Empire</Reward_Param2>\n" +
        "\t</Event>\n" +
        "\t<Event Name=\"Untyped\">\n" +
        "\t\t<Event_Type>STORY_CLICK_GUI</Event_Type>\n" +
        "\t\t<Event_Param1>Button_Build</Event_Param1>\n" +
        "\t</Event>\n" +
        "</Story>\n";

    private static readonly StoryCampaignSeed Seed = new(
        ["Kuat", "Corellia", "Hoth"],
        [new StoryStartingForce("Rebel", "Hoth", "X_Wing"), new StoryStartingForce("Empire", "Kuat", "TIE_Fighter")],
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["Rebel"] = 2 },
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["Rebel"] = 500 },
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Rebel"] = "Hoth" });

    private static (StorySimulator Sim, StoryCampaignModel Model) BuildWorld(IStoryWorldSymbols? symbols = null)
    {
        var schema = new SimSchemaProvider();
        var thread = StoryThreadParser.Parse(WorldThreadText, ThreadAUri);
        var model = new StoryCampaignModel("GC", "Rebel", [thread],
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new StoryGraphBuilder(schema).Build([thread])) { Seed = Seed };
        return (new StorySimulator(model, schema, symbols), model);
    }

    [Fact]
    public void Start_SeedsTheWorld_FromTheCampaignSeed()
    {
        var (sim, _) = BuildWorld();

        var world = sim.Start().Runtime.World;

        Assert.Equal("Empire", world.Planets["Kuat"].Owner);
        Assert.Equal("Rebel", world.Planets["Hoth"].Owner);
        Assert.Null(world.Planets["Corellia"].Owner);
        Assert.Equal(1, world.UnitCount("X_Wing", "Rebel", "Hoth"));
        Assert.Equal(2, world.Tech["Rebel"]);
        Assert.Equal(500, world.Credits["Rebel"]);
    }

    [Fact]
    public void CapturePlanet_FiresTheConquerWatcher_ForAListedPlanetOnly()
    {
        var (sim, model) = BuildWorld();
        var snapshot = sim.Start();

        snapshot = sim.ApplyWorldChange(snapshot,
            new StoryWorldChange(StoryWorldChangeKind.CapturePlanet) { Planet = "Hoth", Faction = "Rebel" });
        Assert.Equal(StoryEventLifecycle.Armed, LifecycleOf(sim, snapshot, model, "Conq"));

        snapshot = sim.ApplyWorldChange(snapshot,
            new StoryWorldChange(StoryWorldChangeKind.CapturePlanet) { Planet = "Kuat", Faction = "Rebel" });
        Assert.Equal(StoryEventLifecycle.Fired, LifecycleOf(sim, snapshot, model, "Conq"));
        Assert.Equal("Rebel", snapshot.Runtime.World.Planets["Kuat"].Owner);
        Assert.Contains(snapshot.Steps, s => s.Cause == StorySimCause.World && s.NodeId == NodeId(model, "Conq"));
    }

    [Fact]
    public void BuildUnit_CountsToTheConstructThreshold()
    {
        var (sim, model) = BuildWorld();
        var snapshot = sim.Start();
        var build = new StoryWorldChange(StoryWorldChangeKind.BuildUnit)
            { UnitType = "X_Wing", Faction = "Rebel", Planet = "Hoth" };

        snapshot = sim.ApplyWorldChange(snapshot, build);
        Assert.Equal(StoryEventLifecycle.Armed, LifecycleOf(sim, snapshot, model, "Build"));

        snapshot = sim.ApplyWorldChange(snapshot, build);
        Assert.Equal(StoryEventLifecycle.Fired, LifecycleOf(sim, snapshot, model, "Build"));
        Assert.Equal(3, snapshot.Runtime.World.UnitCount("X_Wing", "Rebel", "Hoth"));
    }

    [Fact]
    public void SetTech_FiresTheTechWatcher_AtOrAboveItsLevel()
    {
        var (sim, model) = BuildWorld();
        var snapshot = sim.Start();

        snapshot = sim.ApplyWorldChange(snapshot,
            new StoryWorldChange(StoryWorldChangeKind.SetTech) { Faction = "Rebel", Amount = 2 });
        Assert.Equal(StoryEventLifecycle.Armed, LifecycleOf(sim, snapshot, model, "Tech"));

        snapshot = sim.ApplyWorldChange(snapshot,
            new StoryWorldChange(StoryWorldChangeKind.SetTech) { Faction = "Rebel", Amount = 4 });
        Assert.Equal(StoryEventLifecycle.Fired, LifecycleOf(sim, snapshot, model, "Tech"));
        Assert.Equal(4, snapshot.Runtime.World.Tech["Rebel"]);
    }

    [Fact]
    public void BattleWon_FiresVictoryForThatFaction_CountsListedPlanets_AndWritesAttachedFlags()
    {
        var (sim, model) = BuildWorld();
        var snapshot = sim.Start();
        var win = new StoryWorldChange(StoryWorldChangeKind.BattleWon)
        {
            Planet = "Kuat", Faction = "Rebel", Mode = "space",
            Flags = [new StoryFlagWrite("MISSION_DONE", 1)]
        };

        snapshot = sim.ApplyWorldChange(snapshot, win);
        Assert.Equal(StoryEventLifecycle.Fired, LifecycleOf(sim, snapshot, model, "Win"));
        Assert.Equal(StoryEventLifecycle.Armed, LifecycleOf(sim, snapshot, model, "TwoWins"));
        Assert.Equal(1, snapshot.Runtime.Flags["MISSION_DONE"]);

        snapshot = sim.ApplyWorldChange(snapshot,
            new StoryWorldChange(StoryWorldChangeKind.BattleWon) { Planet = "Hoth", Faction = "Rebel" });
        Assert.Equal(StoryEventLifecycle.Armed, LifecycleOf(sim, snapshot, model, "TwoWins"));

        snapshot = sim.ApplyWorldChange(snapshot,
            new StoryWorldChange(StoryWorldChangeKind.BattleWon) { Planet = "Kuat", Faction = "Rebel" });
        Assert.Equal(StoryEventLifecycle.Fired, LifecycleOf(sim, snapshot, model, "TwoWins"));
    }

    [Fact]
    public void AddCredits_FiresAccumulate_WithTheGreaterThanDefault()
    {
        // Measured: StoryEventAccumulateClass defaults to COMPARE_NONE, which the switch treats
        // as GREATER_THAN, and compares the player's credits after the change.
        var (sim, model) = BuildWorld();
        var snapshot = sim.Start();

        snapshot = sim.ApplyWorldChange(snapshot,
            new StoryWorldChange(StoryWorldChangeKind.AddCredits) { Faction = "Rebel", Amount = 500 });
        Assert.Equal(StoryEventLifecycle.Armed, LifecycleOf(sim, snapshot, model, "Rich"));

        snapshot = sim.ApplyWorldChange(snapshot,
            new StoryWorldChange(StoryWorldChangeKind.AddCredits) { Faction = "Rebel", Amount = 1 });
        Assert.Equal(StoryEventLifecycle.Fired, LifecycleOf(sim, snapshot, model, "Rich"));
        Assert.Equal(1001, snapshot.Runtime.World.Credits["Rebel"]);
    }

    [Fact]
    public void DestroyUnit_CountsPerTypeToTheThreshold()
    {
        var (sim, model) = BuildWorld();
        var snapshot = sim.Start();
        var wreck = new StoryWorldChange(StoryWorldChangeKind.DestroyUnit)
            { UnitType = "TIE_Fighter", Faction = "Empire", Planet = "Kuat" };

        snapshot = sim.ApplyWorldChange(snapshot, wreck);
        Assert.Equal(StoryEventLifecycle.Armed, LifecycleOf(sim, snapshot, model, "Wreck"));
        Assert.Equal(0, snapshot.Runtime.World.UnitCount("TIE_Fighter", "Empire", "Kuat"));

        snapshot = sim.ApplyWorldChange(snapshot, wreck);
        Assert.Equal(StoryEventLifecycle.Fired, LifecycleOf(sim, snapshot, model, "Wreck"));
    }

    [Fact]
    public void PlanetFactionReward_WritesTheOwnerFact()
    {
        var (sim, model) = BuildWorld();
        var snapshot = sim.Start();
        snapshot = sim.ApplyWorldChange(snapshot,
            new StoryWorldChange(StoryWorldChangeKind.CapturePlanet) { Planet = "Kuat", Faction = "Rebel" });
        Assert.Equal("Rebel", snapshot.Runtime.World.Planets["Kuat"].Owner);

        snapshot = sim.SatisfyTrigger(snapshot, NodeId(model, "Give"));

        Assert.Equal("Empire", snapshot.Runtime.World.Planets["Kuat"].Owner);
    }

    [Fact]
    public void UnknownPlanet_IsIgnoredWithAWarning_WhenSymbolsAreChecked()
    {
        var (sim, model) = BuildWorld(new KnownSymbols(["Kuat", "Corellia", "Hoth"]));
        var snapshot = sim.Start();

        snapshot = sim.ApplyWorldChange(snapshot,
            new StoryWorldChange(StoryWorldChangeKind.CapturePlanet) { Planet = "Kuatt", Faction = "Rebel" });

        Assert.Equal(StoryEventLifecycle.Armed, LifecycleOf(sim, snapshot, model, "Conq"));
        Assert.False(snapshot.Runtime.World.Planets.ContainsKey("Kuatt"));
        Assert.Contains(snapshot.Steps, s => s.Cause == StorySimCause.Ignored && s.Detail!.Contains("Kuatt"));
    }

    [Fact]
    public void Interventions_CarryTheWorldFacet_AndASuggestedChange()
    {
        var (sim, _) = BuildWorld();
        var snapshot = sim.Start();

        var interventions = sim.GetInterventions(snapshot);

        var conquer = Assert.Single(interventions, i => i.EventName == "Conq");
        Assert.Equal(StoryWorldChangeKind.CapturePlanet, conquer.Facet);
        Assert.Equal(["Kuat", "Corellia"], conquer.Options);
        Assert.Equal("Kuat", conquer.Suggested!.Planet);
        Assert.Equal("Rebel", conquer.Suggested.Faction);

        var tech = Assert.Single(interventions, i => i.EventName == "Tech");
        Assert.Equal(StoryWorldChangeKind.SetTech, tech.Facet);
        Assert.Equal(3, tech.Suggested!.Amount);

        var click = Assert.Single(interventions, i => i.EventName == "Untyped");
        Assert.Equal(StoryWorldChangeKind.ClickGui, click.Facet);
        Assert.Equal("Button_Build", click.Suggested!.Name);
    }

    private sealed class KnownSymbols(IEnumerable<string> planets) : IStoryWorldSymbols
    {
        private readonly HashSet<string> _planets = new(planets, StringComparer.OrdinalIgnoreCase);

        public bool Exists(string kind, string name)
        {
            return kind != StoryWorldSymbolKind.Planet || _planets.Contains(name);
        }
    }

    // ── Lua overlay (chunk 4) ────────────────────────────────────────────────

    private const string LuaUri = "file:///ws/data/scripts/story/story_lua.lua";

    private const string LuaThreadText =
        "<Story>\n" +
        "\t<Event Name=\"Act_Begin\">\n" +
        "\t\t<Event_Type>STORY_GENERIC</Event_Type>\n" +
        "\t\t<Reward_Type>TRIGGER_EVENT</Reward_Type>\n" +
        "\t\t<Reward_Param1>Second</Reward_Param1>\n" +
        "\t</Event>\n" +
        "\t<Event Name=\"Line_Done\">\n" +
        "\t\t<Event_Type>STORY_AI_NOTIFICATION</Event_Type>\n" +
        "\t\t<Event_Param1>LINE_ONE</Event_Param1>\n" +
        "\t\t<Prereq>Act_Begin</Prereq>\n" +
        "\t</Event>\n" +
        "\t<Event Name=\"Second\">\n" +
        "\t\t<Event_Type>STORY_GENERIC</Event_Type>\n" +
        "\t</Event>\n" +
        "\t<Event Name=\"Third\">\n" +
        "\t\t<Event_Type>STORY_GENERIC</Event_Type>\n" +
        "\t</Event>\n" +
        "</Story>\n";

    private static readonly LuaStoryMachine Machine = new(LuaUri, "story_lua",
    [
        new LuaStoryState("Act_Begin", "State_Act_Begin",
            new LuaStoryPhase([new LuaStoryEmission("LINE_ONE", 5)], [], [new LuaStorySpawn("X_Wing", "Hoth")],
                ["Talk"]),
            LuaStoryPhase.Empty,
            new LuaStoryPhase([new LuaStoryEmission("BYE", 0)], [], [], [])),
        new LuaStoryState("Second", "State_Second",
            new LuaStoryPhase([new LuaStoryEmission("SECOND", 0)], [], [], []),
            LuaStoryPhase.Empty, LuaStoryPhase.Empty),
        new LuaStoryState("Third", "State_Third", LuaStoryPhase.Empty, LuaStoryPhase.Empty, LuaStoryPhase.Empty)
    ]);

    private static (StorySimulator Sim, StoryCampaignModel Model) BuildLua()
    {
        var schema = new SimSchemaProvider();
        var thread = StoryThreadParser.Parse(LuaThreadText, ThreadAUri);
        var model = new StoryCampaignModel("GC", "Rebel", [thread],
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new StoryGraphBuilder(schema).Build([thread], null, [Machine])) { LuaMachines = [Machine] };
        return (new StorySimulator(model, schema), model);
    }

    private static string StateId(string state)
    {
        return StoryGraphBuilder.LuaStateNodeId(LuaUri, state);
    }

    [Fact]
    public void Fire_EventKeyedInStoryModeEvents_SetsTheScriptsNextState()
    {
        var (sim, model) = BuildLua();
        var snapshot = sim.Start();

        snapshot = sim.SatisfyTrigger(snapshot, NodeId(model, "Act_Begin"));

        var script = snapshot.Runtime.Scripts[LuaUri];
        Assert.Null(script.Current);
        Assert.Equal("Act_Begin", script.Next);
        var step = Assert.Single(snapshot.Steps, s => s.Cause == StorySimCause.LuaTrigger);
        Assert.Equal(StateId("Act_Begin"), step.NodeId);
        Assert.Equal(NodeId(model, "Act_Begin"), step.SourceNodeId);
    }

    [Fact]
    public void Tick_EntersTheState_SpawnsAndOwesEmissions_ThenDispatchesThemWhenDue()
    {
        var (sim, model) = BuildLua();
        var snapshot = sim.SatisfyTrigger(sim.Start(), NodeId(model, "Act_Begin"));

        snapshot = sim.Tick(snapshot);
        Assert.Equal("Act_Begin", snapshot.Runtime.Scripts[LuaUri].Current);
        Assert.Contains(snapshot.Steps, s => s.Cause == StorySimCause.LuaEnter && s.NodeId == StateId("Act_Begin"));
        Assert.Equal(1, snapshot.Runtime.World.UnitCount("X_Wing", "Rebel", "Hoth"));
        Assert.Equal(StoryEventLifecycle.Armed, LifecycleOf(sim, snapshot, model, "Line_Done"));

        snapshot = sim.Tick(snapshot, 4);
        Assert.Equal(StoryEventLifecycle.Armed, LifecycleOf(sim, snapshot, model, "Line_Done"));

        snapshot = sim.Tick(snapshot);
        Assert.Equal(StoryEventLifecycle.Fired, LifecycleOf(sim, snapshot, model, "Line_Done"));
        var fired = Assert.Single(snapshot.Steps,
            s => s.NodeId == NodeId(model, "Line_Done") && s.To == StoryEventLifecycle.Fired);
        Assert.Equal(StorySimCause.Lua, fired.Cause);
        Assert.Equal(StateId("Act_Begin"), fired.SourceNodeId);
    }

    [Fact]
    public void Trigger_InTheSameFrameAsAPendingTransition_IsDroppedWithAWarning()
    {
        // Measured: Story_Event_Trigger only sets the next state when current == next. Act_Begin's
        // TRIGGER_EVENT fires Second inside the same cascade, one frame, so Second's trigger drops.
        var (sim, model) = BuildLua();

        var snapshot = sim.SatisfyTrigger(sim.Start(), NodeId(model, "Act_Begin"));

        Assert.Equal(StoryEventLifecycle.Fired, LifecycleOf(sim, snapshot, model, "Second"));
        Assert.Equal("Act_Begin", snapshot.Runtime.Scripts[LuaUri].Next);
        Assert.Contains(snapshot.Steps,
            s => s.Cause == StorySimCause.Ignored && s.Detail!.Contains("'Second' dropped"));
    }

    [Fact]
    public void Trigger_OnALaterCommand_FindsTheTransitionDone()
    {
        // A command is its own frame: the script serviced the pending transition before it.
        var (sim, model) = BuildLua();
        var snapshot = sim.SatisfyTrigger(sim.Start(), NodeId(model, "Act_Begin"));

        snapshot = sim.SatisfyTrigger(snapshot, NodeId(model, "Third"));

        var script = snapshot.Runtime.Scripts[LuaUri];
        Assert.Equal("Act_Begin", script.Current);
        Assert.Equal("Third", script.Next);
    }

    [Fact]
    public void Transition_RunsOnExit_ThenOnEnter_InOrder()
    {
        var (sim, model) = BuildLua();
        var snapshot = sim.Tick(sim.SatisfyTrigger(sim.Start(), NodeId(model, "Act_Begin")));
        var before = snapshot.Steps.Count;

        snapshot = sim.Tick(sim.SatisfyTrigger(snapshot, NodeId(model, "Third")));

        var luaSteps = snapshot.Steps.Skip(before)
            .Where(s => s.Cause is StorySimCause.LuaExit or StorySimCause.LuaEnter)
            .Select(s => (s.Cause, s.NodeId)).ToList();
        Assert.Equal([(StorySimCause.LuaExit, StateId("Act_Begin")), (StorySimCause.LuaEnter, StateId("Third"))],
            luaSteps);
        Assert.Equal("Third", snapshot.Runtime.Scripts[LuaUri].Current);
    }

    [Fact]
    public void GraphBuilder_ProducesLuaStateNodes_AndLuaLinkEdges()
    {
        var (_, model) = BuildLua();
        var graph = model.Graph;

        var state = Assert.Single(graph.Nodes, n => n.Id == StateId("Act_Begin"));
        Assert.Equal(StoryNodeKind.LuaState, state.Kind);
        Assert.Equal(LuaUri, state.ThreadUri);
        Assert.Contains(graph.Edges,
            e => e.Kind == StoryEdgeKind.LuaLink && e.FromId == NodeId(model, "Act_Begin") && e.ToId == state.Id);
        Assert.Contains(graph.Edges,
            e => e.Kind == StoryEdgeKind.LuaLink && e.FromId == state.Id && e.ToId == NodeId(model, "Line_Done") &&
                 e.Label == "LINE_ONE");
    }

    private const string TimerChainText =
        "<Story>\n" +
        "\t<Event Name=\"T0\">\n" +
        "\t\t<Event_Type>STORY_ELAPSED</Event_Type>\n" +
        "\t\t<Event_Param1>1</Event_Param1>\n" +
        "\t</Event>\n" +
        "\t<Event Name=\"T1\">\n" +
        "\t\t<Event_Type>STORY_ELAPSED</Event_Type>\n" +
        "\t\t<Event_Param1>1</Event_Param1>\n" +
        "\t\t<Prereq>T0</Prereq>\n" +
        "\t</Event>\n" +
        "\t<Event Name=\"Decision\">\n" +
        "\t\t<Event_Type>STORY_GENERIC</Event_Type>\n" +
        "\t\t<Prereq>T1</Prereq>\n" +
        "\t</Event>\n" +
        "</Story>\n";

    private static (StorySimulator Sim, StoryCampaignModel Model) BuildTimerChain()
    {
        var schema = new SimSchemaProvider();
        var thread = StoryThreadParser.Parse(TimerChainText, ThreadAUri);
        var model = new StoryCampaignModel("GC", "Rebel", [thread],
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new StoryGraphBuilder(schema).Build([thread]));
        return (new StorySimulator(model, schema), model);
    }

    [Fact]
    public void RunToDecision_TicksUntilAnInterventionAppears()
    {
        var (sim, model) = BuildTimerChain();
        var snapshot = sim.Start();
        Assert.Empty(sim.GetInterventions(snapshot));

        // T0 fires at tick 1 and arms T1, which fires at tick 2 and arms the manual Decision.
        snapshot = sim.RunToDecision(snapshot, StorySimBreakpoints.None);

        Assert.Equal(2, snapshot.Tick);
        Assert.Equal("Decision", Assert.Single(sim.GetInterventions(snapshot)).EventName);
        Assert.Equal(StoryEventLifecycle.Fired, LifecycleOf(sim, snapshot, model, "T1"));
    }

    [Fact]
    public void RunToDecision_StopsWhenATickChangesNothing()
    {
        var (sim, model) = BuildTimerChain();
        var snapshot = sim.RunToDecision(sim.Start(), StorySimBreakpoints.None);
        snapshot = sim.SatisfyTrigger(snapshot, NodeId(model, "Decision"));

        // Nothing is left to happen: the run ends after one idle tick instead of spinning.
        snapshot = sim.RunToDecision(snapshot, StorySimBreakpoints.None);

        Assert.Equal(3, snapshot.Tick);
        Assert.Empty(sim.GetInterventions(snapshot));
    }

    [Fact]
    public void Breakpoint_HaltsAfterTheTickInWhichTheNodeFires()
    {
        var (sim, model) = BuildTimerChain();
        var breakpoints = new StorySimBreakpoints([NodeId(model, "T0")], false);
        var snapshot = sim.Start();

        snapshot = sim.Tick(snapshot, 5, breakpoints);

        Assert.Equal(1, snapshot.Tick);
        Assert.Equal(NodeId(model, "T0"), snapshot.HaltedAt);
        Assert.Contains(snapshot.Steps, s => s.Cause == StorySimCause.Breakpoint && s.NodeId == NodeId(model, "T0"));

        // The halt is a pause, not a wall: the next command continues past it.
        snapshot = sim.Tick(snapshot, 5, breakpoints);
        Assert.Null(snapshot.HaltedAt);
        Assert.Equal(6, snapshot.Tick);
        Assert.Equal(StoryEventLifecycle.Fired, LifecycleOf(sim, snapshot, model, "T1"));
    }

    [Fact]
    public void BreakOnGates_HaltsWhenAClockOrFlagEventFires()
    {
        var (sim, model) = BuildTimerChain();
        var breakpoints = new StorySimBreakpoints([], true);

        var snapshot = sim.RunToDecision(sim.Start(), breakpoints);

        Assert.Equal(1, snapshot.Tick);
        Assert.Equal(NodeId(model, "T0"), snapshot.HaltedAt);
    }

    [Fact]
    public void StoryTrigger_FiresFromPrereqsAlone()
    {
        var (sim, model) = Build();
        var snapshot = sim.Start();
        Assert.Equal(StoryEventLifecycle.Waiting, LifecycleOf(sim, snapshot, model, "Follower"));

        snapshot = sim.SatisfyTrigger(snapshot, NodeId(model, "Root"));

        Assert.Equal(StoryEventLifecycle.Fired, LifecycleOf(sim, snapshot, model, "Follower"));
    }

    [Fact]
    public void ResetEvent_ClearsFired_AndRearmsOnlyWhenAParentFiresAgain()
    {
        // Measured: Clear_Triggered drops Triggered and Reset drops Active when the event has
        // prereqs; nothing re-arms it until a prereq pushes Parent_Triggered again.
        var (sim, model) = Build();
        var snapshot = sim.Start();
        snapshot = sim.SatisfyTrigger(snapshot, NodeId(model, "Root"));
        Assert.Equal(StoryEventLifecycle.Fired, LifecycleOf(sim, snapshot, model, "Follower"));

        snapshot = sim.SatisfyTrigger(snapshot, NodeId(model, "Resetter"));
        Assert.Equal(StoryEventLifecycle.Waiting, LifecycleOf(sim, snapshot, model, "Follower"));

        snapshot = sim.SatisfyTrigger(snapshot, NodeId(model, "Retrigger"));
        Assert.Equal(StoryEventLifecycle.Fired, LifecycleOf(sim, snapshot, model, "Follower"));
    }

    [Fact]
    public void ResetBranch_ClearsMembers_RearmsFromFiredPrereqs_AndTriggersParam1()
    {
        var (sim, model) = Build();
        var snapshot = sim.Start();
        snapshot = sim.SatisfyTrigger(snapshot, NodeId(model, "M1"));
        Assert.Equal(StoryEventLifecycle.Fired, LifecycleOf(sim, snapshot, model, "M1"));
        Assert.Equal(StoryEventLifecycle.Fired, LifecycleOf(sim, snapshot, model, "M2"));
        Assert.Equal(StoryEventLifecycle.Armed, LifecycleOf(sim, snapshot, model, "Extra"));

        snapshot = sim.SatisfyTrigger(snapshot, NodeId(model, "BranchReset"));

        // A root member keeps its armed flag through a reset; the STORY_TRIGGER member is cleared
        // and pass 2 finds its prereq no longer fired, so it waits.
        Assert.Equal(StoryEventLifecycle.Armed, LifecycleOf(sim, snapshot, model, "M1"));
        Assert.Equal(StoryEventLifecycle.Waiting, LifecycleOf(sim, snapshot, model, "M2"));
        Assert.Equal(StoryEventLifecycle.Fired, LifecycleOf(sim, snapshot, model, "Extra"));
    }

    [Fact]
    public void DisableStoryEvent_ParamZero_Enables_AndMissingParamIsIgnored()
    {
        var (sim, model) = Build();
        var snapshot = sim.Start();
        snapshot = sim.SatisfyTrigger(snapshot, NodeId(model, "Disabler"));
        Assert.Equal(StoryEventLifecycle.Disabled, LifecycleOf(sim, snapshot, model, "Victim"));

        snapshot = sim.SatisfyTrigger(snapshot, NodeId(model, "Enabler"));
        Assert.Equal(StoryEventLifecycle.Armed, LifecycleOf(sim, snapshot, model, "Victim"));

        snapshot = sim.SatisfyTrigger(snapshot, NodeId(model, "HalfDisabler"));
        Assert.Equal(StoryEventLifecycle.Armed, LifecycleOf(sim, snapshot, model, "Victim2"));
        Assert.Contains(snapshot.Log, l => l.Contains("ignored", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TriggerEvent_FiresAWaitingEvent_IgnoringItsPrereqs()
    {
        // Measured: Reward_Trigger_Event calls Event_Triggered on the named event in every
        // subplot with no Active, prereq or Triggered check.
        var (sim, model) = Build();
        var snapshot = sim.Start();
        Assert.Equal(StoryEventLifecycle.Waiting, LifecycleOf(sim, snapshot, model, "Forced"));

        snapshot = sim.SatisfyTrigger(snapshot, NodeId(model, "Forcer"));

        Assert.Equal(StoryEventLifecycle.Fired, LifecycleOf(sim, snapshot, model, "Forced"));
        Assert.Equal(StoryEventLifecycle.Armed, LifecycleOf(sim, snapshot, model, "NeverFires"));
    }

    [Fact]
    public void SpeechDone_CompletesOnTheNextCommand_AfterItsSpeechReward()
    {
        var (sim, model) = Build();
        var snapshot = sim.Start();

        snapshot = sim.SatisfyTrigger(snapshot, NodeId(model, "Talker"));
        Assert.Equal(StoryEventLifecycle.Armed, LifecycleOf(sim, snapshot, model, "AfterTalk"));
        Assert.DoesNotContain(sim.GetInterventions(snapshot), i => i.EventName == "AfterTalk");

        snapshot = sim.AdvanceClock(snapshot, 1);
        Assert.Equal(StoryEventLifecycle.Fired, LifecycleOf(sim, snapshot, model, "AfterTalk"));
    }

    [Fact]
    public void Elapsed_CountsFromArming_NotFromCampaignStart()
    {
        var (sim, model) = Build();
        var snapshot = sim.Start();

        snapshot = sim.AdvanceClock(snapshot, 10);
        Assert.Equal(StoryEventLifecycle.Waiting, LifecycleOf(sim, snapshot, model, "LateTimer"));

        snapshot = sim.SatisfyTrigger(snapshot, NodeId(model, "Root"));
        Assert.Equal(StoryEventLifecycle.Armed, LifecycleOf(sim, snapshot, model, "LateTimer"));

        snapshot = sim.AdvanceClock(snapshot, 4);
        Assert.Equal(StoryEventLifecycle.Armed, LifecycleOf(sim, snapshot, model, "LateTimer"));

        snapshot = sim.AdvanceClock(snapshot, 1);
        Assert.Equal(StoryEventLifecycle.Fired, LifecycleOf(sim, snapshot, model, "LateTimer"));
    }

    [Fact]
    public void StoryFlag_FlagList_IsOrSemantics()
    {
        // Schema: multiple values in param 0 = OR condition. No comparison params, so the
        // measured default applies: EQUAL_TO 0.
        var (sim, model) = Build();
        var snapshot = sim.Start();

        snapshot = sim.Tick(sim.SetFlag(snapshot, "FLAG_B", 0));

        Assert.Equal(StoryEventLifecycle.Fired, LifecycleOf(sim, snapshot, model, "EitherWatcher"));
    }

    [Fact]
    public void StoryFlag_GreaterComparison_UsesParams()
    {
        var (sim, model) = Build();
        var snapshot = sim.Start();

        snapshot = sim.Tick(sim.SetFlag(snapshot, "FLAG_C", 3));
        Assert.Equal(StoryEventLifecycle.Armed, LifecycleOf(sim, snapshot, model, "CounterWatcher"));

        snapshot = sim.Tick(sim.SetFlag(snapshot, "FLAG_C", 4));
        Assert.Equal(StoryEventLifecycle.Fired, LifecycleOf(sim, snapshot, model, "CounterWatcher"));
    }

    [Fact]
    public void IncrementFlag_AddsToTheFlagValue()
    {
        var (sim, model) = Build();
        var snapshot = sim.Start();
        snapshot = sim.SetFlag(snapshot, "FLAG_C", 2);

        snapshot = sim.SatisfyTrigger(snapshot, NodeId(model, "Incrementer"));
        snapshot = sim.SatisfyTrigger(snapshot, NodeId(model, "Incrementer"));
        snapshot = sim.Tick(snapshot);

        // 2 + 2 x 2 = 6 > 3 - the greater-than watcher fires on the tick after the second increment.
        Assert.Equal(6, snapshot.Runtime.Flags["FLAG_C"]);
        Assert.Equal(StoryEventLifecycle.Fired, LifecycleOf(sim, snapshot, model, "CounterWatcher"));
    }

    [Fact]
    public void SatisfyTrigger_FiresArmedEvent_AndAppliesFlagRewards()
    {
        var (sim, model) = Build();
        var snapshot = sim.Start();

        snapshot = sim.SatisfyTrigger(snapshot, NodeId(model, "Setter"));

        Assert.Equal(StoryEventLifecycle.Fired, LifecycleOf(sim, snapshot, model, "Setter"));
        Assert.Equal(1, snapshot.Runtime.Flags["FLAG_Y"]);
    }

    [Fact]
    public void SatisfyTrigger_NotArmed_IsIgnoredWithWarning()
    {
        var (sim, model) = Build();
        var snapshot = sim.Start();

        var next = sim.SatisfyTrigger(snapshot, NodeId(model, "Chained"));

        Assert.Equal(snapshot.Runtime.FiredEvents, next.Runtime.FiredEvents);
        Assert.Contains(next.Log, l => l.Contains("not armed"));
    }

    [Fact]
    public void LuaNotify_FiresMatchingAiNotification()
    {
        var (sim, model) = Build();
        var snapshot = sim.Start();

        snapshot = sim.LuaNotify(snapshot, "Alert_One");
        Assert.Equal(StoryEventLifecycle.Fired, LifecycleOf(sim, snapshot, model, "Notify"));

        var unmatched = sim.LuaNotify(snapshot, "Alert_Ghost");
        Assert.Contains(unmatched.Log, l => l.Contains("No armed event listens"));
    }

    [Fact]
    public void DisableReward_DisablesTheTarget()
    {
        var (sim, model) = Build();
        var snapshot = sim.Start();

        snapshot = sim.SatisfyTrigger(snapshot, NodeId(model, "Disabler"));

        Assert.Equal(StoryEventLifecycle.Disabled, LifecycleOf(sim, snapshot, model, "Victim"));
    }

    [Fact]
    public void StoryElementReward_ActivatesTheSuspendedThread()
    {
        var (sim, model) = Build();
        var snapshot = sim.Start();
        Assert.Equal(StoryEventLifecycle.Inactive, LifecycleOf(sim, snapshot, model, "BEvent"));

        snapshot = sim.SatisfyTrigger(snapshot, NodeId(model, "Activator"));
        Assert.Equal(StoryEventLifecycle.Armed, LifecycleOf(sim, snapshot, model, "BEvent"));

        // Thread B is active now; its elapsed-0 event fires on the next tick's poll.
        snapshot = sim.Tick(snapshot);
        Assert.Equal(StoryEventLifecycle.Fired, LifecycleOf(sim, snapshot, model, "BEvent"));
    }

    [Fact]
    public void PerpetualEvent_RefiresOncePerTick()
    {
        var (sim, model) = Build();
        var snapshot = sim.Start();

        snapshot = sim.Tick(sim.SetFlag(snapshot, "FLAG_P", 0));
        var firstCount = snapshot.Log.Count(l => l.Contains("Fired 'PerpFlag'"));

        snapshot = sim.Tick(snapshot);
        var secondCount = snapshot.Log.Count(l => l.Contains("Fired 'PerpFlag'"));

        Assert.Equal(1, firstCount);
        Assert.Equal(2, secondCount);
        Assert.Equal(StoryEventLifecycle.Armed, LifecycleOf(sim, snapshot, model, "PerpFlag"));
    }

    [Fact]
    public void Interventions_ListArmedManualEvents_WithKinds()
    {
        var (sim, model) = Build();
        var snapshot = sim.Start();

        var interventions = sim.GetInterventions(snapshot);

        Assert.DoesNotContain(interventions, i => i.EventName is "Begin" or "Chained" or "Later" or "FlagWatcher");
        Assert.Equal("lua", Assert.Single(interventions, i => i.EventName == "Notify").Kind);
        Assert.Equal(["Alert_One"], Assert.Single(interventions, i => i.EventName == "Notify").Options);
        Assert.Equal("manual", Assert.Single(interventions, i => i.EventName == "Setter").Kind);
        _ = NodeId(model, "Setter"); // fixture sanity
    }

    [Fact]
    public void SameCommandSequence_ProducesIdenticalState()
    {
        static string Fingerprint(StorySimulator sim, StorySimSnapshot snapshot)
        {
            var fired = string.Join(",", snapshot.Runtime.FiredEvents.OrderBy(x => x, StringComparer.Ordinal));
            var flags = string.Join(",", snapshot.Runtime.Flags
                .OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
                .Select(kvp => $"{kvp.Key}={kvp.Value}"));
            var steps = string.Join(";",
                snapshot.Steps.Select(s => $"{s.Tick}:{s.Seq}:{s.NodeId}:{s.From}>{s.To}:{s.Cause}"));
            return $"{fired}|{flags}|{snapshot.Clock}|{string.Join(";", snapshot.Log)}|{steps}";
        }

        static string Run()
        {
            var (sim, model) = Build();
            var snapshot = sim.Start();
            snapshot = sim.SetFlag(snapshot, "FLAG_X", 1);
            snapshot = sim.AdvanceClock(snapshot, 10);
            snapshot = sim.SatisfyTrigger(snapshot, NodeId(model, "Setter"));
            snapshot = sim.LuaNotify(snapshot, "Alert_One");
            return Fingerprint(sim, snapshot);
        }

        Assert.Equal(Run(), Run());
    }

    // ── fakes ────────────────────────────────────────────────────────────────

    private sealed class SimSchemaProvider : ISchemaProvider
    {
        private static readonly EnumDefinition Events = new()
        {
            Name = "StoryEventType",
            Values =
            [
                new EnumValueDefinition { Name = "STORY_ELAPSED" },
                new EnumValueDefinition { Name = "STORY_TRIGGER" },
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
                    Name = "STORY_AI_NOTIFICATION",
                    Params =
                    [
                        new ParamDefinition
                        {
                            Position = 0, ValueType = XmlValueType.NameReference,
                            ReferenceTypeName = StoryReferenceTypes.Notification
                        }
                    ]
                },
                new EnumValueDefinition { Name = "STORY_GENERIC" },
                new EnumValueDefinition { Name = "STORY_SPEECH_DONE" }
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
                            ReferenceTypeName = StoryReferenceTypes.EventName
                        }
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
                },
                new EnumValueDefinition
                {
                    Name = "DISABLE_STORY_EVENT",
                    Params =
                    [
                        new ParamDefinition
                        {
                            Position = 0, ValueType = XmlValueType.NameReference,
                            ReferenceTypeName = StoryReferenceTypes.EventName
                        }
                    ]
                },
                new EnumValueDefinition { Name = "STORY_ELEMENT" },
                new EnumValueDefinition { Name = "SPEECH" },
                new EnumValueDefinition
                {
                    Name = "RESET_EVENT",
                    Params =
                    [
                        new ParamDefinition
                        {
                            Position = 0, ValueType = XmlValueType.NameReference,
                            ReferenceTypeName = StoryReferenceTypes.EventName
                        }
                    ]
                },
                new EnumValueDefinition
                {
                    Name = "RESET_BRANCH",
                    Params =
                    [
                        new ParamDefinition
                        {
                            Position = 0, ValueType = XmlValueType.NameReference,
                            ReferenceTypeName = StoryReferenceTypes.Branch
                        },
                        new ParamDefinition
                        {
                            Position = 1, ValueType = XmlValueType.NameReference,
                            ReferenceTypeName = StoryReferenceTypes.EventName
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
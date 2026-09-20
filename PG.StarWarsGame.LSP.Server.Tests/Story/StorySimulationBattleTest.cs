// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Server.Story;
using PG.StarWarsGame.LSP.Story.Sim;

namespace PG.StarWarsGame.LSP.Server.Tests.Story;

/// <summary>
///     A battle is its own session with its own clock. The game freezes the galaxy while a battle
///     plays, so the galactic session pauses while one is up; only the flags cross, and the
///     outcome lands back in the galaxy as one world change when the battle resolves. The campaign
///     is <see cref="StoryBattleFixture" />.
/// </summary>
public sealed class StorySimulationBattleTest
{
    private const string Galaxy = StoryBattleFixture.Galaxy;
    private const string Battle = StoryBattleFixture.Battle;
    private const string M2Key = StoryBattleFixture.M2Key;
    private const string M5Key = StoryBattleFixture.M5Key;

    private static readonly StorySimKey GalaxyKey = new("GC", "Empire");
    private static readonly StorySimKey M2Session = new("GC", "Empire", M2Key);
    private static readonly StorySimKey M5Session = new("GC", "Empire", M5Key);

    private static (StorySimulationService Service, List<StorySimKey> Notified) BuildService()
    {
        var notified = new List<StorySimKey>();
        var service = new StorySimulationService(
            new StoryBattleFixture.ModelService(), new StoryBattleFixture.IndexService(),
            new StoryBattleFixture.Schema(), notified.Add);
        return (service, notified);
    }

    private static string Id(string uri, string name)
    {
        return StoryBattleFixture.Id(uri, name);
    }

    private static string LifecycleOf(StorySimStateDto state, string uri, string name)
    {
        return state.Nodes.Single(n => n.NodeId == Id(uri, name)).Lifecycle;
    }

    [Fact]
    public void Start_Galactic_ListsTheBattlesNotStarted_AndArmsNoTacticalListener()
    {
        var (service, _) = BuildService();

        var (state, error) = service.Start(GalaxyKey);

        Assert.Null(error);
        Assert.Null(state!.Scope);
        Assert.Null(state.PausedFor);
        Assert.Equal(["Story_Plots_M2_Land", "Story_Plots_M5_Space"], state.Battles.Select(b => b.Label));
        // Both links fire on the first frame: the first battle's choice is up, the second link
        // queued nothing (measured: the engine refuses a queue while one is pending).
        Assert.Equal(["pending", "notStarted"], state.Battles.Select(b => b.Status));
        // Win is the galaxy's own listener and waits; nothing from inside a battle is armed.
        Assert.Contains(state.Interventions, i => i.EventName == "Win" && i.BattleKey == M2Key);
        Assert.DoesNotContain(state.Nodes, n => n.NodeId == Id(Battle, "Ambush"));
    }

    [Fact]
    public void Start_Battle_PausesTheGalaxy_AndSeedsItsFlagsFromIt()
    {
        var (service, notified) = BuildService();
        service.Start(GalaxyKey);
        service.SetFlag(GalaxyKey, "G", 7);
        notified.Clear();

        var (battle, error) = service.Start(M2Session);

        Assert.Null(error);
        Assert.Equal(M2Key, battle!.Scope);
        Assert.Equal(0, battle.Tick);
        Assert.Contains(battle.Flags, f => f.Name == "G" && f.Value == 7);
        Assert.Contains(battle.Nodes, n => n.NodeId == Id(Battle, "Ambush"));
        Assert.DoesNotContain(battle.Nodes, n => n.NodeId == Id(Galaxy, "Win"));

        var galaxy = service.GetState(GalaxyKey).State!;
        Assert.Equal("Story_Plots_M2_Land", galaxy.PausedFor);
        Assert.Equal("running", galaxy.Battles.Single(b => b.Key == M2Key).Status);
        var (_, refused) = service.Tick(GalaxyKey, 1);
        Assert.Contains("paused", refused, StringComparison.OrdinalIgnoreCase);
        // Both panels learn of it: the battle exists, the galaxy is paused.
        Assert.Contains(M2Session, notified);
        Assert.Contains(GalaxyKey, notified);
    }

    [Fact]
    public void Start_SecondBattle_WhileOneRuns_IsRefused()
    {
        var (service, _) = BuildService();
        service.Start(GalaxyKey);
        service.Start(M2Session);

        var (state, error) = service.Start(M5Session);

        Assert.Null(state);
        Assert.Contains("Story_Plots_M2_Land", error);
    }

    [Fact]
    public void Start_UnknownBattle_IsRefused()
    {
        var (service, _) = BuildService();
        service.Start(GalaxyKey);

        var (state, error) = service.Start(GalaxyKey with { Scope = "story_plots_nowhere.xml" });

        Assert.Null(state);
        Assert.NotNull(error);
    }

    [Fact]
    public void ResolveBattle_MergesItsFlagWrites_FiresTheGalacticListener_AndResumesTheGalaxy()
    {
        var (service, notified) = BuildService();
        service.Start(GalaxyKey);
        service.Start(M2Session);
        // Inside the battle: the ambush happens, reinforcements follow and write F.
        service.SatisfyTrigger(M2Session, Id(Battle, "Ambush"));
        Assert.Contains(service.GetState(M2Session).State!.Flags, f => f.Name == "F" && f.Value == 1);
        Assert.DoesNotContain(service.GetState(GalaxyKey).State!.Flags, f => f.Name == "F");
        notified.Clear();

        var (galaxy, error) = service.ResolveBattle(GalaxyKey, M2Key, true);

        Assert.Null(error);
        Assert.Null(galaxy!.Scope);
        Assert.Null(galaxy.PausedFor);
        Assert.Contains(galaxy.Flags, f => f.Name == "F" && f.Value == 1);
        Assert.Equal("Fired", LifecycleOf(galaxy, Galaxy, "Win"));
        // A STORY_FLAG is polled, so the merged flag is read on the galaxy's next tick, not on the
        // write itself.
        Assert.Equal("Armed", LifecycleOf(galaxy, Galaxy, "Reader"));
        Assert.Equal("won", galaxy.Battles.Single(b => b.Key == M2Key).Status);
        Assert.Contains(M2Session, notified);
        Assert.Contains(GalaxyKey, notified);
        // The battle's own state keeps its outcome and refuses to run on.
        var battle = service.GetState(M2Session).State!;
        Assert.Equal("won", battle.Outcome);
        Assert.NotNull(service.Tick(M2Session, 1).Error);
        // The galaxy ticks again, and its flag reader sees what the battle wrote.
        var (ticked, tickError) = service.Tick(GalaxyKey, 1);
        Assert.Null(tickError);
        Assert.Equal("Fired", LifecycleOf(ticked!, Galaxy, "Reader"));
    }

    [Fact]
    public void Seek_ReplaysTheResolution_AsOneGalacticCommand()
    {
        var (service, _) = BuildService();
        service.Start(GalaxyKey);
        service.Tick(GalaxyKey, 2);
        service.Start(M2Session);
        service.SatisfyTrigger(M2Session, Id(Battle, "Ambush"));
        service.ResolveBattle(GalaxyKey, M2Key, true);
        service.Tick(GalaxyKey, 3);

        // Back to the tick after the resolution (which landed at tick 2, so a seek to 2 - the state
        // just after that tick completed - would sit before it): the outcome and its flag writes
        // are there, replayed from the galactic log alone - the battle's own log was never consulted.
        var (state, error) = service.Seek(GalaxyKey, 3);

        Assert.Null(error);
        Assert.Equal(3, state!.Tick);
        Assert.Equal("Fired", LifecycleOf(state, Galaxy, "Win"));
        Assert.Contains(state.Flags, f => f.Name == "F" && f.Value == 1);
        Assert.Equal("won", state.Battles.Single(b => b.Key == M2Key).Status);

        // Before the battle was ever entered: the outcome is gone, the battle is back to its
        // pending choice, where the link left it on the first frame.
        var earlier = service.Seek(GalaxyKey, 1).State!;
        Assert.Equal("Armed", LifecycleOf(earlier, Galaxy, "Win"));
        Assert.Equal("pending", earlier.Battles.Single(b => b.Key == M2Key).Status);
    }

    [Fact]
    public void ResolveBattle_WithoutEntering_TakesTheOutcomeStraightAway()
    {
        var (service, _) = BuildService();
        service.Start(GalaxyKey);

        var (state, error) = service.ResolveBattle(GalaxyKey, M2Key, false);

        Assert.Null(error);
        Assert.Equal("lost", state!.Battles.Single(b => b.Key == M2Key).Status);
        // A defeat fires no STORY_VICTORY; the galaxy's listener is moot now, not a decision.
        Assert.Equal("Armed", LifecycleOf(state, Galaxy, "Win"));
        Assert.DoesNotContain(state.Interventions, i => i.EventName == "Win");
    }

    /// <summary>
    ///     Deciding a battle on the portal is not playing it, but its outcome listeners still run:
    ///     the tutorial's victory listener increments the flag the galaxy reads, and without it the
    ///     galactic story stood still after a win decided on the portal.
    /// </summary>
    [Fact]
    public void ResolveBattle_WithoutEntering_RunsTheBattlesOwnOutcomeListeners()
    {
        var (service, _) = BuildService();
        service.Start(GalaxyKey);

        var (state, error) = service.ResolveBattle(GalaxyKey, M2Key, true);

        Assert.Null(error);
        Assert.Contains(state!.Flags, f => f.Name == "W" && f.Value == 1);
        Assert.Equal("Fired", LifecycleOf(state, Galaxy, "Win"));
        // No battle session was left behind: the decision was the galaxy's alone.
        Assert.False(service.GetState(M2Session).State!.Running);
    }

    [Fact]
    public void ResolveBattle_TakesTheAuthorsPicks_AsFlagWritesThatCross()
    {
        var (service, _) = BuildService();
        service.Start(GalaxyKey);

        var (state, error) = service.ResolveBattle(GalaxyKey, M2Key, true, 0,
            [new StorySimFlagDto("F", 1), new StorySimFlagDto("Score", 7)]);

        Assert.Null(error);
        Assert.Contains(state!.Flags, f => f.Name == "F" && f.Value == 1);
        Assert.Contains(state.Flags, f => f.Name == "Score" && f.Value == 7);
        // Replayed as one command, picks included.
        service.Tick(GalaxyKey, 2);
        var replayed = service.Seek(GalaxyKey, 1).State!;
        Assert.Contains(replayed.Flags, f => f.Name == "Score" && f.Value == 7);
    }

    [Fact]
    public void State_ListsWhatEachBattleCanWrite_ForThePortalsPicks()
    {
        var (service, _) = BuildService();

        var state = service.Start(GalaxyKey).State!;

        var m2 = state.Battles.Single(b => b.Key == M2Key);
        Assert.Equal([("F", 1), ("W", 1)], m2.Writes.Select(w => (w.Name, w.Value)).Order());
        Assert.Empty(state.Battles.Single(b => b.Key == M5Key).Writes);
    }

    // Measured: LINK_TACTICAL from the galaxy brings up the pending-battle choice and pauses
    // gameplay; the galactic story is still serviced while the choice waits; a second link while
    // one is pending queues nothing.
    [Fact]
    public void Start_Galactic_TheFirstLinkedBattleIsPending_TheSecondLinkQueuesNothing()
    {
        var (service, _) = BuildService();

        var state = service.Start(GalaxyKey).State!;

        Assert.Equal("pending", state.Battles.Single(b => b.Key == M2Key).Status);
        Assert.Equal("notStarted", state.Battles.Single(b => b.Key == M5Key).Status);
        // Pending is not frozen: the galaxy still takes ticks.
        Assert.Null(state.PausedFor);
        Assert.Null(service.Tick(GalaxyKey, 1).Error);
    }

    [Fact]
    public void ChoosingToFight_FreezesTheGalaxy_UntilTheBattleIsPlayedAndResolved()
    {
        var (service, _) = BuildService();
        service.Start(GalaxyKey);

        var chosen = service.ApplyWorldChange(GalaxyKey,
            new StorySimWorldChangeDto(StoryWorldChangeKind.ClickGui) { Name = "choice_button_left" }).State!;

        Assert.Equal("fight", chosen.Battles.Single(b => b.Key == M2Key).Status);
        Assert.Equal("Story_Plots_M2_Land", chosen.PausedFor);
        Assert.Contains("paused", service.Tick(GalaxyKey, 1).Error, StringComparison.OrdinalIgnoreCase);

        Assert.Null(service.Start(M2Session).Error);
        Assert.Equal("running", service.GetState(GalaxyKey).State!.Battles.Single(b => b.Key == M2Key).Status);

        var resolved = service.ResolveBattle(M2Session, M2Key, true).State!;
        var galaxy = service.GetState(GalaxyKey).State!;
        Assert.Equal("won", resolved.Outcome);
        Assert.Null(galaxy.PausedFor);
        Assert.Equal("won", galaxy.Battles.Single(b => b.Key == M2Key).Status);
        Assert.Null(service.Tick(GalaxyKey, 1).Error);
    }

    [Fact]
    public void ChoosingAutoResolve_LeavesTheOutcomeToThePicker_AndKeepsServicingTheGalaxy()
    {
        var (service, _) = BuildService();
        service.Start(GalaxyKey);

        var chosen = service.ApplyWorldChange(GalaxyKey,
            new StorySimWorldChangeDto(StoryWorldChangeKind.ClickGui) { Name = "choice_button_right" }).State!;

        Assert.Equal("autoResolve", chosen.Battles.Single(b => b.Key == M2Key).Status);
        Assert.Null(chosen.PausedFor);
        Assert.Null(service.Tick(GalaxyKey, 1).Error);

        var galaxy = service.ResolveBattle(GalaxyKey, M2Key, false).State!;

        Assert.Equal("lost", galaxy.Battles.Single(b => b.Key == M2Key).Status);
        Assert.Null(galaxy.PausedFor);
    }

    [Fact]
    public void ResolveBattle_AfterTheMissionIsLinkedAgain_TakesTheNewAttempt()
    {
        var (service, _) = BuildService();
        service.Start(GalaxyKey);
        Assert.Equal("lost",
            service.ResolveBattle(GalaxyKey, M2Key, false).State!.Battles.Single(b => b.Key == M2Key).Status);

        // The failure branch links the same mission once more: pending again, no outcome on the books.
        var relinked = service.ApplyWorldChange(GalaxyKey,
            new StorySimWorldChangeDto(StoryWorldChangeKind.Generic) { Name = "again" }).State!;
        Assert.Equal("pending", relinked.Battles.Single(b => b.Key == M2Key).Status);

        var won = service.ResolveBattle(GalaxyKey, M2Key, true).State!;
        Assert.Equal("won", won.Battles.Single(b => b.Key == M2Key).Status);
    }

    [Fact]
    public void Start_CarriesTheMediaOption_ToTheSession()
    {
        var (service, _) = BuildService();

        var (state, error) = service.Start(GalaxyKey, new StorySimOptions(AssumeMediaCompletes: false));

        Assert.Null(error);
        Assert.False(state!.AssumeMediaCompletes);
        Assert.True(service.Start(GalaxyKey).State!.AssumeMediaCompletes);
    }

    [Fact]
    public void ApplyWorldChange_BattleOutcome_InsideABattle_ResolvesIt()
    {
        var (service, _) = BuildService();
        service.Start(GalaxyKey);
        service.Start(M2Session);

        var (battle, error) = service.ApplyWorldChange(M2Session, new StorySimWorldChangeDto("battleWon"));

        Assert.Null(error);
        Assert.Equal("won", battle!.Outcome);
        var galaxy = service.GetState(GalaxyKey).State!;
        Assert.Null(galaxy.PausedFor);
        Assert.Equal("Fired", LifecycleOf(galaxy, Galaxy, "Win"));
    }

    [Fact]
    public void Stop_Galactic_TakesItsBattlesDown_AndStopBattle_ResumesTheGalaxy()
    {
        var (service, _) = BuildService();
        service.Start(GalaxyKey);
        service.Start(M2Session);

        service.Stop(M2Session);
        Assert.Null(service.GetState(GalaxyKey).State!.PausedFor);
        Assert.False(service.GetState(M2Session).State!.Running);

        service.Start(M2Session);
        service.Stop(GalaxyKey);
        Assert.False(service.GetState(M2Session).State!.Running);
    }
}
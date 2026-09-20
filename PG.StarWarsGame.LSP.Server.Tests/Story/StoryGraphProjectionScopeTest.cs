// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Server.Story;
using PG.StarWarsGame.LSP.Story.Graph;

namespace PG.StarWarsGame.LSP.Server.Tests.Story;

/// <summary>
///     The view filters run INSIDE a scope. The reachable-from filter walks the scoped graph, so
///     on the galactic level a battle counts as reached through its stub when the event that
///     links it in is, and inside a battle the walk starts from the portal that stands for that
///     event and ends at the portal for the outcome listener.
/// </summary>
public sealed class StoryGraphProjectionScopeTest
{
    private static ILspConfigurationProvider Config()
    {
        return FakeLspConfigurationProvider.WithFeatures(new FeatureFlags
        {
            Tools = new ToolsFeatureFlags { StoryEditor = true },
            Story = new StoryFeatureFlags { Discovery = true }
        });
    }

    private static GetStoryGraphHandler Handler()
    {
        return new GetStoryGraphHandler(new StoryBattleFixture.ModelService(), Config());
    }

    [Fact]
    public async Task ReachableFrom_InTheGalaxy_ReachesTheBattleStub_ButNotTheOtherBattle()
    {
        var result = await Handler().Handle(
            new GetStoryGraphParams("GC", "Empire",
                ReachableFrom: StoryBattleFixture.Id(StoryBattleFixture.Galaxy, "E")),
            CancellationToken.None);

        var kinds = result.Nodes.ToDictionary(n => n.Id, n => n.Kind, StringComparer.Ordinal);
        // Reader is downstream THROUGH the battle: M2 writes the flag it reads, and that write is
        // folded onto the stub - the walk goes in at the stub and out along the flag edge.
        Assert.Equal(["E", "Reader", "Win"], result.Nodes.Where(n => n.Kind == "Event").Select(n => n.Label).Order());
        Assert.Equal("TacticalPlot", kinds[StoryGraphScoper.TacticalNodeId(StoryBattleFixture.M2Key)]);
        Assert.DoesNotContain(StoryGraphScoper.TacticalNodeId(StoryBattleFixture.M5Key), kinds.Keys);
        // The battle's own events never appear on the galactic level, filtered or not.
        Assert.DoesNotContain(result.Nodes, n => n.Label == "Ambush");
    }

    [Fact]
    public async Task ReachableFrom_InsideABattle_WalksFromItsEntryPortal_ToItsExitPortal()
    {
        var entryPortal = StoryGraphScoper.GalacticPortalId(StoryBattleFixture.M2Key,
            StoryBattleFixture.Id(StoryBattleFixture.Galaxy, "E"));
        var exitPortal = StoryGraphScoper.GalacticPortalId(StoryBattleFixture.M2Key,
            StoryBattleFixture.Id(StoryBattleFixture.Galaxy, "Win"));

        var result = await Handler().Handle(
            new GetStoryGraphParams("GC", "Empire", ReachableFrom: entryPortal, Scope: StoryBattleFixture.M2Key),
            CancellationToken.None);

        // Outcome has no prerequisite, so it is a root the entry portal reaches like the ambush.
        Assert.Equal(["Ambush", "Outcome", "Reinforcements"],
            result.Nodes.Where(n => n.Kind == "Event").Select(n => n.Label).Order());
        var portals = result.Nodes.Where(n => n.Kind == "GalacticPortal").Select(n => n.Id).ToList();
        Assert.Contains(entryPortal, portals);
        Assert.Contains(exitPortal, portals);
        // Upstream from the exit portal is the way back to the entry: the portals frame the battle.
        var upstream = await Handler().Handle(
            new GetStoryGraphParams("GC", "Empire", ReachableFrom: exitPortal, ReachableDirection: "Upstream",
                Scope: StoryBattleFixture.M2Key),
            CancellationToken.None);
        Assert.Contains(upstream.Nodes, n => n.Id == entryPortal);
    }
}
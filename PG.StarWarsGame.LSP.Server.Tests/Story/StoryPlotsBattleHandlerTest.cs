// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Server.Story;

namespace PG.StarWarsGame.LSP.Server.Tests.Story;

/// <summary>
///     The plots feed resolves a manifest entry to its document through the resolution the model
///     was built from, so casing never decides whether a file is found; and a battle's plot files
///     travel with the battle, since the faction manifest never lists them.
/// </summary>
public sealed class StoryPlotsBattleHandlerTest
{
    private static ILspConfigurationProvider Config()
    {
        return FakeLspConfigurationProvider.WithFeatures(new FeatureFlags
        {
            Tools = new ToolsFeatureFlags { StoryEditor = true },
            Story = new StoryFeatureFlags { Discovery = true }
        });
    }

    private static async Task<StoryFactionDto> Faction()
    {
        var result = await new GetStoryPlotsHandler(
                new StoryBattleFixture.ModelService(), new StoryBattleFixture.IndexService(), Config())
            .Handle(new GetStoryPlotsParams(), CancellationToken.None);
        Assert.Null(result.Error);
        return Assert.Single(Assert.Single(result.Campaigns).Factions);
    }

    [Fact]
    public async Task Threads_ResolveThroughTheModel_WhateverTheManifestsCasing()
    {
        // The manifest writes STORY_CAMPAIGN.XML, the file on disk is Story_Campaign.xml: the same
        // file to the engine, and the model already resolved it when it was assembled.
        var faction = await Faction();

        var thread = Assert.Single(faction.Threads);
        Assert.Equal(StoryBattleFixture.GalaxyManifestEntry, thread.File);
        Assert.Equal(StoryBattleFixture.Galaxy, thread.Uri);
    }

    [Fact]
    public async Task Battles_CarryTheirPlotFiles_WithNameAndUri()
    {
        var faction = await Faction();

        var m2 = Assert.Single(faction.Battles!, b => b.Key == StoryBattleFixture.M2Key);
        var file = Assert.Single(m2.Threads);
        Assert.Equal(StoryBattleFixture.M2ManifestEntry, file.File);
        Assert.Equal(StoryBattleFixture.Battle, file.Uri);
        Assert.False(file.Suspended);
        // The faction's own list stays the galactic manifest's: a battle's files are not repeated there.
        Assert.DoesNotContain(faction.Threads, t => t.Uri == StoryBattleFixture.Battle);
    }
}

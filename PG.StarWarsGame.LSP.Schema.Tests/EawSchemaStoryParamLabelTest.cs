// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Schema.Tests;

/// <summary>
///     Every story param carries a label - one to three words naming what the slot holds - so the
///     graph never falls back to "Param 3" and a diagnostic can quote the slot by name. A new param
///     cannot ship without one. The last unnamed slots were named from the engine on 2026-09-21:
///     each event type's class reads its slots in Set_Param, and the factory says which class an
///     event is built as - the bounce and destroyed events are StoryEventEnterClass with
///     STORY_ENTER's seven slots, the count events share one class whose third slot is the filter.
/// </summary>
public sealed class EawSchemaStoryParamLabelTest
{
    [Fact]
    public void EveryEventParam_HasALabel()
    {
        var missing = StoryEventSchema.AllEvents()
            .SelectMany(v => (v.Params ?? []).Select(p => (Slot: $"{v.Name}[{p.Position}]", p.Label)))
            .Where(x => !x.Label.TryGetValue("en", out var label) || string.IsNullOrWhiteSpace(label))
            .Select(x => x.Slot)
            .ToList();

        Assert.True(missing.Count == 0, "Event params without a label: " + string.Join(", ", missing));
    }

    [Fact]
    public void EveryRewardParam_HasALabel()
    {
        var missing = StoryEventSchema.AllRewards()
            .SelectMany(v => (v.Params ?? []).Select(p => (Slot: $"{v.Name}[{p.Position}]", p.Label)))
            .Where(x => !x.Label.TryGetValue("en", out var label) || string.IsNullOrWhiteSpace(label))
            .Select(x => x.Slot)
            .ToList();

        Assert.True(missing.Count == 0, "Reward params without a label: " + string.Join(", ", missing));
    }

    // A label names the thing; the sentence, the legend and the ranges stay in the description.
    [Fact]
    public void ALabel_IsShort_AndCarriesNoLegendOrRange()
    {
        var offenders = StoryEventSchema.AllEvents().Concat(StoryEventSchema.AllRewards())
            .SelectMany(v => (v.Params ?? []).Select(p => (Slot: $"{v.Name}[{p.Position}]", p.Label)))
            .Where(x => x.Label.TryGetValue("en", out var label)
                        && (label.Length > 24 || label.Contains('=') || label.Contains('(') || label.EndsWith('.')
                            || label.Split(' ').Length > 3))
            .Select(x => $"{x.Slot} '{x.Label["en"]}'")
            .ToList();

        Assert.True(offenders.Count == 0, "Labels that are not a short name: " + string.Join(", ", offenders));
    }

    // The bounce and destroyed events are built as the STORY_ENTER class, so their seven slots are
    // STORY_ENTER's seven and read the same. Measured in the factory and Set_Param.
    [Theory]
    [InlineData("STORY_LAND_ON")]
    [InlineData("STORY_PLANET_DESTROYED")]
    [InlineData("STORY_FLEET_BOUNCED")]
    [InlineData("STORY_INVASION_BOUNCED")]
    public void TheEnterFamily_SharesStoryEntersSlotNames(string eventName)
    {
        var enter = StoryEventSchema.Value("STORY_ENTER").Params!;
        var value = StoryEventSchema.Value(eventName).Params!;
        for (var position = 1; position <= 6; position++)
        {
            var expected = enter.Single(p => p.Position == position).Label["en"];
            Assert.Equal(expected, value.Single(p => p.Position == position).Label["en"]);
        }
    }
}
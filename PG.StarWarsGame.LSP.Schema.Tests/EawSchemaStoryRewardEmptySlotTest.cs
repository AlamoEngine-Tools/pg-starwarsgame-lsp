// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Schema.Tests;

/// <summary>
///     Which reward slots the engine takes empty, measured in each reward's handler and against
///     the shipped corpus (2026-09-21). A slot is declared required only where the handler refuses
///     or does nothing without it; where the handler substitutes a value, the slot is optional and
///     the description says what an empty slot means.
/// </summary>
/// <remarks>
///     <para>
///         Every slot below is one vanilla leaves empty somewhere, so each rule is loud. Measured
///         empty uses, eaw + foc: HIDE_TUTORIAL_CURSOR 42 + 93 (the handler hides the flash shown
///         without an identifier), ENABLE_OBJECTIVE_DISPLAY 7 + 7 and ENABLE_CAMPAIGN_VICTORY_MOVIE
///         0 + 1 (empty reads as on), CREDITS 7 + 7 (empty adds nothing), SHOW_SPECIAL_SLOT slot 3
///         0 + 10 (empty keeps the planet).
///     </para>
///     <para>
///         The required ones fail in the game on the same corpus: ADD_OBJECTIVE, OBJECTIVE_COMPLETE
///         and OBJECTIVE_FAILED log an incomplete-parameters error; PLANET_FACTION,
///         SET_PLANET_RESTRICTED, SET_PLANET_SPAWN and SHOW_SPECIAL_SLOT the same for their leading
///         slots; TRIGGER_AI, REVEAL_PLANET and DISABLE_RETREAT return without a word; UNIQUE_UNIT
///         asserts. Those warnings point at real dead rewards and stay.
///     </para>
/// </remarks>
public sealed class EawSchemaStoryRewardEmptySlotTest
{
    [Theory]
    [InlineData("HIDE_TUTORIAL_CURSOR", 0)]
    [InlineData("CREDITS", 0)]
    [InlineData("ENABLE_OBJECTIVE_DISPLAY", 0)]
    [InlineData("ENABLE_CAMPAIGN_VICTORY_MOVIE", 0)]
    [InlineData("SHOW_SPECIAL_SLOT", 2)]
    public void SlotTheEngineTakesEmpty_IsOptional(string reward, int position)
    {
        var slot = StoryEventSchema.Reward(reward).Params!.Single(p => p.Position == position);
        Assert.True(slot.Optional,
            $"{reward} Reward_Param{position + 1} is declared required, but the engine substitutes a value " +
            "for an empty slot - so the warning fires on data the game runs.");
    }

    [Theory]
    [InlineData("ADD_OBJECTIVE", 0)]
    [InlineData("OBJECTIVE_COMPLETE", 0)]
    [InlineData("OBJECTIVE_FAILED", 0)]
    [InlineData("PLANET_FACTION", 0)]
    [InlineData("PLANET_FACTION", 1)]
    [InlineData("TRIGGER_AI", 0)]
    [InlineData("TRIGGER_AI", 1)]
    [InlineData("UNIQUE_UNIT", 0)]
    [InlineData("REVEAL_PLANET", 0)]
    [InlineData("REVEAL_PLANET", 1)]
    [InlineData("SET_PLANET_RESTRICTED", 0)]
    [InlineData("SET_PLANET_RESTRICTED", 1)]
    [InlineData("SET_PLANET_SPAWN", 0)]
    [InlineData("SET_PLANET_SPAWN", 1)]
    [InlineData("SET_PLANET_SPAWN", 2)]
    [InlineData("DISABLE_RETREAT", 0)]
    [InlineData("DISABLE_RETREAT", 1)]
    [InlineData("SHOW_SPECIAL_SLOT", 0)]
    [InlineData("SHOW_SPECIAL_SLOT", 1)]
    public void SlotTheEngineRefusesEmpty_StaysRequired(string reward, int position)
    {
        var slot = StoryEventSchema.Reward(reward).Params!.Single(p => p.Position == position);
        Assert.False(slot.Optional,
            $"{reward} Reward_Param{position + 1} is declared optional, but the engine's handler refuses or " +
            "does nothing without it - the warning names a dead reward.");
    }
}

// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Schema.Tests;

/// <summary>
///     Which event slots the engine takes empty, measured in each event class's Set_Param and
///     Evaluate_Event and against the shipped corpus (2026-09-21). Companion to the reward table in
///     <see cref="EawSchemaStoryRewardEmptySlotTest" />.
/// </summary>
/// <remarks>
///     <para>
///         Optional: STORY_ELAPSED's time parses as 0 when empty and 0 elapsed passes at the first
///         evaluation (4 + 4 vanilla uses); STORY_CONSTRUCT_LEVEL's level parses as 0 and any base
///         level is at least 0 (1 + 1).
///     </para>
///     <para>
///         Required, because the evaluator loops over the list and an empty list never matches:
///         the object list of the count events (STORY_CONQUER 10 + 10 dead vanilla uses,
///         STORY_CONSTRUCT 2 + 2), of STORY_DESTROY (1 + 1) and STORY_TACTICAL_DESTROY (7 + 7), the
///         planet list of STORY_MOVE (2 + 2) and STORY_CONSTRUCT_LEVEL (1 + 1), both lists of
///         STORY_CAPTURE_STRUCTURE (1 + 1); STORY_VICTORY's faction, which the evaluator refuses
///         when unresolved (4 + 4).
///     </para>
/// </remarks>
public sealed class EawSchemaStoryEventEmptySlotTest
{
    [Theory]
    [InlineData("STORY_ELAPSED", 0)]
    [InlineData("STORY_CONSTRUCT_LEVEL", 1)]
    public void SlotTheEngineTakesEmpty_IsOptional(string eventName, int position)
    {
        var slot = StoryEventSchema.Value(eventName).Params!.Single(p => p.Position == position);
        Assert.True(slot.Optional,
            $"{eventName} Event_Param{position + 1} is declared required, but an empty slot parses as 0 " +
            "and the evaluator passes it - so the warning fires on data the game runs.");
    }

    [Theory]
    [InlineData("STORY_CONQUER", 0)]
    [InlineData("STORY_CONSTRUCT", 0)]
    [InlineData("STORY_DESTROY", 0)]
    [InlineData("STORY_TACTICAL_DESTROY", 0)]
    [InlineData("STORY_MOVE", 1)]
    [InlineData("STORY_CONSTRUCT_LEVEL", 0)]
    [InlineData("STORY_CAPTURE_STRUCTURE", 0)]
    [InlineData("STORY_CAPTURE_STRUCTURE", 1)]
    [InlineData("STORY_VICTORY", 0)]
    public void SlotWhoseEmptyListNeverMatches_StaysRequired(string eventName, int position)
    {
        var slot = StoryEventSchema.Value(eventName).Params!.Single(p => p.Position == position);
        Assert.False(slot.Optional,
            $"{eventName} Event_Param{position + 1} is declared optional, but the evaluator loops over it " +
            "and an empty list never matches - the warning names a listener that can never fire.");
    }
}

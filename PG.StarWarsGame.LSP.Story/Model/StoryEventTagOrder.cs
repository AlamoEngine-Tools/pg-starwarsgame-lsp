// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Story.Model;

/// <summary>
///     The event block child-tag order of the scripting document ("Story Mode and Tutorial
///     Scripting System"), which the canonical writer follows when it inserts a tag. It is a
///     tidiness convention, not a rule the game applies: the game reads each tag by name into its
///     own slot and builds the event from the slots afterwards, so a file in any order runs the
///     same, and hundreds of the shipped events are in another. Tags not listed carry no rank.
///     Param slots share their prefix's rank.
/// </summary>
public static class StoryEventTagOrder
{
    public static readonly IReadOnlyList<string> Canonical =
    [
        "EVENT_TYPE", "EVENT_PARAM", "EVENT_FILTER", "REWARD_TYPE", "REWARD_PARAM", "PREREQ",
        "STORY_DIALOG", "STORY_CHAPTER", "STORY_TAG", "STORY_VAR", "BRANCH", "PERPETUAL",
        "MULTIPLAYER", "STORY_DIALOG_POPUP"
    ];

    /// <summary>The tag's canonical rank, or <see langword="null" /> when unconstrained.</summary>
    public static int? RankOf(string tagName)
    {
        var upper = tagName.ToUpperInvariant();
        for (var i = 0; i < Canonical.Count; i++)
        {
            var entry = Canonical[i];
            var matches = entry is "EVENT_PARAM" or "REWARD_PARAM"
                ? upper.StartsWith(entry, StringComparison.Ordinal)
                : upper == entry;
            if (matches) return i;
        }

        return null;
    }
}
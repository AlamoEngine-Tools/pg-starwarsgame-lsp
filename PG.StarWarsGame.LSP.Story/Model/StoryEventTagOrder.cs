// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Story.Model;

/// <summary>
///     The documented event block child-tag order ("Story Mode and Tutorial Scripting System").
///     Tags not listed carry no order constraint. Param slots share their prefix's rank. Shared
///     by the tag-order diagnostics and the canonical writer so both always agree.
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

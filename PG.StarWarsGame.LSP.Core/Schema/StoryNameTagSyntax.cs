// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Schema;

/// <summary>
///     Shared knowledge of how a campaign attaches a plot to a faction. There are two authoring
///     forms and the engine merges every occurrence of both into one (faction, plot) list:
///     <list type="bullet">
///         <item>
///             the faction-specific tags <c>Rebel_Story_Name</c> / <c>Empire_Story_Name</c> /
///             <c>Underworld_Story_Name</c>, whose value is a single plot file (accessors
///             <c>Get_Good/Evil/Corrupt_Story_Name</c>);
///         </item>
///         <item>
///             the generic, additive <c>Story_Name</c> tag, whose value is a flat
///             <c>Faction, PlotFile[, Faction, PlotFile ...]</c> tuple list (accessor
///             <c>Get_Faction_Story_Name</c>). Only this form can attach a non-major faction.
///         </item>
///     </list>
///     Vanilla FoC uses only the faction-specific tags; mods (e.g. EaWX) use the generic tag.
///     Which faction a faction-specific tag attaches is the side GameConstants names for it
///     (<see cref="StorySideNames" />), not the faction the tag's prefix spells (#192).
///     Lives in Core because both the story chain scanner (Story) and the campaign attachment
///     diagnostics rule (Xml) need it, and the project dependency runs Story -> Xml.
/// </summary>
public static class StoryNameTagSyntax
{
    public const string GenericTag = "Story_Name";

    private const string GoodTag = "Rebel_Story_Name";
    private const string EvilTag = "Empire_Story_Name";
    private const string CorruptTag = "Underworld_Story_Name";

    private static readonly HashSet<string> FactionTags = new(StringComparer.OrdinalIgnoreCase)
        { GoodTag, EvilTag, CorruptTag };

    /// <summary>Whether the tag attaches a plot (either authoring form).</summary>
    public static bool IsStoryNameTag(string tagName)
    {
        return FactionTags.Contains(tagName) || IsGenericTag(tagName);
    }

    public static bool IsGenericTag(string tagName)
    {
        return tagName.Equals(GenericTag, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsFactionSpecificTag(string tagName)
    {
        return FactionTags.Contains(tagName);
    }

    /// <summary>
    ///     Whether the faction has a dedicated tag with the shipped sides. The sides' factions attach
    ///     through their side's tag; every other faction must use the generic tag. Callers that know
    ///     the project's sides ask <see cref="SideTagFor" />.
    /// </summary>
    public static bool IsMajorFaction(string faction)
    {
        return SideTagFor(faction, StorySideNames.Shipped) is not null;
    }

    /// <summary>The shipped side's tag for one of the shipped sides' factions (<c>Rebel</c> -> <c>Rebel_Story_Name</c>).</summary>
    public static string FactionTagFor(string faction)
    {
        return faction + "_Story_Name";
    }

    /// <summary>
    ///     The dedicated tag that attaches a plot to <paramref name="faction" />, or null when the
    ///     faction is none of the sides and only the generic tag can attach it.
    /// </summary>
    public static string? SideTagFor(string faction, StorySideNames sides)
    {
        var named = faction.Trim();
        if (named.Equals(sides.Good, StringComparison.OrdinalIgnoreCase)) return GoodTag;
        if (named.Equals(sides.Evil, StringComparison.OrdinalIgnoreCase)) return EvilTag;
        if (named.Equals(sides.Corrupt, StringComparison.OrdinalIgnoreCase)) return CorruptTag;
        return null;
    }

    /// <summary>
    ///     The faction a faction-specific tag attaches: the side GameConstants names for it
    ///     (<c>"rebel_story_name"</c> -> the good side, <c>"Rebel"</c> with the shipped sides). Null
    ///     for any other tag.
    /// </summary>
    public static string? FactionOf(string tagName, StorySideNames? sides = null)
    {
        var names = sides ?? StorySideNames.Shipped;
        if (tagName.Equals(GoodTag, StringComparison.OrdinalIgnoreCase)) return names.Good;
        if (tagName.Equals(EvilTag, StringComparison.OrdinalIgnoreCase)) return names.Evil;
        if (tagName.Equals(CorruptTag, StringComparison.OrdinalIgnoreCase)) return names.Corrupt;
        return null;
    }

    /// <summary>
    ///     A faction-specific tag's name in the schema's casing (<c>"rebel_story_name"</c> ->
    ///     <c>"Rebel_Story_Name"</c>), for messages that quote the tag as authored. Null for any other tag.
    /// </summary>
    public static string? CanonicalFactionTag(string tagName)
    {
        if (tagName.Equals(GoodTag, StringComparison.OrdinalIgnoreCase)) return GoodTag;
        if (tagName.Equals(EvilTag, StringComparison.OrdinalIgnoreCase)) return EvilTag;
        if (tagName.Equals(CorruptTag, StringComparison.OrdinalIgnoreCase)) return CorruptTag;
        return null;
    }

    /// <summary>
    ///     The (faction, plot file) pairs a story-name tag contributes, in document order. Values
    ///     are returned as written (no path normalization); a dangling token - the trailing comma
    ///     the real data ships, or an odd faction with no plot - is ignored. A faction-specific tag
    ///     attaches the side <paramref name="sides" /> names for it, the shipped sides when none are given.
    /// </summary>
    public static IEnumerable<(string Faction, string PlotFile)> ReadPairs(string tagName, string rawValue,
        StorySideNames? sides = null)
    {
        var value = rawValue.Trim();
        if (value.Length == 0) yield break;

        if (IsGenericTag(tagName))
        {
            var tokens = value.Split(',',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            for (var i = 0; i + 1 < tokens.Length; i += 2)
                yield return (tokens[i], tokens[i + 1]);
            yield break;
        }

        // "Rebel_Story_Name" -> the good side's faction; the value is the plot file.
        if (FactionOf(tagName, sides) is { } faction) yield return (faction, value);
    }
}
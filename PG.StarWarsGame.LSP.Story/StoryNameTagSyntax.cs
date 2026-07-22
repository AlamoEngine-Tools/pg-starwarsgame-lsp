// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using HtmlAgilityPack;

namespace PG.StarWarsGame.LSP.Story;

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
/// </summary>
public static class StoryNameTagSyntax
{
    public const string GenericTag = "Story_Name";

    private static readonly HashSet<string> FactionTags = new(StringComparer.OrdinalIgnoreCase)
        { "Rebel_Story_Name", "Empire_Story_Name", "Underworld_Story_Name" };

    private static readonly HashSet<string> MajorFactions = new(StringComparer.OrdinalIgnoreCase)
        { "Rebel", "Empire", "Underworld" };

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
    ///     Whether the faction has a dedicated tag. Major factions attach through
    ///     <c>{Faction}_Story_Name</c>; every other faction must use the generic tag.
    /// </summary>
    public static bool IsMajorFaction(string faction)
    {
        return MajorFactions.Contains(faction);
    }

    public static string FactionTagFor(string faction)
    {
        return faction + "_Story_Name";
    }

    /// <summary>
    ///     The (faction, plot file) pairs a story-name tag contributes, in document order. Values
    ///     are returned as written (no path normalization); a dangling token - the trailing comma
    ///     the real data ships, or an odd faction with no plot - is ignored.
    /// </summary>
    public static IEnumerable<(string Faction, string PlotFile)> ReadPairs(HtmlNode node)
    {
        var value = node.InnerText.Trim();
        if (value.Length == 0) yield break;

        if (IsGenericTag(node.Name))
        {
            var tokens = value.Split(',',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            for (var i = 0; i + 1 < tokens.Length; i += 2)
                yield return (tokens[i], tokens[i + 1]);
            yield break;
        }

        // "Rebel_Story_Name" → faction "Rebel"; the value is the plot file.
        var prefix = node.Name[..node.Name.IndexOf('_')];
        yield return (char.ToUpperInvariant(prefix[0]) + prefix[1..], value);
    }
}

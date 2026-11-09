// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Schema;

/// <summary>
///     The factions GameConstants names as the three sides, which the dedicated campaign story tags
///     attach their plots to.
/// </summary>
/// <remarks>
///     <para>
///         MEASURED on the 2024 64-bit build, in the routine that starts a campaign: a
///         <c>Rebel_Story_Name</c> loads its plots for the player of the faction
///         <c>Good_Side_Name</c> names, <c>Empire_Story_Name</c> for <c>Evil_Side_Name</c>'s and
///         <c>Underworld_Story_Name</c> for <c>Corrupt_Side_Name</c>'s. The tag's prefix names the
///         SLOT; a mod that renames its good side to Republic runs Rebel_Story_Name for Republic (#192).
///     </para>
///     <para>
///         The shipped GameConstants spell the sides REBEL, EMPIRE and UNDERWORLD while the schema and
///         every label spell them Rebel, Empire and Underworld; <see cref="From" /> keeps the schema's
///         casing for a side that is the shipped one, so vanilla output stays as it was, and takes a
///         renamed side as written.
///     </para>
/// </remarks>
public sealed record StorySideNames(string Good, string Evil, string Corrupt)
{
    public const string GoodTag = "Good_Side_Name";
    public const string EvilTag = "Evil_Side_Name";
    public const string CorruptTag = "Corrupt_Side_Name";

    /// <summary>The shipped games' sides, in the schema's casing; what applies when GameConstants names none.</summary>
    public static StorySideNames Shipped { get; } = new("Rebel", "Empire", "Underworld");

    /// <summary>The sides the effective GameConstants names, from its tag values by tag name.</summary>
    public static StorySideNames From(IReadOnlyDictionary<string, string> gameConstantsTags)
    {
        return new StorySideNames(
            Side(gameConstantsTags, GoodTag, Shipped.Good),
            Side(gameConstantsTags, EvilTag, Shipped.Evil),
            Side(gameConstantsTags, CorruptTag, Shipped.Corrupt));
    }

    private static string Side(IReadOnlyDictionary<string, string> tags, string tag, string shipped)
    {
        if (!tags.TryGetValue(tag, out var value)) return shipped;
        var named = value.Trim();
        return named.Length == 0 || named.Equals(shipped, StringComparison.OrdinalIgnoreCase) ? shipped : named;
    }
}

/// <summary>
///     Where the sides of the project in front come from: its effective GameConstants, a mod's own
///     over the game's. Shared so the story chain scan, the campaign attachment check and the manifest
///     writer read one answer.
/// </summary>
public interface IStorySideNamesSource
{
    StorySideNames Current { get; }
}
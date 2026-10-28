// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Schema;

/// <summary>
///     The tag values the game maps nothing from.
/// </summary>
/// <remarks>
///     Measured in the game's object parser: an empty value makes the shared field mapper return
///     before any conversion, and a value of exactly <c>TBD</c> is withheld by the parser itself
///     with a debug warning. A tag carrying either writes no field, so the earlier occurrence - or
///     the value inherited from a variant base - stays; it clears nothing. Han_Solo names its icon
///     and then repeats <c>Icon_Name</c> empty, and the game shows the icon.
///     <para>
///         That is the path of a plain tag, whose value is its text. A tag with element children -
///         a sub-object list such as <c>Unit_Abilities_Data</c>, or a composite the engine reads with
///         a parser of its own - has no text value to map and is outside the measurement, so
///         <see cref="WritesNothing" /> leaves it alone.
///     </para>
/// </remarks>
public static class EngineTagValue
{
    /// <summary>The one spelled-out value the parser withholds, compared byte for byte.</summary>
    public const string Placeholder = "TBD";

    /// <summary>Whether the game maps nothing from this value: blank, or exactly <see cref="Placeholder" />.</summary>
    public static bool IsWithheld(string? value)
    {
        var trimmed = value?.Trim();

        return string.IsNullOrEmpty(trimmed) || trimmed.Equals(Placeholder, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Whether a tag occurrence writes no field: a plain tag - no element children in its outer
    ///     XML - whose value is withheld.
    /// </summary>
    public static bool WritesNothing(string? value, string fragment)
    {
        return IsWithheld(value) && !HasElementChild(fragment);
    }

    /// <summary>An element start after the opening tag; <c>&lt;/</c> closes and <c>&lt;!</c> comments.</summary>
    private static bool HasElementChild(string fragment)
    {
        var openingEnd = fragment.IndexOf('>');
        if (openingEnd <= 0 || fragment[openingEnd - 1] == '/')
            return false;

        for (var i = fragment.IndexOf('<', openingEnd + 1); i >= 0; i = fragment.IndexOf('<', i + 1))
            if (i + 1 < fragment.Length && char.IsLetter(fragment[i + 1]))
                return true;

        return false;
    }
}
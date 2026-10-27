// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Globalization;
using System.Text.RegularExpressions;

namespace PG.StarWarsGame.LSP.Core.Schema;

/// <summary>
///     A number as a tag value writes it, read two ways: the well-formed number a validator asks
///     for, and the number the game takes. One rule for every validator, cross-tag rule and the
///     preview, where each used to carry its own copy - one stripped a single <c>f</c> suffix, the
///     next stripped any run of them, and the scene builder stripped none and lost every spin time
///     written <c>2.0f</c> to its default.
/// </summary>
/// <remarks>
///     The game converts a float field with the C runtime's <c>atof</c>, and an integer with
///     <c>atoi</c>: leading blanks are skipped, the longest run that forms a number is converted, and
///     whatever follows is ignored - one trailing <c>f</c>, a run of them, a space before the
///     suffix, or any other text. A value with no number at the front reads as 0. That is what
///     <see cref="ReadFloat" /> and <see cref="ReadDouble" /> reproduce, for the preview and for the
///     rules that compare values the way the game will.
///     <para>
///         <see cref="TryParseFloat" />, <see cref="TryParseDouble" /> and <see cref="TryParseInt" />
///         are stricter on purpose: the number and at most one <c>f</c> or <c>F</c> suffix (floats
///         only), nothing else. <c>1,5</c>, <c>1.5ff</c> and <c>7abc</c> each read in the game as
///         something other than what the author meant, and a validator built on this reading says
///         so, where the game is silent.
///     </para>
/// </remarks>
public static class EngineNumber
{
    /// <summary>What <c>atof</c> consumes: an optional sign, digits with an optional point, an optional exponent.</summary>
    private static readonly Regex Prefix = new(
        @"^[+-]?(?:\d+\.?\d*|\.\d+)(?:[eE][+-]?\d+)?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>What <c>atoi</c> consumes: an optional sign and the digits, so <c>1.0</c> and <c>1e3</c> both read 1.</summary>
    private static readonly Regex IntegerPrefix = new(
        @"^[+-]?\d+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    ///     A sign, a point and an exponent, but no blanks: the text is trimmed before the suffix comes
    ///     off, so a blank left over sat between the number and its suffix.
    /// </summary>
    private const NumberStyles WellFormed =
        NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent;

    /// <summary>Whether the text is a well-formed float: the number and at most one <c>f</c>/<c>F</c>.</summary>
    public static bool TryParseFloat(string? text, out float value)
    {
        return float.TryParse(WithoutSuffix(text), WellFormed, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>Whether the text is a well-formed float, read at double precision.</summary>
    public static bool TryParseDouble(string? text, out double value)
    {
        return double.TryParse(WithoutSuffix(text), WellFormed, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>Whether the text is a whole number: digits and an optional sign, no fraction, suffix or exponent.</summary>
    public static bool TryParseInt(string? text, out int value)
    {
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>
    ///     The number the game reads from the text, or null when no number starts it - where the game
    ///     reads 0, which the caller tells apart from a written zero.
    /// </summary>
    public static float? ReadFloat(string? text)
    {
        return NumberPrefix(text) is { } prefix &&
               float.TryParse(prefix, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    /// <summary>The number the game reads, at double precision; see <see cref="ReadFloat" />.</summary>
    public static double? ReadDouble(string? text)
    {
        return NumberPrefix(text) is { } prefix &&
               double.TryParse(prefix, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    /// <summary>
    ///     The whole number the game reads from the text, or null when no digit starts it - where the
    ///     game reads 0. A fraction, a suffix or an exponent after the digits is ignored, so a count
    ///     written <c>1.0</c>, as the shipped files write some, reads 1.
    /// </summary>
    public static int? ReadInt(string? text)
    {
        if (text is null)
            return null;

        var match = IntegerPrefix.Match(text.TrimStart());

        return match.Success &&
               int.TryParse(match.Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static string? WithoutSuffix(string? text)
    {
        if (text is null)
            return null;

        var trimmed = text.Trim();

        return trimmed.Length > 0 && trimmed[^1] is 'f' or 'F' ? trimmed[..^1] : trimmed;
    }

    private static string? NumberPrefix(string? text)
    {
        if (text is null)
            return null;

        var match = Prefix.Match(text.TrimStart());

        return match.Success ? match.Value : null;
    }
}
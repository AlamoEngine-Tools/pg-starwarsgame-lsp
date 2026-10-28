// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Globalization;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Preview;

namespace PG.StarWarsGame.LSP.Preview.Sections;

/// <summary>
///     How the preview reads a tag's written value: the first token of a list, a category mask, a
///     colour, a vector, a list of numbers. Pure functions shared by the section builders (#314).
/// </summary>
public static class PreviewTagReader
{
    /// <summary>The three tags an object lists its behaviours under, as the engine's parser reads them.</summary>
    public static readonly string[] BehaviorListTags = ["Behavior", "SpaceBehavior", "LandBehavior"];

    /// <summary>The first entry of a comma-separated list, or null when there is none.</summary>
    public static string? FirstToken(string? value)
    {
        return (value ?? string.Empty)
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
    }

    /// <summary>
    ///     One category per entry, out of a mask that names several.
    /// </summary>
    /// <remarks>
    ///     <c>CategoryMask</c> is written <c>Vehicle | AntiInfantry | AntiVehicle</c>, so it is not
    ///     a value that can be looked up - it is a set that has to be tested against. Splitting it
    ///     server-side keeps the client from having to know the engine's punctuation.
    /// </remarks>
    public static List<string> CategoryTokens(string? mask)
    {
        if (string.IsNullOrWhiteSpace(mask))
            return [];

        return
        [
            .. mask
                .Split(['|', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(token => token.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
        ];
    }

    public static PreviewRgba? Colour(EffectiveObject effective, string tagName)
    {
        if (effective.ValueOf(tagName) is not { } raw)
            return null;

        var tokens = raw.Split([' ', '\t', ',', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 3)
            return null;

        var channels = new int[4];
        channels[3] = 255;

        for (var i = 0; i < Math.Min(4, tokens.Length); i++)
        {
            if (!int.TryParse(tokens[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
                return null;
            channels[i] = Math.Clamp(v, 0, 255);
        }

        return new PreviewRgba(channels[0], channels[1], channels[2], channels[3]);
    }

    /// <summary>A comma-separated triple, or null when the tag is absent or malformed.</summary>
    public static PreviewVector3? Vector3Tag(EffectiveObject effective, string tagName)
    {
        var raw = effective.ValueOf(tagName);
        if (string.IsNullOrEmpty(raw))
            return null;

        var parts = raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3)
            return null;

        return float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) &&
               float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) &&
               float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var z)
            ? new PreviewVector3(x, y, z)
            : null;
    }

    /// <summary>
    ///     Every entry of a comma-separated list that parses, in written order.
    /// </summary>
    /// <remarks>
    ///     A non-numeric entry is DROPPED rather than skipped-with-a-hole, which is the same rule
    ///     <see cref="DamageStages" /> uses. It also means a table carrying one is caught by the
    ///     length check above and discarded whole, which is the safe direction for a positional
    ///     pairing.
    /// </remarks>
    public static List<T> Numbers<T>(string? raw, ParseNumber<T> parse)
    {
        var found = new List<T>();
        if (string.IsNullOrWhiteSpace(raw))
            return found;

        foreach (var token in raw.Split(
                     ',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            if (parse(token, out var value))
                found.Add(value);

        return found;
    }

    public delegate bool ParseNumber<T>(string written, out T value);

    public static string? Blank(string value)
    {
        return value.Length == 0 ? null : value;
    }
}
// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Schema;

/// <summary>
///     The spellings the engine accepts for a <see cref="XmlValueType.Boolean" /> tag value, in one
///     place. Validation and behaviour previously each carried their own copy, so a tag the validator
///     accepted could still read as false to a rule that only knew about "Yes" - silently disabling
///     whatever that rule gated.
/// </summary>
public static class EngineBoolean
{
    private static readonly HashSet<string> True =
        new(StringComparer.OrdinalIgnoreCase) { "true", "yes", "1" };

    private static readonly HashSet<string> False =
        new(StringComparer.OrdinalIgnoreCase) { "false", "no", "0" };

    /// <summary>Every accepted spelling, for validators reporting what a value should have been.</summary>
    public static IReadOnlyCollection<string> AllValues { get; } =
        [.. True.Concat(False)];

    /// <summary>True only for an affirmative spelling; null, blank and unrecognised read as false.</summary>
    public static bool IsTrue(string? value)
    {
        return value is not null && True.Contains(value.Trim());
    }

    /// <summary>
    ///     True unless the value is an explicit denial - the reading for a flag whose default is ON.
    /// </summary>
    /// <remarks>
    ///     Which default a tag takes is not a detail for a flag the shipped data never writes down:
    ///     it IS the behaviour. <c>Engine_Death_Hide_Engine_Particles</c> is the case that produced
    ///     this - it appears in the engine's own parameter table in <c>DatabaseMapExport.xml</c> and
    ///     exactly zero times across the shipped corpus, so reading an absent tag as false meant a
    ///     destroyed engine block never put its glow out anywhere. A tag nobody writes is an
    ///     opt-OUT, not an opt-in.
    ///     <para>
    ///         An unrecognised spelling is not a denial. Whether a value is a spelling at all is a
    ///         validation question, answered by <see cref="IsValid" />; answering it here would turn
    ///         a feature off over a typo.
    ///     </para>
    /// </remarks>
    public static bool IsTrueUnlessDenied(string? value)
    {
        return value is null || !False.Contains(value.Trim());
    }

    /// <summary>Whether the value is a spelling the engine recognises at all.</summary>
    public static bool IsValid(string? value)
    {
        if (value is null) return false;
        var trimmed = value.Trim();
        return True.Contains(trimmed) || False.Contains(trimmed);
    }
}

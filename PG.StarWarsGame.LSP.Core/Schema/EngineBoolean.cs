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

    /// <summary>Whether the value is a spelling the engine recognises at all.</summary>
    public static bool IsValid(string? value)
    {
        if (value is null) return false;
        var trimmed = value.Trim();
        return True.Contains(trimmed) || False.Contains(trimmed);
    }
}

// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Globalization;

namespace PG.StarWarsGame.LSP.Core.Diagnostics;

/// <summary>
///     Stable identifier for one kind of diagnostic, rendered as <c>aetswg-000-0000</c> (see
///     issue #66): the literal prefix, a 3-digit <see cref="Group" />, and a 4-digit
///     <see cref="Number" /> within that group - always 15 characters.
///     <para>
///         These end up in suppression comments that users commit to their mods, so an id is a
///         published contract: never reuse a retired number, and never renumber an existing
///         diagnostic. Both would silence something other than what the author wrote down.
///     </para>
/// </summary>
public readonly record struct DiagnosticId
{
    private const string Prefix = "aetswg";
    private const int MaxGroup = 999;
    private const int MaxNumber = 9999;

    public DiagnosticId(int group, int number)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(group);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(group, MaxGroup);
        ArgumentOutOfRangeException.ThrowIfNegative(number);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(number, MaxNumber);

        Group = group;
        Number = number;
    }

    public DiagnosticId(DiagnosticGroup group, int number)
        : this((int)group, number)
    {
    }

    /// <summary>Coarse category, matching a <see cref="DiagnosticGroup" /> member.</summary>
    public int Group { get; }

    /// <summary>Number within the group, unique per group.</summary>
    public int Number { get; }

    public override string ToString()
    {
        return string.Create(CultureInfo.InvariantCulture, $"{Prefix}-{Group:D3}-{Number:D4}");
    }

    /// <summary>
    ///     Parses the wire format. Case-insensitive and tolerant of surrounding whitespace, because
    ///     the main source of these strings is a comment someone typed by hand; strict about the
    ///     zero padding and segment count, so a malformed id is reported rather than half-honoured.
    /// </summary>
    public static bool TryParse(string? text, out DiagnosticId id)
    {
        id = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var span = text.AsSpan().Trim();
        if (span.Length != Prefix.Length + 1 + 3 + 1 + 4) return false;
        if (!span[..Prefix.Length].Equals(Prefix, StringComparison.OrdinalIgnoreCase)) return false;
        if (span[Prefix.Length] != '-' || span[Prefix.Length + 4] != '-') return false;

        var groupSpan = span.Slice(Prefix.Length + 1, 3);
        var numberSpan = span.Slice(Prefix.Length + 5, 4);

        if (!int.TryParse(groupSpan, NumberStyles.None, CultureInfo.InvariantCulture, out var group) ||
            !int.TryParse(numberSpan, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
            return false;

        id = new DiagnosticId(group, number);
        return true;
    }
}

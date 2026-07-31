// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Globalization;

namespace PG.StarWarsGame.LSP.Core.Diagnostics.Suppression;

/// <summary>
///     What a suppression applies to: one diagnostic id (<c>aetswg-010-0001</c>) or every id in a
///     group (<c>aetswg-010-*</c>).
///     <para>
///         The group form exists because the useful bulk operation is "stop checking whether asset
///         files exist", not "silence these forty ids". It is why <see cref="DiagnosticGroup" /> is
///         a coarse axis rather than one bucket per handler.
///     </para>
/// </summary>
public readonly record struct SuppressionMatcher
{
    private SuppressionMatcher(int group, int? number)
    {
        Group = group;
        Number = number;
    }

    public int Group { get; }

    /// <summary>Null for a whole-group matcher.</summary>
    public int? Number { get; }

    public bool IsWholeGroup => Number is null;

    public static SuppressionMatcher ForId(DiagnosticId id)
    {
        return new SuppressionMatcher(id.Group, id.Number);
    }

    public static SuppressionMatcher ForGroup(DiagnosticGroup group)
    {
        return new SuppressionMatcher((int)group, null);
    }

    public bool Matches(DiagnosticId id)
    {
        return id.Group == Group && (Number is null || id.Number == Number);
    }

    /// <summary>
    ///     Parses either wire form. Anything else is rejected rather than guessed at: a typo in a
    ///     suppression must leave the diagnostic visible, not silence something unintended.
    /// </summary>
    public static bool TryParse(string? text, out SuppressionMatcher matcher)
    {
        matcher = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var span = text.Trim();
        if (DiagnosticId.TryParse(span, out var id))
        {
            matcher = ForId(id);
            return true;
        }

        // aetswg-<group>-*
        const string prefix = "aetswg-";
        if (!span.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        var rest = span[prefix.Length..];
        if (rest.Length != 5 || !rest.EndsWith("-*", StringComparison.Ordinal)) return false;
        if (!int.TryParse(rest[..3], NumberStyles.None, CultureInfo.InvariantCulture, out var group))
            return false;

        matcher = new SuppressionMatcher(group, null);
        return true;
    }

    public override string ToString()
    {
        return Number is null
            ? string.Create(CultureInfo.InvariantCulture, $"aetswg-{Group:D3}-*")
            : new DiagnosticId(Group, Number.Value).ToString();
    }
}

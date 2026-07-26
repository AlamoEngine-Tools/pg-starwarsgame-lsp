// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Diagnostics.Suppression;

/// <summary>The four scopes from issue #66, narrowest last.</summary>
public enum SuppressionScope
{
    /// <summary>Every file in the project. Stored in <c>.aetswg/suppressions.json</c>.</summary>
    Global,

    /// <summary>The file carrying the directive.</summary>
    File,

    /// <summary>The object element enclosing the directive.</summary>
    Object,

    /// <summary>The single XML node following the directive.</summary>
    Node
}

/// <summary>
///     One suppression resolved against the document that declared it: the lines it covers are
///     worked out while the DOM is in hand, so applying it later is a range check rather than
///     another tree walk.
/// </summary>
/// <param name="Matcher">Which diagnostics it silences.</param>
/// <param name="Scope">Scope it was written at - carried for reporting, not for matching.</param>
/// <param name="StartLine">First covered line, 0-based inclusive.</param>
/// <param name="EndLine">Last covered line, 0-based inclusive.</param>
/// <param name="DirectiveLine">Line the directive itself sits on, for "unused suppression" reporting.</param>
/// <param name="Reason">
///     Free text the author gave after <c>reason::</c>, or null. A directive naming several ids
///     produces one range each, all sharing the one reason it was written with.
/// </param>
public sealed record SuppressionRange(
    SuppressionMatcher Matcher,
    SuppressionScope Scope,
    int StartLine,
    int EndLine,
    int DirectiveLine,
    string? Reason = null)
{
    public bool Covers(DiagnosticId id, int line)
    {
        return line >= StartLine && line <= EndLine && Matcher.Matches(id);
    }
}

/// <summary>
///     Every suppression in force for one document: the ranges parsed out of its own comments plus
///     the project-wide matchers. Immutable, so it can be built once per parse and shared.
/// </summary>
public sealed class DocumentSuppressions
{
    public static readonly DocumentSuppressions Empty = new([], []);

    public DocumentSuppressions(
        IReadOnlyList<SuppressionRange> ranges, IReadOnlyList<SuppressionMatcher> global)
    {
        Ranges = ranges;
        Global = global;
    }

    public IReadOnlyList<SuppressionRange> Ranges { get; }

    public IReadOnlyList<SuppressionMatcher> Global { get; }

    public bool IsEmpty => Ranges.Count == 0 && Global.Count == 0;

    /// <summary>True when a diagnostic of this id, reported at this 0-based line, is silenced.</summary>
    public bool IsSuppressed(DiagnosticId id, int line)
    {
        foreach (var matcher in Global)
            if (matcher.Matches(id))
                return true;

        foreach (var range in Ranges)
            if (range.Covers(id, line))
                return true;

        return false;
    }
}

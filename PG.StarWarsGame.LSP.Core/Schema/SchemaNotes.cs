// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Schema;

/// <summary>
///     Reading a note list the few ways anything ever needs to.
///     <para>
///         Here rather than repeated at each call site: a handler asking "is this deprecated" and a
///         hover asking the same question must not be able to disagree, and the answer is a property
///         of the list, not of whoever is asking.
///     </para>
/// </summary>
public static class SchemaNotes
{
    /// <summary>Whether anything on this element says <paramref name="kind" />.</summary>
    public static bool Has(this IReadOnlyList<SchemaNote> notes, SchemaNoteKind kind)
    {
        return notes.Any(n => n.Kind == kind);
    }

    /// <summary>
    ///     The first note of <paramref name="kind" />, or null. First and not "the only one": an
    ///     element may carry two engine bugs, and the ranked list puts the one written first here.
    /// </summary>
    public static SchemaNote? First(this IReadOnlyList<SchemaNote> notes, SchemaNoteKind kind)
    {
        return notes.FirstOrDefault(n => n.Kind == kind);
    }

    /// <summary>
    ///     The text of the first note of <paramref name="kind" /> in <paramref name="locale" />, or
    ///     null when there is no such note or it carries no text in that language. Callers treat a
    ///     missing translation the same as a missing note: they say less rather than say it in a
    ///     language the reader did not ask for.
    /// </summary>
    public static string? TextFor(
        this IReadOnlyList<SchemaNote> notes, SchemaNoteKind kind, string locale)
    {
        var text = notes.First(kind)?.Text.GetValueOrDefault(locale);
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    /// <summary>The value the first note of <paramref name="kind" /> carries - a version for Since.</summary>
    public static string? ValueFor(this IReadOnlyList<SchemaNote> notes, SchemaNoteKind kind)
    {
        return notes.First(kind)?.Value;
    }
}

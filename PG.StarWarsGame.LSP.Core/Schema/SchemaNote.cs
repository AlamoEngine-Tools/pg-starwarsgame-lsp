// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Schema;

/// <summary>
///     One thing worth saying about a schema element, and what sort of thing it is.
///     <para>
///         Every element that carries a description carries a list of these. It is the schema's only
///         annotation channel: whatever is true about an element - that it is deprecated, that the
///         engine ignores it, that nobody has checked it - is recorded here and nowhere else, so
///         there is one place to write it and one place to read it.
///     </para>
/// </summary>
/// <param name="Kind">
///     What sort of note this is, and therefore how loudly it is said. See
///     <see cref="SchemaNoteKind" />; the kind also ranks the note against its siblings.
/// </param>
/// <param name="Text">
///     The note by locale, the same shape as an element's description. Empty for a kind whose whole
///     content is its <paramref name="Value" />.
/// </param>
/// <param name="Value">
///     What the kind carries besides prose - a version for <see cref="SchemaNoteKind.Since" /> -
///     or null for the kinds that carry nothing.
/// </param>
public sealed record SchemaNote(
    SchemaNoteKind Kind,
    IReadOnlyDictionary<string, string> Text,
    string? Value = null)
{
    /// <summary>
    ///     The notes in the order they should be shown: by kind rank, then as written. Ordering is
    ///     stable, so two notes of the same kind keep the order someone chose to write them in -
    ///     the one place the file's own order means anything.
    /// </summary>
    public static IReadOnlyList<SchemaNote> Ranked(IEnumerable<SchemaNote> notes)
    {
        return notes.OrderBy(n => (int)n.Kind).ToList();
    }
}

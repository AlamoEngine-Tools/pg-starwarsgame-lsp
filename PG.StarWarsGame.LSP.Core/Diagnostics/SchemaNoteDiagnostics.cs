// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Core.Diagnostics;

/// <summary>
///     How loudly each note kind is said, in one place.
///     <para>
///         The severity belongs to the KIND, not to the handler that happens to find the note, so
///         that a deprecated tag and a deprecated enum value cannot end up at different severities
///         because two handlers were written months apart. Handlers still own their message and
///         their diagnostic id - the wording is site-specific, and the id is a published contract
///         that must not move.
///     </para>
/// </summary>
public static class SchemaNoteDiagnostics
{
    /// <summary>
    ///     What a note of this kind is worth as a diagnostic, or null for a kind that belongs in
    ///     hover only. <see cref="SchemaNoteKind.Since" /> is the latter: a version is read on
    ///     purpose, and in the problems list it would be noise on every use of a recent value.
    /// </summary>
    public static XmlDiagnosticSeverity? SeverityOf(SchemaNoteKind kind)
    {
        return kind switch
        {
            // The element does nothing, or the wrong thing. A file that keeps it is broken, and
            // the author cannot see that from the file itself.
            SchemaNoteKind.BuggedInEngine => XmlDiagnosticSeverity.Error,
            SchemaNoteKind.Deprecated => XmlDiagnosticSeverity.Warning,
            SchemaNoteKind.Untested => XmlDiagnosticSeverity.Information,
            SchemaNoteKind.Remark => XmlDiagnosticSeverity.Hint,
            _ => null
        };
    }

    /// <summary>
    ///     The editor metadata for this kind. Only deprecation has one: the strike-through says
    ///     "this still works, but stop using it", which is exactly and only what Deprecated means.
    /// </summary>
    public static IReadOnlyList<XmlDiagnosticTag>? TagsFor(SchemaNoteKind kind)
    {
        return kind == SchemaNoteKind.Deprecated ? [XmlDiagnosticTag.Deprecated] : null;
    }

    /// <summary>
    ///     The note's own words in <paramref name="locale" />, falling back to English, or null when
    ///     it has none. A kind whose whole content is its value carries no text at all.
    /// </summary>
    public static string? TextOf(SchemaNote note, string locale)
    {
        var text = note.Text.GetValueOrDefault(locale) ?? note.Text.GetValueOrDefault("en");
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }
}

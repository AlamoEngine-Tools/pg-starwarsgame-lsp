// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     Surfaces the notes carried by the enum VALUE a story parameter names, as opposed to the notes
///     on the parameter slot itself (<see cref="StoryParamNotesHandler" />).
///     <para>
///         The gap this closes: until schema 2.0.0 only a hardcoded-set value could carry a
///         diagnostic, so an enum member the engine ignores had nowhere to say so.
///         <c>NOT_EQUAL_TO</c> on <c>StoryFlagCompareMethod</c> is the case that prompted it - it
///         parses, its branch in the comparison does nothing, and the event silently never fires.
///     </para>
/// </summary>
public sealed class StoryParamValueNotesHandler : XmlDiagnosticsHandler<StoryParamFact>
{
    /// <inheritdoc />
    public override DiagnosticId? DefaultId => DiagnosticIds.EnumValueNotes;

    protected override IEnumerable<XmlDiagnosticResult> Handle(StoryParamFact fact, DiagnosticsContext ctx)
    {
        var written = fact.RawValue.Trim();

        if (fact.Def?.Enum is not { } declared || written.Length == 0)
            return [];

        // Matched the way the engine reads it. A value nobody declared is the unknown-value check's
        // diagnostic, not this one's - saying it twice helps no reader.
        var value = declared.Values
            .FirstOrDefault(v => string.Equals(v.Name, written, StringComparison.OrdinalIgnoreCase));
        if (value is null)
            return [];

        // Ranked here rather than trusted to arrive ranked. The parser does rank what it reads, but
        // a definition built in code - a test, or any future source that is not YAML - has not been
        // through it, and "worst thing first" is a promise made to the reader of this list.
        var results = new List<XmlDiagnosticResult>();
        foreach (var note in SchemaNote.Ranked(value.Notes))
        {
            var severity = SchemaNoteDiagnostics.SeverityOf(note.Kind);
            if (severity is null) continue;

            var message = Message(note, value.Name, ctx.Locale);
            if (message is null) continue;

            results.Add(new XmlDiagnosticResult(severity.Value, message,
                Tags: SchemaNoteDiagnostics.TagsFor(note.Kind), Id: IdFor(note.Kind)));
        }

        return results;
    }

    private static string? Message(SchemaNote note, string value, string locale)
    {
        var text = SchemaNoteDiagnostics.TextOf(note, locale);
        var lead = note.Kind switch
        {
            SchemaNoteKind.BuggedInEngine => $"'{value}' is accepted but does nothing in the engine.",
            SchemaNoteKind.Deprecated => $"'{value}' is deprecated and should not be used.",
            SchemaNoteKind.Untested => $"'{value}' is documented but untested - it may not work in the engine.",
            // A remark is only ever its own words; without them it has nothing to say.
            _ => null
        };

        if (lead is null) return text;
        return text is null ? lead : $"{lead} {text}";
    }

    private static DiagnosticId IdFor(SchemaNoteKind kind)
    {
        return kind switch
        {
            SchemaNoteKind.BuggedInEngine => DiagnosticIds.EnumValueBuggedInEngine,
            SchemaNoteKind.Deprecated => DiagnosticIds.EnumValueDeprecated,
            SchemaNoteKind.Untested => DiagnosticIds.EnumValueUntested,
            _ => DiagnosticIds.EnumValueNotes
        };
    }
}
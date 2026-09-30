// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

public sealed class EventTypeNotesHandler : XmlDiagnosticsHandler<StoryEventFact>
{
    /// <inheritdoc />
    public override DiagnosticId? DefaultId => DiagnosticIds.EventTypeNotes;

    protected override IEnumerable<XmlDiagnosticResult> Handle(StoryEventFact fact, DiagnosticsContext ctx)
    {
        // Remarks only. The kinds that say something is wrong - deprecated, bugged, untested - are
        // each raised by their own handler at their own severity; this one carries what is left.
        var note = fact.Def?.Notes.TextFor(SchemaNoteKind.Remark, ctx.Locale);
        if (note is null)
            return [];

        return [new XmlDiagnosticResult(XmlDiagnosticSeverity.Hint, note)];
    }
}
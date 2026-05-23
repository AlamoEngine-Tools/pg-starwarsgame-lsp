// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

public sealed class XmlNotesHandler : XmlDiagnosticsHandler<XmlNotesFact>
{
    protected override IEnumerable<XmlDiagnosticResult> Handle(XmlNotesFact fact, DiagnosticsContext ctx)
    {
        if (!fact.Tag.Notes.TryGetValue(ctx.Locale, out var note))
            return [];

        return [new XmlDiagnosticResult(XmlDiagnosticSeverity.Hint, note)];
    }
}
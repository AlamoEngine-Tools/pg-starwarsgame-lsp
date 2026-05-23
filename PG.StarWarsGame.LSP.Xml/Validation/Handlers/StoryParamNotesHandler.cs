// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

public sealed class StoryParamNotesHandler : XmlDiagnosticsHandler<StoryParamFact>
{
    protected override IEnumerable<XmlDiagnosticResult> Handle(StoryParamFact fact, DiagnosticsContext ctx)
    {
        if (fact.Def is null || fact.RawValue.Length == 0)
            return [];
        if (!fact.Def.Notes.TryGetValue(ctx.Locale, out var note))
            return [];

        return [new XmlDiagnosticResult(XmlDiagnosticSeverity.Hint, note)];
    }
}
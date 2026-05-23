// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

public sealed class DeprecatedEventTypeHandler : XmlDiagnosticsHandler<StoryEventFact>
{
    protected override IEnumerable<XmlDiagnosticResult> Handle(StoryEventFact fact, DiagnosticsContext ctx)
    {
        if (fact.Def is null || !fact.Def.Deprecated)
            return [];

        return
        [
            new XmlDiagnosticResult(XmlDiagnosticSeverity.Warning,
                $"'{fact.EventType}' is deprecated.")
        ];
    }
}
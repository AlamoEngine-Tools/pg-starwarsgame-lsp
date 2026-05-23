// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

public sealed class UnresolvedReferenceHandler : XmlDiagnosticsHandler<XmlReferenceFact>
{
    protected override IEnumerable<XmlDiagnosticResult> Handle(XmlReferenceFact fact, DiagnosticsContext ctx)
    {
        if (fact.Resolved is not null)
            return [];

        return
        [
            new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                $"Cannot resolve reference '{fact.TargetId}': no object with this name exists in the workspace.")
        ];
    }
}
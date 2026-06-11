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

        var eval = ReferenceResolutionEvaluator.Evaluate(fact.TargetId, fact.ExpectedTypeName, null);
        return eval is { } r ? [new XmlDiagnosticResult(r.Severity, r.Message)] : [];
    }
}
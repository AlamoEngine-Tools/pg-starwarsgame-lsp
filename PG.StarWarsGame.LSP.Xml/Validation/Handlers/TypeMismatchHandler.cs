// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

public sealed class TypeMismatchHandler : XmlDiagnosticsHandler<XmlReferenceFact>
{
    /// <inheritdoc />
    public override DiagnosticId? DefaultId => DiagnosticIds.TypeMismatch;

    protected override IEnumerable<XmlDiagnosticResult> Handle(XmlReferenceFact fact, DiagnosticsContext ctx)
    {
        if (fact.Resolved is null || fact.ExpectedTypeName is null)
            return [];

        // The expected name may be a KIND rather than a type - a planet slot, a hero slot - and the
        // two never share a name, so looking it up here is unambiguous.
        var eval = ReferenceResolutionEvaluator.Evaluate(fact.TargetId, fact.ExpectedTypeName, fact.Resolved,
            expectedKind: ctx.Schema.GetKind(fact.ExpectedTypeName), index: ctx.Index);
        return eval is { } r ? [new XmlDiagnosticResult(r.Severity, r.Message)] : [];
    }
}
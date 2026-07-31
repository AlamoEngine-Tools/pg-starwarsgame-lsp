// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

public sealed class CrossTypeShadowHandler : XmlDiagnosticsHandler<XmlCrossTypeShadowFact>
{
    /// <inheritdoc />
    public override DiagnosticId? DefaultId => DiagnosticIds.CrossTypeShadow;

    protected override IEnumerable<XmlDiagnosticResult> Handle(XmlCrossTypeShadowFact fact, DiagnosticsContext ctx)
    {
        yield return new XmlDiagnosticResult(
            XmlDiagnosticSeverity.Warning,
            $"'{fact.SymbolId}' is defined as both {fact.OwnTypeName} and " +
            $"{fact.CollidingTypeName}. Typed references resolve to the matching type, " +
            $"but untyped lookups pick the highest-rank definition. " +
            $"If that is intended, declare it: <!-- <Override Name=\"{fact.SymbolId}\"/> -->");
    }
}
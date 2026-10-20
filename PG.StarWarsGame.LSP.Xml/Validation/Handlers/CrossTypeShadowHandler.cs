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
            ctx.Messages.Format("CrossTypeShadow_IsDefinedAsBothAnd", fact.SymbolId, fact.OwnTypeName,
                fact.CollidingTypeName, fact.SymbolId));
    }
}
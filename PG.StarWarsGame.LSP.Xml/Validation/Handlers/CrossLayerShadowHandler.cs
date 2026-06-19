// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

public sealed class CrossLayerShadowHandler : XmlDiagnosticsHandler<XmlLayerShadowFact>
{
    protected override IEnumerable<XmlDiagnosticResult> Handle(XmlLayerShadowFact fact, DiagnosticsContext ctx)
    {
        yield return new XmlDiagnosticResult(
            XmlDiagnosticSeverity.Warning,
            $"'{fact.SymbolId}' overrides a definition from '{fact.ShadowedLayerName}'. " +
            $"To suppress: <!-- <Override Name=\"{fact.SymbolId}\"/> -->");
    }
}
// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

public sealed class CrossLayerShadowHandler : XmlDiagnosticsHandler<XmlLayerShadowFact>
{
    /// <inheritdoc />
    public override DiagnosticId? DefaultId => DiagnosticIds.CrossLayerShadow;

    protected override IEnumerable<XmlDiagnosticResult> Handle(XmlLayerShadowFact fact, DiagnosticsContext ctx)
    {
        // "Declare", not "suppress": the marker asserts the override is deliberate and is checked
        // against the code, where a suppression would only hide the message.
        yield return new XmlDiagnosticResult(
            XmlDiagnosticSeverity.Warning,
            ctx.Messages.Format("CrossLayerShadow_OverridesADefinitionFromIf", fact.SymbolId, fact.ShadowedLayerName,
                fact.SymbolId));
    }
}
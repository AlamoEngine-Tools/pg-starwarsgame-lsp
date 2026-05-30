// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

public sealed class ForceDeploymentListHandler : XmlDiagnosticsHandler<XmlTagValueFact>
{
    public override XmlValueType? HandledValueType => XmlValueType.ForceDeploymentList;

    protected override IEnumerable<XmlDiagnosticResult> Handle(XmlTagValueFact fact, DiagnosticsContext ctx)
    {
        if (fact.Tag.ValueType != XmlValueType.ForceDeploymentList)
            return [];

        var trimmed = fact.RawValue.Trim();
        var parts = trimmed.Split(',');
        if (parts.Length < 3 || parts.Any(p => p.Trim().Length == 0))
            return
            [
                new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                    $"'{trimmed}' is not a valid force deployment entry for <{fact.Tag.Tag}>. Expected: FactionName, PlanetName, UnitTypeName.")
            ];

        return [];
    }
}

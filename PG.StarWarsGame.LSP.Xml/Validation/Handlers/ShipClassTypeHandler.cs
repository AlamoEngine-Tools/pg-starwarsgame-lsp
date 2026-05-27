// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

public sealed class ShipClassTypeHandler : XmlDiagnosticsHandler<XmlTagValueFact>
{
    public override XmlValueType? HandledValueType => XmlValueType.ShipClassType;

    protected override IEnumerable<XmlDiagnosticResult> Handle(XmlTagValueFact fact, DiagnosticsContext ctx)
    {
        if (fact.Tag.ValueType != XmlValueType.ShipClassType)
            return [];

        if (fact.RawValue.Trim().Length == 0)
            return
            [
                new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                    $"'' is not a valid ship class type for <{fact.Tag.Tag}>.")
            ];

        return [];
    }
}
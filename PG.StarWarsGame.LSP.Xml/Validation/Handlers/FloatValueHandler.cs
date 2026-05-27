// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

public sealed class FloatValueHandler : XmlDiagnosticsHandler<XmlTagValueFact>
{
    public override XmlValueType? HandledValueType => XmlValueType.Float;

    protected override IEnumerable<XmlDiagnosticResult> Handle(XmlTagValueFact fact, DiagnosticsContext ctx)
    {
        if (fact.Tag.ValueType != XmlValueType.Float)
            return [];

        var trimmed = fact.RawValue.Trim();
        if (!LenientFloatParser.TryParse(trimmed, out _))
            return
            [
                new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                    $"'{trimmed}' is not a valid float for <{fact.Tag.Tag}>.")
            ];

        return [];
    }
}
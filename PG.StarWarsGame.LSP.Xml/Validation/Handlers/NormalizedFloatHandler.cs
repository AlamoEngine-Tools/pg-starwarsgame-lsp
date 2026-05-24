// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Globalization;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

public sealed class NormalizedFloatHandler : XmlDiagnosticsHandler<XmlTagValueFact>
{
    protected override IEnumerable<XmlDiagnosticResult> Handle(XmlTagValueFact fact, DiagnosticsContext ctx)
    {
        if (fact.Tag.ValueType != XmlValueType.NormalizedFloat)
            return [];

        var trimmed = fact.RawValue.Trim().TrimEnd('f', 'F');
        if (!double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
            return
            [
                new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                    $"'{fact.RawValue.Trim()}' is not a valid number for <{fact.Tag.Tag}>.")
            ];

        if (d is < 0.0 or > 1.0)
        {
            var clamped = Math.Clamp(d, 0.0, 1.0).ToString("G", CultureInfo.InvariantCulture);
            return
            [
                new XmlDiagnosticResult(XmlDiagnosticSeverity.Warning,
                    $"Value {d} is out of range [0, 1] for <{fact.Tag.Tag}>. Did you mean {clamped}?",
                    SuggestedFix: clamped)
            ];
        }

        return [];
    }
}

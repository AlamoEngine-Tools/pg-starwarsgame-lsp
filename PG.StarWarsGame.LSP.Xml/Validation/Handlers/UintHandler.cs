// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

public sealed class UintHandler : XmlDiagnosticsHandler<XmlTagValueFact>
{
    public override IEnumerable<XmlValueType> HandledValueTypes =>
        [XmlValueType.UInt, XmlValueType.HardwareUInt];

    protected override IEnumerable<XmlDiagnosticResult> Handle(XmlTagValueFact fact, DiagnosticsContext ctx)
    {
        if (fact.Tag.ValueType is not (XmlValueType.UInt or XmlValueType.HardwareUInt))
            return [];

        var trimmed = fact.RawValue.Trim();
        if (!LenientFloatParser.TryParse(trimmed, out var floatVal))
            return
            [
                new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                    $"'{trimmed}' is not a valid number for <{fact.Tag.Tag}>.")
            ];

        if (uint.TryParse(trimmed, out _))
            return [];

        var clamped = Math.Clamp(Math.Truncate(floatVal), 0.0, uint.MaxValue);
        var corrected = ((uint)clamped).ToString();
        return
        [
            new XmlDiagnosticResult(XmlDiagnosticSeverity.Warning,
                $"'{trimmed}' is not a valid non-negative integer for <{fact.Tag.Tag}>. Did you mean {corrected}?",
                SuggestedFix: corrected)
        ];
    }
}

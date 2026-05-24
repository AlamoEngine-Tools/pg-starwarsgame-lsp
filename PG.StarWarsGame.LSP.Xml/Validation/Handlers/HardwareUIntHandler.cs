// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

public sealed class HardwareUIntHandler : NumberValueHandlerBase
{
    protected override XmlValueType TargetType => XmlValueType.HardwareUInt;

    protected override IEnumerable<XmlDiagnosticResult> HandlePrecise(
        XmlTagValueFact fact, string trimmed, double floatVal, DiagnosticsContext ctx)
    {
        if (uint.TryParse(trimmed, out _))
            return [];

        var clamped = Math.Clamp(Math.Truncate(floatVal), 0.0, uint.MaxValue);
        var corrected = ((uint)clamped).ToString();
        return
        [
            new XmlDiagnosticResult(XmlDiagnosticSeverity.Warning,
                $"'{trimmed}' is not a valid hardware unsigned integer for <{fact.Tag.Tag}>. Did you mean {corrected}?",
                SuggestedFix: corrected)
        ];
    }
}

// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

public sealed class UintValueHandler : NumberValueHandlerBase
{
    protected override XmlValueType TargetType => XmlValueType.UInt;

    protected override IEnumerable<XmlDiagnosticResult> HandlePrecise(
        XmlTagValueFact fact, string trimmed, double floatVal, DiagnosticsContext ctx)
    {
        if (int.TryParse(trimmed, out var value) && value >= 0)
            return [];

        if (floatVal > int.MaxValue)
            return
            [
                new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                    $"'{trimmed}' is out of range for <{fact.Tag.Tag}>. Expected a non-negative integer.")
            ];

        var corrected = ((int)Math.Max(0.0, Math.Truncate(floatVal))).ToString();
        return
        [
            new XmlDiagnosticResult(XmlDiagnosticSeverity.Warning,
                $"'{trimmed}' is not a valid non-negative integer for <{fact.Tag.Tag}>. Did you mean {corrected}?",
                SuggestedFix: corrected)
        ];
    }
}

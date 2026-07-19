// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

public sealed class UvSlotIndexHandler : NumberValueHandlerBase
{
    protected override XmlValueType TargetType => XmlValueType.UvSlotIndex;

    protected override IEnumerable<XmlDiagnosticResult> HandlePrecise(
        XmlTagValueFact fact, string trimmed, double floatVal, DiagnosticsContext ctx)
    {
        if (int.TryParse(trimmed, out var value) && value >= 0 && value <= 3)
            return [];

        var corrected = ((int)Math.Clamp(Math.Truncate(floatVal), 0.0, 3.0)).ToString();
        return
        [
            new XmlDiagnosticResult(XmlDiagnosticSeverity.Warning,
                $"'{trimmed}' is not a valid UV slot index for <{fact.Tag.Tag}>. Expected an integer in [0, 3]. Did you mean {corrected}?",
                SuggestedFix: corrected)
        ];
    }
}
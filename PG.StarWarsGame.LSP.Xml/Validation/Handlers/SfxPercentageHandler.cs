// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

public sealed class SfxPercentageHandler : NumberValueHandlerBase
{
    protected override XmlValueType TargetType => XmlValueType.SfxPercentage;

    protected override IEnumerable<XmlDiagnosticResult> HandlePrecise(
        XmlTagValueFact fact, string trimmed, double floatVal, DiagnosticsContext ctx)
    {
        if (int.TryParse(trimmed, out var value) && value >= 0 && value <= 100)
            return [];

        var corrected = ((int)Math.Clamp(Math.Truncate(floatVal), 0.0, 100.0)).ToString();
        return
        [
            new XmlDiagnosticResult(XmlDiagnosticSeverity.Warning,
                $"'{trimmed}' is not a valid SFX percentage for <{fact.Tag.Tag}>. Expected [0, 100]. Did you mean {corrected}?",
                SuggestedFix: corrected)
        ];
    }
}

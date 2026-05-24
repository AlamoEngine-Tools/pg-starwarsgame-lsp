// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

public sealed class SfxCountHandler : NumberValueHandlerBase
{
    protected override XmlValueType TargetType => XmlValueType.SfxCount;

    protected override IEnumerable<XmlDiagnosticResult> HandlePrecise(
        XmlTagValueFact fact, string trimmed, double floatVal, DiagnosticsContext ctx)
    {
        if (int.TryParse(trimmed, out var value) && value >= -1)
            return [];

        var corrected = ((int)Math.Clamp(Math.Truncate(floatVal), -1.0, (double)int.MaxValue)).ToString();
        return
        [
            new XmlDiagnosticResult(XmlDiagnosticSeverity.Warning,
                $"'{trimmed}' is not a valid SFX count for <{fact.Tag.Tag}>. Expected -1 or a non-negative integer. Did you mean {corrected}?",
                SuggestedFix: corrected)
        ];
    }
}

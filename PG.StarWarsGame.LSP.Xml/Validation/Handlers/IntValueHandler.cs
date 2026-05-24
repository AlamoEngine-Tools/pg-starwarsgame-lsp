// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

public sealed class IntValueHandler : NumberValueHandlerBase
{
    protected override XmlValueType TargetType => XmlValueType.Int;

    protected override IEnumerable<XmlDiagnosticResult> HandlePrecise(
        XmlTagValueFact fact, string trimmed, double floatVal, DiagnosticsContext ctx)
    {
        if (int.TryParse(trimmed, out _))
            return [];

        if (floatVal is >= int.MinValue and <= int.MaxValue)
        {
            var corrected = ((int)floatVal).ToString();
            return
            [
                new XmlDiagnosticResult(XmlDiagnosticSeverity.Warning,
                    $"'{trimmed}' is a float but <{fact.Tag.Tag}> expects an integer. Did you mean {corrected}?",
                    SuggestedFix: corrected)
            ];
        }

        return
        [
            new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                $"'{trimmed}' is out of range for <{fact.Tag.Tag}>. Expected a valid integer.")
        ];
    }
}

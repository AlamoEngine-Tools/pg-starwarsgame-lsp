// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

public sealed class AudioParamIntHandler : NumberValueHandlerBase
{
    protected override XmlValueType TargetType => XmlValueType.AudioParamInt;

    protected override IEnumerable<XmlDiagnosticResult> HandlePrecise(
        XmlTagValueFact fact, string trimmed, double floatVal, DiagnosticsContext ctx)
    {
        if (int.TryParse(trimmed, out _))
            return [];

        var corrected = ((int)Math.Truncate(floatVal)).ToString();
        return
        [
            new XmlDiagnosticResult(XmlDiagnosticSeverity.Warning,
                $"'{trimmed}' is not a valid audio parameter integer for <{fact.Tag.Tag}>. Did you mean {corrected}?",
                SuggestedFix: corrected)
        ];
    }
}
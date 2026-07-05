// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

public sealed class MovieFrameTriggerHandler : CommaSeparatedPairHandlerBase
{
    protected override XmlValueType TargetType => XmlValueType.MovieFrameTrigger;

    protected override IEnumerable<XmlDiagnosticResult> HandleValue(XmlTagValueFact fact, DiagnosticsContext ctx)
    {
        var parts = SplitOnFirstComma(fact.RawValue.Trim());
        if (parts.Length != 2 || parts[0].Trim().Length == 0 ||
            !LenientIntParser.TryParse(parts[1].Trim(), out var frame, out var wasFloat) || frame < 0)
            return
            [
                new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                    $"'{fact.RawValue.Trim()}' is not a valid movie frame trigger for <{fact.Tag.Tag}>. Expected: EventKey, NonNegativeInteger.")
            ];

        if (wasFloat)
            // Consistent int-slot policy: floats are accepted (the game truncates) but warned.
            return
            [
                AtPairSlot(new XmlDiagnosticResult(XmlDiagnosticSeverity.Warning,
                        $"'{parts[1].Trim()}' is a float but <{fact.Tag.Tag}> expects an integer. Did you mean {frame}?",
                        SuggestedFix: frame.ToString()),
                    fact, 1)
            ];

        return [];
    }
}
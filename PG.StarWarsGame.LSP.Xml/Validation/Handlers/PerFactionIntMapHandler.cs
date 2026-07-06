// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

public sealed class PerFactionIntMapHandler : CommaSeparatedPairHandlerBase
{
    protected override XmlValueType TargetType => XmlValueType.PerFactionIntMap;

    protected override IEnumerable<XmlDiagnosticResult> HandleValue(XmlTagValueFact fact, DiagnosticsContext ctx)
    {
        var parts = SplitOnFirstComma(fact.RawValue.Trim());
        if (parts.Length != 2 || parts[0].Trim().Length == 0 ||
            !LenientIntParser.TryParse(parts[1].Trim(), out var value, out var wasFloat))
            return
            [
                new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                    $"'{fact.RawValue.Trim()}' is not a valid per-faction int map for <{fact.Tag.Tag}>. Expected: FactionName, Integer.")
            ];

        if (wasFloat)
            // Consistent int-slot policy: floats are accepted (the game truncates) but warned.
            return
            [
                AtPairSlot(new XmlDiagnosticResult(XmlDiagnosticSeverity.Warning,
                        $"'{parts[1].Trim()}' is a float but <{fact.Tag.Tag}> expects an integer. Did you mean {value}?",
                        SuggestedFix: value.ToString()),
                    fact, 1)
            ];

        return [];
    }
}
// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

public sealed class UnitSpawnTableHandler : CommaSeparatedPairHandlerBase
{
    protected override XmlValueType TargetType => XmlValueType.UnitSpawnTable;

    protected override IEnumerable<XmlDiagnosticResult> HandleValue(XmlTagValueFact fact, DiagnosticsContext ctx)
    {
        var parts = SplitOnFirstComma(fact.RawValue.Trim());
        if (parts.Length != 2 || parts[0].Trim().Length == 0 ||
            !LenientIntParser.TryParse(parts[1].Trim(), out var count, out var wasFloat) || count < -1)
            return
            [
                new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                    $"'{fact.RawValue.Trim()}' is not a valid unit spawn entry for <{fact.Tag.Tag}>. Expected: UnitTypeName, Integer >= -1.")
            ];

        var results = new List<XmlDiagnosticResult>();
        if (wasFloat)
            // Consistent int-slot policy: floats are accepted (the game truncates) but warned.
            results.Add(AtPairSlot(new XmlDiagnosticResult(XmlDiagnosticSeverity.Warning,
                    $"'{parts[1].Trim()}' is a float but <{fact.Tag.Tag}> expects an integer. Did you mean {count}?",
                    SuggestedFix: count.ToString()),
                fact, 1));

        var d = TryValidateGameObjectName(parts[0].Trim(), fact.Tag.Tag, ctx.Index);
        if (d is not null)
            results.Add(AtPairSlot(d, fact, 0));
        return results;
    }
}
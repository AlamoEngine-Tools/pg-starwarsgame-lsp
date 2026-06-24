// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

public sealed class UnitSpawnProbabilityTableHandler : CommaSeparatedPairHandlerBase
{
    protected override XmlValueType TargetType => XmlValueType.UnitSpawnProbabilityTable;

    protected override IEnumerable<XmlDiagnosticResult> HandleValue(XmlTagValueFact fact, DiagnosticsContext ctx)
    {
        var trimmed = fact.RawValue.Trim();
        if (trimmed.Length == 0)
            return [Error(fact, trimmed)];

        var parts = trimmed.Split(',')
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .ToArray();

        if (parts.Length == 0 || parts.Length % 2 != 0)
            return [Error(fact, trimmed)];

        for (var i = 0; i < parts.Length; i += 2)
        {
            if (!LenientFloatParser.TryParse(parts[i + 1], out var probability) ||
                probability < 0.0f || probability > 1.0f)
                return [Error(fact, trimmed)];
        }

        var results = new List<XmlDiagnosticResult>();
        for (var i = 0; i < parts.Length; i += 2)
        {
            var d = TryValidateGameObjectName(parts[i], fact.Tag.Tag, ctx.Index);
            if (d is not null) results.Add(d);
        }
        return results;
    }

    private static XmlDiagnosticResult Error(XmlTagValueFact fact, string trimmed) =>
        new(XmlDiagnosticSeverity.Error,
            $"'{trimmed}' is not a valid spawn probability table for <{fact.Tag.Tag}>. Expected pairs of UnitTypeName, Float [0.0, 1.0].");
}

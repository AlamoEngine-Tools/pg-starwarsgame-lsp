// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

public sealed class PerFactionValueHandler : CommaSeparatedPairHandlerBase
{
    /// <inheritdoc />
    public override DiagnosticId? DefaultId => DiagnosticIds.PerFactionValue;

    protected override XmlValueType TargetType => XmlValueType.PerFactionValue;

    protected override IEnumerable<XmlDiagnosticResult> HandleValue(XmlTagValueFact fact, DiagnosticsContext ctx)
    {
        var raw = fact.RawValue.Trim();
        var parts = raw.Split(',').Select(p => p.Trim()).ToArray();

        // Same reader, different first slot: Corruption_Level_Override names a planet.
        var planet = fact.Tag.SemanticType == TagSemanticType.PlanetValuePair;
        if (parts.Length < 2 || parts[0].Length == 0 || !EngineNumber.TryParseFloat(parts[1], out _))
            return
            [
                new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                    planet
                        ? ctx.Messages.Format("PerFactionValue_IsNotAValidPlanet", raw, fact.Tag.Tag)
                        : ctx.Messages.Format("PerFactionValue_IsNotAValidPer", raw, fact.Tag.Tag))
            ];

        return OneEntryPerTag.ExtraTokens(fact.Tag.Tag, parts, 2, ctx.Messages) is { } extra ? [extra] : [];
    }
}
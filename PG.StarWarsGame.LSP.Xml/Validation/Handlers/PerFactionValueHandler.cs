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
        if (parts.Length < 2 || parts[0].Length == 0 || !LenientFloatParser.TryParse(parts[1], out _))
            return
            [
                new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                    planet
                        ? $"'{raw}' is not a valid planet value for <{fact.Tag.Tag}>. Expected: Planet, Number."
                        : $"'{raw}' is not a valid per-faction value for <{fact.Tag.Tag}>. Expected: FactionName, Number.")
            ];

        return OneEntryPerTag.ExtraTokens(fact.Tag.Tag, parts, 2) is { } extra ? [extra] : [];
    }
}
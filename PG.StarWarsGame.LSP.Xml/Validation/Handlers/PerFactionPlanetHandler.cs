// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

public sealed class PerFactionPlanetHandler : CommaSeparatedPairHandlerBase
{
    /// <inheritdoc />
    public override DiagnosticId? DefaultId => DiagnosticIds.PerFactionPlanet;

    protected override XmlValueType TargetType => XmlValueType.PerFactionPlanet;

    protected override IEnumerable<XmlDiagnosticResult> HandleValue(XmlTagValueFact fact, DiagnosticsContext ctx)
    {
        var raw = fact.RawValue.Trim();
        var parts = raw.Split(',').Select(p => p.Trim()).ToArray();
        if (parts.Length < 2 || parts[0].Length == 0 || parts[1].Length == 0)
            return
            [
                new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                    $"'{raw}' is not a valid per-faction planet for <{fact.Tag.Tag}>. Expected: FactionName, PlanetName.")
            ];

        return OneEntryPerTag.ExtraTokens(fact.Tag.Tag, parts, 2) is { } extra ? [extra] : [];
    }
}
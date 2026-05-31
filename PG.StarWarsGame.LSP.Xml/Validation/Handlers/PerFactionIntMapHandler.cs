// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

public sealed class PerFactionIntMapHandler : CommaSeparatedPairHandlerBase
{
    public override XmlValueType? HandledValueType => XmlValueType.PerFactionIntMap;

    protected override IEnumerable<XmlDiagnosticResult> Handle(XmlTagValueFact fact, DiagnosticsContext ctx)
    {
        if (fact.Tag.ValueType != XmlValueType.PerFactionIntMap)
            return [];

        var parts = SplitOnFirstComma(fact.RawValue.Trim());
        if (parts.Length != 2 || parts[0].Trim().Length == 0 ||
            !int.TryParse(parts[1].Trim(), out _))
            return
            [
                new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                    $"'{fact.RawValue.Trim()}' is not a valid per-faction int map for <{fact.Tag.Tag}>. Expected: FactionName, Integer.")
            ];

        return [];
    }
}
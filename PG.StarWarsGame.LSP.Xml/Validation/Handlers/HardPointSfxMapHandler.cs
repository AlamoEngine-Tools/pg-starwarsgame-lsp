// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

public sealed class HardPointSfxMapHandler : CommaSeparatedPairHandlerBase
{
    public override XmlValueType? HandledValueType => XmlValueType.HardPointSfxMap;

    protected override IEnumerable<XmlDiagnosticResult> Handle(XmlTagValueFact fact, DiagnosticsContext ctx)
    {
        if (fact.Tag.ValueType != XmlValueType.HardPointSfxMap)
            return [];

        var parts = SplitOnFirstComma(fact.RawValue.Trim());
        // element[1] (SFXEvent) is allowed to be empty; element[0] (hard point type) must be non-empty
        if (parts.Length != 2 || parts[0].Trim().Length == 0)
            return
            [
                new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                    $"'{fact.RawValue.Trim()}' is not a valid hard point SFX map for <{fact.Tag.Tag}>. Expected: HardPointType, SFXEventName.")
            ];

        return [];
    }
}

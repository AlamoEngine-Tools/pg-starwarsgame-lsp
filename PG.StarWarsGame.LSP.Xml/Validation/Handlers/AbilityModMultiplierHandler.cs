// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

public sealed class AbilityModMultiplierHandler : CommaSeparatedPairHandlerBase
{
    public override XmlValueType? HandledValueType => XmlValueType.AbilityModMultiplier;

    protected override IEnumerable<XmlDiagnosticResult> Handle(XmlTagValueFact fact, DiagnosticsContext ctx)
    {
        if (fact.Tag.ValueType != XmlValueType.AbilityModMultiplier)
            return [];

        var parts = SplitOnFirstComma(fact.RawValue.Trim());
        if (parts.Length != 2 || parts[0].Trim().Length == 0 ||
            !LenientFloatParser.TryParse(parts[1].Trim(), out _))
            return
            [
                new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                    $"'{fact.RawValue.Trim()}' is not a valid ability multiplier for <{fact.Tag.Tag}>. Expected: MultiplierType, Float.")
            ];

        return [];
    }
}
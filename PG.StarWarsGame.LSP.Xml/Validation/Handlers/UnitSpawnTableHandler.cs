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
            !int.TryParse(parts[1].Trim(), out var count) || count < -1)
            return
            [
                new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                    $"'{fact.RawValue.Trim()}' is not a valid unit spawn entry for <{fact.Tag.Tag}>. Expected: UnitTypeName, Integer >= -1.")
            ];

        var d = TryValidateGameObjectName(parts[0].Trim(), fact.Tag.Tag, ctx.Index);
        return d is not null ? [d] : [];
    }
}
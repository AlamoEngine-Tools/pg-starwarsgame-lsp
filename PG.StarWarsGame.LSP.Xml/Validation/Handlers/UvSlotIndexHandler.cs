// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

public sealed class UvSlotIndexHandler : SingleValueTypeHandlerBase
{
    protected override XmlValueType TargetType => XmlValueType.UvSlotIndex;

    protected override IEnumerable<XmlDiagnosticResult> HandleValue(XmlTagValueFact fact, DiagnosticsContext ctx)
    {
        var trimmed = fact.RawValue.Trim();
        if (!int.TryParse(trimmed, out var value) || value < 0 || value > 3)
            return
            [
                new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                    $"'{trimmed}' is not a valid UV slot index for <{fact.Tag.Tag}>. Expected an integer in [0, 3].")
            ];

        return [];
    }
}
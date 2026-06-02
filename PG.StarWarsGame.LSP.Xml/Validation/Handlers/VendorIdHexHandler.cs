// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

public sealed class VendorIdHexHandler : SingleValueTypeHandlerBase
{
    protected override XmlValueType TargetType => XmlValueType.VendorIdHex;

    protected override IEnumerable<XmlDiagnosticResult> HandleValue(XmlTagValueFact fact, DiagnosticsContext ctx)
    {
        var trimmed = fact.RawValue.Trim();
        if (!ShaderVersionHexHandler.IsHexLiteral(trimmed))
            return
            [
                new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                    $"'{trimmed}' is not a valid hex literal for <{fact.Tag.Tag}>. Expected format: 0x[0-9A-Fa-f]+.")
            ];

        return [];
    }
}
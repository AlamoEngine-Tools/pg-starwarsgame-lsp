// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

public sealed class ShaderVersionHexHandler : XmlDiagnosticsHandler<XmlTagValueFact>
{
    protected override IEnumerable<XmlDiagnosticResult> Handle(XmlTagValueFact fact, DiagnosticsContext ctx)
    {
        if (fact.Tag.ValueType != XmlValueType.ShaderVersionHex)
            return [];

        var trimmed = fact.RawValue.Trim();
        if (!IsHexLiteral(trimmed))
            return
            [
                new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                    $"'{trimmed}' is not a valid hex literal for <{fact.Tag.Tag}>. Expected format: 0x[0-9A-Fa-f]+.")
            ];

        return [];
    }

    internal static bool IsHexLiteral(string s)
    {
        return s.Length > 2 && s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && s[2..].All(Uri.IsHexDigit);
    }
}
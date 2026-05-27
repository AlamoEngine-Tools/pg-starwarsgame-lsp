// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

public sealed class BooleanValueHandler : XmlDiagnosticsHandler<XmlTagValueFact>
{
    public override XmlValueType? HandledValueType => XmlValueType.Boolean;

    private static readonly HashSet<string> ValidValues =
        new(StringComparer.OrdinalIgnoreCase) { "true", "false", "yes", "no", "1", "0" };

    protected override IEnumerable<XmlDiagnosticResult> Handle(XmlTagValueFact fact, DiagnosticsContext ctx)
    {
        if (fact.Tag.ValueType != XmlValueType.Boolean)
            return [];

        var trimmed = fact.RawValue.Trim();
        if (!ValidValues.Contains(trimmed))
            return
            [
                new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                    $"'{trimmed}' is not a valid Boolean for <{fact.Tag.Tag}>. Expected: True, False, Yes or No.")
            ];

        return [];
    }
}
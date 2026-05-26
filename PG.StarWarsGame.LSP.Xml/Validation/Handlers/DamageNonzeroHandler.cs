// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

public sealed class DamageNonzeroHandler : XmlDiagnosticsHandler<XmlTagValueFact>, IXmlNamedDiagnosticsHandler
{
    public string ValidationId => "damage-nonzero";

    protected override IEnumerable<XmlDiagnosticResult> Handle(XmlTagValueFact fact, DiagnosticsContext ctx)
    {
        var trimmed = fact.RawValue.Trim();
        if (!LenientFloatParser.TryParse(trimmed, out var value))
            return [];

        if (value <= 0f)
            return
            [
                new XmlDiagnosticResult(XmlDiagnosticSeverity.Warning,
                    $"<{fact.Tag.Tag}> must be greater than 0 for the AI to use this unit.")
            ];

        return [];
    }
}
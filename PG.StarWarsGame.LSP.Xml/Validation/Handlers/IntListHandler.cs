// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text.RegularExpressions;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

public sealed partial class IntListHandler : XmlDiagnosticsHandler<XmlTagValueFact>
{
    protected override IEnumerable<XmlDiagnosticResult> Handle(XmlTagValueFact fact, DiagnosticsContext ctx)
    {
        if (fact.Tag.ValueType != XmlValueType.IntList)
            return [];

        var trimmed = fact.RawValue.Trim();
        if (trimmed.Length == 0)
            return
            [
                new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                    $"'' is not a valid integer list for <{fact.Tag.Tag}>.")
            ];

        var parts = Separator().Split(trimmed);

        // Any token that is not a valid float at all → error
        if (parts.Any(p => !LenientFloatParser.TryParse(p, out _)))
            return
            [
                new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                    $"'{trimmed}' is not a valid integer list for <{fact.Tag.Tag}>. Expected space-separated integers.")
            ];

        // All tokens are valid floats and also valid ints → OK
        if (parts.All(p => int.TryParse(p, out _)))
            return [];

        // Some tokens are floats but not ints → warning with corrected list
        var corrected = string.Join(" ", parts.Select(p =>
        {
            if (int.TryParse(p, out var iv))
                return iv.ToString();
            LenientFloatParser.TryParse(p, out var fv);
            return ((int)fv).ToString();
        }));

        return
        [
            new XmlDiagnosticResult(XmlDiagnosticSeverity.Warning,
                $"'{trimmed}' contains float values but <{fact.Tag.Tag}> expects integers. Did you mean '{corrected}'?",
                SuggestedFix: corrected)
        ];
    }

    [GeneratedRegex(@"[\s,]+")]
    private static partial Regex Separator();
}

// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text.RegularExpressions;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

public sealed partial class RgbaValueHandler : SingleValueTypeHandlerBase
{
    protected override XmlValueType TargetType => XmlValueType.RGBA;

    protected override IEnumerable<XmlDiagnosticResult> HandleValue(XmlTagValueFact fact, DiagnosticsContext ctx)
    {
        var trimmed = fact.RawValue.Trim();
        var parts = Separator().Split(trimmed);
        if (parts.Length is not 3 and not 4 || parts.Any(p => !IsByteComponent(p, out _, out _)))
            return
            [
                new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                    $"'{trimmed}' is not a valid RGBA color for <{fact.Tag.Tag}>. Expected 3 or 4 integers in 0–255, separated by spaces or commas.")
            ];

        // Consistent int-slot policy: float components are accepted (the game truncates) but warned.
        var anyFloat = false;
        var corrected = new List<string>(parts.Length);
        foreach (var p in parts)
        {
            IsByteComponent(p, out var value, out var wasFloat);
            anyFloat |= wasFloat;
            corrected.Add(value.ToString());
        }

        if (anyFloat)
        {
            var fix = string.Join(" ", corrected);
            return
            [
                new XmlDiagnosticResult(XmlDiagnosticSeverity.Warning,
                    $"'{trimmed}' contains float components but <{fact.Tag.Tag}> expects integers. Did you mean '{fix}'?",
                    SuggestedFix: fix)
            ];
        }

        return [];
    }

    [GeneratedRegex(@"[\s,]+")]
    private static partial Regex Separator();

    private static bool IsByteComponent(string s, out int value, out bool wasFloat)
    {
        return LenientIntParser.TryParse(s, out value, out wasFloat) && value is >= 0 and <= 255;
    }
}
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
        if (parts.Length is not 3 and not 4 || parts.Any(p => !IsByteComponent(p)))
            return
            [
                new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                    $"'{trimmed}' is not a valid RGBA color for <{fact.Tag.Tag}>. Expected 3 or 4 integers in 0–255, separated by spaces or commas.")
            ];

        return [];
    }

    [GeneratedRegex(@"[\s,]+")]
    private static partial Regex Separator();

    private static bool IsByteComponent(string s)
    {
        return int.TryParse(s, out var v) && v is >= 0 and <= 255;
    }
}
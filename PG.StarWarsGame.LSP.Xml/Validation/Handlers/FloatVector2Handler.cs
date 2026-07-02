// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text.RegularExpressions;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

public sealed partial class FloatVector2Handler : SingleValueTypeHandlerBase
{
    protected override XmlValueType TargetType => XmlValueType.FloatVector2;

    protected override IEnumerable<XmlDiagnosticResult> HandleValue(XmlTagValueFact fact, DiagnosticsContext ctx)
    {
        var trimmed = fact.RawValue.Trim();
        var parts = Separator().Split(trimmed).Where(p => p.Length > 0).ToArray();
        if (parts.Length != 2 || parts.Any(p => !LenientFloatParser.TryParse(p, out _)))
            return
            [
                new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                    $"'{trimmed}' is not a valid Float2 for <{fact.Tag.Tag}>. Expected 2 floats separated by spaces or commas.")
            ];

        return [];
    }

    [GeneratedRegex(@"[\s,]+")]
    private static partial Regex Separator();
}
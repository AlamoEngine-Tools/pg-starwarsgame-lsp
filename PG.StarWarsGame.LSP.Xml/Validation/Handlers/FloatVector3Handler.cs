// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text.RegularExpressions;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

public sealed partial class FloatVector3Handler : SingleValueTypeHandlerBase
{
    protected override XmlValueType TargetType => XmlValueType.FloatVector3;

    protected override IEnumerable<XmlDiagnosticResult> HandleValue(XmlTagValueFact fact, DiagnosticsContext ctx)
    {
        var trimmed = fact.RawValue.Trim();
        var parts = Separator().Split(trimmed);
        if (parts.Length != 3 || parts.Any(p => !LenientFloatParser.TryParse(p, out _)))
            return
            [
                new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                    $"'{trimmed}' is not a valid Float3 for <{fact.Tag.Tag}>. Expected 3 floats separated by spaces or commas.")
            ];

        return [];
    }

    [GeneratedRegex(@"[\s,]+")]
    private static partial Regex Separator();
}
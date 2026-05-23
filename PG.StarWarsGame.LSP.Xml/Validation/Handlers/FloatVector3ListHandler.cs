// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text.RegularExpressions;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

public sealed partial class FloatVector3ListHandler : XmlDiagnosticsHandler<XmlTagValueFact>
{
    protected override IEnumerable<XmlDiagnosticResult> Handle(XmlTagValueFact fact, DiagnosticsContext ctx)
    {
        if (fact.Tag.ValueType != XmlValueType.FloatVector3List)
            return [];

        var trimmed = fact.RawValue.Trim();
        if (trimmed.Length == 0)
            return
            [
                new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                    $"'' is not a valid Float3 list for <{fact.Tag.Tag}>.")
            ];

        var parts = Separator().Split(trimmed);
        if (parts.Length == 0 || parts.Length % 3 != 0)
            return
            [
                new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                    $"'{trimmed}' is not a valid Float3 list for <{fact.Tag.Tag}>. Expected a multiple of 3 floats.")
            ];

        if (parts.Any(p => !LenientFloatParser.TryParse(p, out _)))
            return
            [
                new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                    $"'{trimmed}' is not a valid Float3 list for <{fact.Tag.Tag}>. Expected space-separated floats.")
            ];

        return [];
    }

    [GeneratedRegex(@"[\s,]+")]
    private static partial Regex Separator();
}
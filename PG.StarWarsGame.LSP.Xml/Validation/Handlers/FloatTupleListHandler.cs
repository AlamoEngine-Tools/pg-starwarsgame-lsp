// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text.RegularExpressions;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

public sealed partial class FloatTupleListHandler : SingleValueTypeHandlerBase
{
    protected override XmlValueType TargetType => XmlValueType.FloatTupleList;

    protected override IEnumerable<XmlDiagnosticResult> HandleValue(XmlTagValueFact fact, DiagnosticsContext ctx)
    {
        var trimmed = fact.RawValue.Trim();
        if (trimmed.Length == 0)
            return
            [
                new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                    $"'' is not a valid float tuple list for <{fact.Tag.Tag}>.")
            ];

        var parts = Separator().Split(trimmed);
        if (parts.Any(p => !LenientFloatParser.TryParse(p, out _)))
            return
            [
                new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                    $"'{trimmed}' is not a valid float tuple list for <{fact.Tag.Tag}>. Expected space/comma-separated floats.")
            ];

        return [];
    }

    [GeneratedRegex(@"[\s,]+")]
    private static partial Regex Separator();
}
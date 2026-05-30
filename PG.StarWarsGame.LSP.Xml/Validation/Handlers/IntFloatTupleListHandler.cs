// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text.RegularExpressions;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

public sealed partial class IntFloatTupleListHandler : XmlDiagnosticsHandler<XmlTagValueFact>
{
    public override XmlValueType? HandledValueType => XmlValueType.IntFloatTupleList;

    protected override IEnumerable<XmlDiagnosticResult> Handle(XmlTagValueFact fact, DiagnosticsContext ctx)
    {
        if (fact.Tag.ValueType != XmlValueType.IntFloatTupleList)
            return [];

        var trimmed = fact.RawValue.Trim();
        if (trimmed.Length == 0)
            return [Error(fact, trimmed)];

        var parts = Separator().Split(trimmed);
        if (parts.Length % 2 != 0)
            return [Error(fact, trimmed)];

        for (var i = 0; i < parts.Length; i += 2)
        {
            if (!int.TryParse(parts[i], out _) || !LenientFloatParser.TryParse(parts[i + 1], out _))
                return [Error(fact, trimmed)];
        }

        return [];
    }

    private static XmlDiagnosticResult Error(XmlTagValueFact fact, string trimmed) =>
        new(XmlDiagnosticSeverity.Error,
            $"'{trimmed}' is not a valid int-float tuple list for <{fact.Tag.Tag}>. Expected alternating integer and float pairs.");

    [GeneratedRegex(@"[\s,]+")]
    private static partial Regex Separator();
}

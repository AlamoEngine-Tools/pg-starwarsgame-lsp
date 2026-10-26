// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text.RegularExpressions;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

public sealed partial class IntListHandler : SingleValueTypeHandlerBase
{
    /// <inheritdoc />
    public override DiagnosticId? DefaultId => DiagnosticIds.IntList;

    protected override XmlValueType TargetType => XmlValueType.IntList;

    protected override IEnumerable<XmlDiagnosticResult> HandleValue(XmlTagValueFact fact, DiagnosticsContext ctx)
    {
        var trimmed = fact.RawValue.Trim();
        if (trimmed.Length == 0)
            return
            [
                new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                    ctx.Messages.Format("IntList_IsNotAValidInteger", fact.Tag.Tag), Id: DiagnosticIds.IntListEmpty)
            ];

        var parts = Separator().Split(trimmed).Where(p => p.Length > 0).ToArray();

        // Any token that is not a valid float at all → error
        if (parts.Any(p => !EngineNumber.TryParseFloat(p, out _)))
            return
            [
                new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                    ctx.Messages.Format("IntList_IsNotAValidInteger2", trimmed, fact.Tag.Tag))
            ];

        // All tokens are valid floats and also valid ints → OK
        if (parts.All(p => int.TryParse(p, out _)))
            return [];

        // Some tokens are floats but not ints → warning with corrected list
        var corrected = string.Join(" ", parts.Select(p =>
        {
            if (int.TryParse(p, out var iv))
                return iv.ToString();
            EngineNumber.TryParseFloat(p, out var fv);
            return ((int)fv).ToString();
        }));

        return
        [
            new XmlDiagnosticResult(XmlDiagnosticSeverity.Warning,
                ctx.Messages.Format("IntList_ContainsFloatValuesButExpects", trimmed, fact.Tag.Tag, corrected),
                SuggestedFix: corrected, Id: DiagnosticIds.IntListFloatWhereIntExpected)
        ];
    }

    [GeneratedRegex(@"[\s,]+")]
    private static partial Regex Separator();
}
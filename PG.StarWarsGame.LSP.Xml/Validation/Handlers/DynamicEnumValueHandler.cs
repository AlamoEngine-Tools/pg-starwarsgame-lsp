// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text.RegularExpressions;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

public sealed partial class DynamicEnumValueHandler : SingleValueTypeHandlerBase
{
    private static readonly char[] ValueSeparators = ['|', ','];
    protected override XmlValueType TargetType => XmlValueType.DynamicEnumValue;

    protected override IEnumerable<XmlDiagnosticResult> HandleValue(XmlTagValueFact fact, DiagnosticsContext ctx)
    {
        var trimmed = fact.RawValue.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return
            [
                new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                    $"'' is not a valid enum identifier for <{fact.Tag.Tag}>.")
            ];

        var isFlagList = fact.Tag.SemanticType == TagSemanticType.FlagList;

        if (!isFlagList && trimmed.Contains('|'))
            return
            [
                new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                    $"<{fact.Tag.Tag}> expects a single enum identifier; '|' is not allowed here.")
            ];

        foreach (var segment in trimmed.Split(isFlagList ? ValueSeparators : [',']))
        {
            var part = segment.Trim();
            if (part.Length == 0 || !SegmentPattern().IsMatch(part))
                return
                [
                    new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                        $"'{trimmed}' is not a valid enum identifier for <{fact.Tag.Tag}>.")
                ];
        }

        if (fact.Tag.Enum is { Kind: EnumKind.SchemaFixed, Values.Count: > 0 } enumDef)
        {
            var known = enumDef.Values
                .Select(v => v.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var seg in trimmed.Split(ValueSeparators,
                         StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                if (!known.Contains(seg))
                    return
                    [
                        new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                            $"'{seg}' is not a known value for enum '{enumDef.Name}' on <{fact.Tag.Tag}>.")
                    ];
        }

        if (fact.Tag.Enum is { Kind: EnumKind.DynamicXml } dynEnumDef
            && ctx.Index.Baseline.DynamicEnumValues.TryGetValue(dynEnumDef.Name, out var knownValues)
            && knownValues.Length > 0)
        {
            var knownSet = new HashSet<string>(knownValues, StringComparer.OrdinalIgnoreCase);
            foreach (var seg in trimmed.Split(ValueSeparators,
                         StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                if (!knownSet.Contains(seg))
                    return
                    [
                        new XmlDiagnosticResult(XmlDiagnosticSeverity.Warning,
                            $"'{seg}' is not a known {dynEnumDef.Name} value.")
                    ];
        }

        return [];
    }

    [GeneratedRegex(@"^[\w ]+$")]
    private static partial Regex SegmentPattern();
}
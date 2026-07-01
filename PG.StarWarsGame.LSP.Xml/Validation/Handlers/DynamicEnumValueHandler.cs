// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text.RegularExpressions;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

public sealed partial class DynamicEnumValueHandler : NamedEnumValueHandlerBase
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

        var enumDef = fact.Tag.Enum;
        if (enumDef is null)
            return [];

        // SchemaFixed: known at compile time — unknown value is an Error.
        if (enumDef.Kind == EnumKind.SchemaFixed)
        {
            var valid = GetValidValues(enumDef, ctx);
            if (valid is not null)
            {
                foreach (var seg in trimmed.Split(ValueSeparators,
                             StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                    if (!valid.Contains(seg))
                        return
                        [
                            new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                                $"'{seg}' is not a known value for enum '{enumDef.Name}' on <{fact.Tag.Tag}>.")
                        ];
            }
        }

        // DynamicXml: defined in data files — unknown value is a Warning (baseline may be incomplete).
        if (enumDef.Kind == EnumKind.DynamicXml)
        {
            var valid = GetValidValues(enumDef, ctx);
            if (valid is not null)
            {
                foreach (var seg in trimmed.Split(ValueSeparators,
                             StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                    if (!valid.Contains(seg))
                        return
                        [
                            new XmlDiagnosticResult(XmlDiagnosticSeverity.Warning,
                                $"'{seg}' is not a known {enumDef.Name} value.")
                        ];
            }
        }

        return [];
    }

    [GeneratedRegex(@"^[\w ]+$")]
    private static partial Regex SegmentPattern();
}

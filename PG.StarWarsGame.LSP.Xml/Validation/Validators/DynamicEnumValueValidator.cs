// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text.RegularExpressions;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Validation;

namespace PG.StarWarsGame.LSP.Xml.Validation.Validators;

public sealed partial class DynamicEnumValueValidator : IXmlValueValidator
{
    private static readonly char[] ValueSeparators = ['|', ','];

    private readonly ISchemaProvider _schema;

    public DynamicEnumValueValidator(ISchemaProvider schema) => _schema = schema;

    public XmlValueType ValueType => XmlValueType.DynamicEnumValue;

    public XmlValidationResult Validate(string rawValue, XmlTagDefinition tag)
    {
        var trimmed = rawValue.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return XmlValidationResult.Failure(
                $"'' is not a valid enum identifier for <{tag.Tag}>.");

        var isFlagList = tag.SemanticType == TagSemanticType.FlagList;

        if (!isFlagList && trimmed.Contains('|'))
            return XmlValidationResult.Failure(
                $"<{tag.Tag}> expects a single enum identifier; '|' is not allowed here.");

        foreach (var segment in trimmed.Split(isFlagList ? ValueSeparators : [',']))
        {
            var part = segment.Trim();
            if (part.Length == 0 || !SegmentPattern().IsMatch(part))
                return XmlValidationResult.Failure(
                    $"'{trimmed}' is not a valid enum identifier for <{tag.Tag}>.");
        }

        if (!string.IsNullOrEmpty(tag.EnumName))
        {
            var enumDef = _schema.GetEnum(tag.EnumName);
            if (enumDef is { Kind: EnumKind.SchemaFixed, Values.Count: > 0 })
            {
                var known = enumDef.Values
                    .Select(v => v.Name)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                foreach (var seg in trimmed.Split(ValueSeparators, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                {
                    if (!known.Contains(seg))
                        return XmlValidationResult.Failure(
                            $"'{seg}' is not a known value for enum '{tag.EnumName}' on <{tag.Tag}>.");
                }
            }
        }

        return XmlValidationResult.Valid();
    }

    [GeneratedRegex(@"^[\w ]+$")]
    private static partial Regex SegmentPattern();
}

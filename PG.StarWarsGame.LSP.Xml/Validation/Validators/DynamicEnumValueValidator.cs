using System.Text.RegularExpressions;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Validation;

namespace PG.StarWarsGame.LSP.Xml.Validation.Validators;

public sealed partial class DynamicEnumValueValidator : IXmlValueValidator
{
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

        foreach (var segment in trimmed.Split(isFlagList ? '|' : ','))
        {
            var part = segment.Trim();
            if (part.Length == 0 || !SegmentPattern().IsMatch(part))
                return XmlValidationResult.Failure(
                    $"'{trimmed}' is not a valid enum identifier for <{tag.Tag}>.");
        }

        return XmlValidationResult.Valid();
    }

    [GeneratedRegex(@"^[\w ]+$")]
    private static partial Regex SegmentPattern();
}
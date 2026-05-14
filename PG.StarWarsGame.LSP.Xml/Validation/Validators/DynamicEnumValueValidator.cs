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
                $"'' is not a valid enum identifier for <{tag.Tag}>. Expected one or more identifiers separated by '|'.");

        foreach (var segment in trimmed.Split('|'))
        {
            var part = segment.Trim();
            if (part.Length == 0 || !SegmentPattern().IsMatch(part))
                return XmlValidationResult.Failure(
                    $"'{trimmed}' is not a valid enum identifier for <{tag.Tag}>. Expected one or more identifiers separated by '|'.");
        }

        return XmlValidationResult.Valid();
    }

    [GeneratedRegex(@"^[\w ]+$")]
    private static partial Regex SegmentPattern();
}
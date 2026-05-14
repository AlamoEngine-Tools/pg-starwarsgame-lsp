using System.Text.RegularExpressions;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Validation;

namespace PG.StarWarsGame.LSP.Xml.Validation.Validators;

public sealed partial class RgbaValidator : IXmlValueValidator
{
    public XmlValueType ValueType => XmlValueType.RGBA;

    public XmlValidationResult Validate(string rawValue, XmlTagDefinition tag)
    {
        var parts = Separator().Split(rawValue.Trim());
        if (parts.Length is not 3 and not 4 || parts.Any(p => !IsByteComponent(p)))
            return XmlValidationResult.Failure(
                $"'{rawValue.Trim()}' is not a valid RGBA color for <{tag.Tag}>. Expected 3 or 4 integers in 0–255, separated by spaces or commas.");

        return XmlValidationResult.Valid();
    }

    [GeneratedRegex(@"[\s,]+")]
    private static partial Regex Separator();

    private static bool IsByteComponent(string s)
    {
        return int.TryParse(s, out var v) && v is >= 0 and <= 255;
    }
}
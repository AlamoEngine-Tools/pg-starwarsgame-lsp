using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Validation;

namespace PG.StarWarsGame.LSP.Xml.Validation.Validators;

public sealed class ShaderVersionHexValidator : IXmlValueValidator
{
    public XmlValueType ValueType => XmlValueType.ShaderVersionHex;

    public XmlValidationResult Validate(string rawValue, XmlTagDefinition tag)
    {
        var trimmed = rawValue.Trim();
        return IsHexLiteral(trimmed)
            ? XmlValidationResult.Valid()
            : XmlValidationResult.Failure(
                $"'{trimmed}' is not a valid hex literal for <{tag.Tag}>. Expected format: 0x[0-9A-Fa-f]+.");
    }

    internal static bool IsHexLiteral(string s)
    {
        return s.Length > 2
               && s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
               && s[2..].All(Uri.IsHexDigit);
    }
}
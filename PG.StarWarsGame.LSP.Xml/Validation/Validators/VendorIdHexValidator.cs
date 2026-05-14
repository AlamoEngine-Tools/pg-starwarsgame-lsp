using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Validation;

namespace PG.StarWarsGame.LSP.Xml.Validation.Validators;

public sealed class VendorIdHexValidator : IXmlValueValidator
{
    public XmlValueType ValueType => XmlValueType.VendorIdHex;

    public XmlValidationResult Validate(string rawValue, XmlTagDefinition tag)
    {
        var trimmed = rawValue.Trim();
        return ShaderVersionHexValidator.IsHexLiteral(trimmed)
            ? XmlValidationResult.Valid()
            : XmlValidationResult.Failure(
                $"'{trimmed}' is not a valid hex literal for <{tag.Tag}>. Expected format: 0x[0-9A-Fa-f]+.");
    }
}
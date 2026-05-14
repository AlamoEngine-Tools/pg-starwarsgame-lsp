using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Validation;

namespace PG.StarWarsGame.LSP.Xml.Validation.Validators;

public sealed class FloatValueValidator : IXmlValueValidator
{
    public XmlValueType ValueType => XmlValueType.Float;

    public XmlValidationResult Validate(string rawValue, XmlTagDefinition tag)
    {
        var trimmed = rawValue.Trim();
        return LenientFloatParser.TryParse(trimmed, out _)
            ? XmlValidationResult.Valid()
            : XmlValidationResult.Failure($"'{trimmed}' is not a valid float for <{tag.Tag}>.");
    }
}
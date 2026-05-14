using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Validation;

namespace PG.StarWarsGame.LSP.Xml.Validation;

public sealed class XmlValueValidatorRegistry : IXmlValueValidatorRegistry
{
    private readonly IReadOnlyDictionary<XmlValueType, IXmlValueValidator> _validators;

    public XmlValueValidatorRegistry(IEnumerable<IXmlValueValidator> validators)
    {
        _validators = validators.ToDictionary(v => v.ValueType);
    }

    public XmlValidationResult Validate(XmlValueType valueType, string rawValue, XmlTagDefinition tag)
    {
        if (!_validators.TryGetValue(valueType, out var validator))
            return new XmlValidationResult
            {
                IsValid = false,
                Severity = XmlValidationSeverity.Warning,
                Message = $"No validator registered for value type '{valueType}' on tag '{tag.Tag}'."
            };

        return validator.Validate(rawValue, tag);
    }
}
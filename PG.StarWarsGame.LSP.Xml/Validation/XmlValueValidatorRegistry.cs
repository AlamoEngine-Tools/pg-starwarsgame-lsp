using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Validation;

namespace PG.StarWarsGame.LSP.Xml.Validation;

public sealed class XmlValueValidatorRegistry : IXmlValueValidatorRegistry
{
    private readonly IReadOnlyDictionary<XmlValueType, IXmlValueValidator> _validators;
    private readonly IReadOnlyDictionary<TagSemanticType, IXmlValueValidator> _semanticValidators;

    public XmlValueValidatorRegistry(IEnumerable<IXmlValueValidator> validators)
    {
        var all = validators.ToList();
        _validators = all
            .Where(v => v.SemanticType == TagSemanticType.Default)
            .ToDictionary(v => v.ValueType);
        _semanticValidators = all
            .Where(v => v.SemanticType != TagSemanticType.Default)
            .ToDictionary(v => v.SemanticType);
    }

    public XmlValidationResult Validate(XmlValueType valueType, string rawValue, XmlTagDefinition tag)
    {
        if (tag.SemanticType != TagSemanticType.Default &&
            _semanticValidators.TryGetValue(tag.SemanticType, out var semanticValidator))
            return semanticValidator.Validate(rawValue, tag);

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
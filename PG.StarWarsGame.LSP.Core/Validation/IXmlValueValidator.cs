using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Core.Validation;

public interface IXmlValueValidator
{
    XmlValueType ValueType { get; }

    /// <summary>
    /// Optional semantic refinement that routes this validator ahead of the base-type validator.
    /// <see cref="TagSemanticType.Default" /> (the default) means "dispatch by <see cref="ValueType" /> only."
    /// </summary>
    TagSemanticType SemanticType => TagSemanticType.Default;

    XmlValidationResult Validate(string rawValue, XmlTagDefinition tag);
}
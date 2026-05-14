using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Core.Validation;

public interface IXmlValueValidator
{
    XmlValueType ValueType { get; }
    XmlValidationResult Validate(string rawValue, XmlTagDefinition tag);
}
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Core.Validation;

public interface IXmlValueValidatorRegistry
{
    XmlValidationResult Validate(XmlValueType valueType, string rawValue, XmlTagDefinition tag);
}
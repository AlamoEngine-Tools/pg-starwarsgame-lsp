using System.Text.RegularExpressions;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Validation;

namespace PG.StarWarsGame.LSP.Xml.Validation.Validators;

public sealed partial class FloatVector4Validator : IXmlValueValidator
{
    public XmlValueType ValueType => XmlValueType.FloatVector4;

    public XmlValidationResult Validate(string rawValue, XmlTagDefinition tag)
    {
        var parts = Separator().Split(rawValue.Trim());
        if (parts.Length != 4 || parts.Any(p => !LenientFloatParser.TryParse(p, out _)))
            return XmlValidationResult.Failure(
                $"'{rawValue.Trim()}' is not a valid Float4 for <{tag.Tag}>. Expected 4 floats separated by spaces or commas.");

        return XmlValidationResult.Valid();
    }

    [GeneratedRegex(@"[\s,]+")]
    private static partial Regex Separator();
}
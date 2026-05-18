// // Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// // Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Validation;

namespace PG.StarWarsGame.LSP.Xml.Validation.Validators;

public sealed class UintValueValidator : IXmlValueValidator
{
    public XmlValueType ValueType => XmlValueType.UInt;

    public XmlValidationResult Validate(string rawValue, XmlTagDefinition tag)
    {
        try
        {
            return int.Parse(rawValue.Trim()) < 0
                ? throw new InvalidDataException("Value must be positive.")
                : XmlValidationResult.Valid();
        }
        catch (Exception _)
        {
            return XmlValidationResult.Failure(
                $"'{rawValue.Trim()}' is not a valid unsigned integer for <{tag.Tag}>. Expected a valid positive integer.");
        }
    }
}
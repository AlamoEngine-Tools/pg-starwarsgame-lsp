// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Validation;

namespace PG.StarWarsGame.LSP.Xml.Validation.Validators;

public sealed class IntValueValidator : IXmlValueValidator
{
    public XmlValueType ValueType => XmlValueType.Int;

    public XmlValidationResult Validate(string rawValue, XmlTagDefinition tag)
    {
        try
        {
            var _ = int.Parse(rawValue.Trim());
            return XmlValidationResult.Valid();
        }
        catch (Exception _)
        {
            return XmlValidationResult.Failure(
                $"'{rawValue.Trim()}' is not a valid integer for <{tag.Tag}>. Expected a valid integer.");
        }
    }
}
// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Validation;

namespace PG.StarWarsGame.LSP.Xml.Validation.Validators;

public sealed class HardwareUIntValidator : IXmlValueValidator
{
    public XmlValueType ValueType => XmlValueType.HardwareUInt;

    public XmlValidationResult Validate(string rawValue, XmlTagDefinition tag)
    {
        if (!uint.TryParse(rawValue.Trim(), out _))
            return XmlValidationResult.Failure(
                $"'{rawValue.Trim()}' is not a valid hardware unsigned integer for <{tag.Tag}>. Expected a non-negative integer.");
        return XmlValidationResult.Valid();
    }
}
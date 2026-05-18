// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Validation;

namespace PG.StarWarsGame.LSP.Xml.Validation.Validators;

public sealed class UvSlotIndexValidator : IXmlValueValidator
{
    public XmlValueType ValueType => XmlValueType.UvSlotIndex;

    public XmlValidationResult Validate(string rawValue, XmlTagDefinition tag)
    {
        if (!int.TryParse(rawValue.Trim(), out var value) || value < 0 || value > 3)
            return XmlValidationResult.Failure(
                $"'{rawValue.Trim()}' is not a valid UV slot index for <{tag.Tag}>. Expected an integer in [0, 3].");
        return XmlValidationResult.Valid();
    }
}

// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Validation;

namespace PG.StarWarsGame.LSP.Xml.Validation.Validators;

public sealed class SfxCountValidator : IXmlValueValidator
{
    public XmlValueType ValueType => XmlValueType.SfxCount;

    public XmlValidationResult Validate(string rawValue, XmlTagDefinition tag)
    {
        if (!int.TryParse(rawValue.Trim(), out var value) || value < -1)
            return XmlValidationResult.Failure(
                $"'{rawValue.Trim()}' is not a valid SFX count for <{tag.Tag}>. Expected -1 or a non-negative integer.");
        return XmlValidationResult.Valid();
    }
}

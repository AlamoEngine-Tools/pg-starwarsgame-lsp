// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Validation;

namespace PG.StarWarsGame.LSP.Xml.Validation.Validators;

public sealed class SfxPercentageValidator : IXmlValueValidator
{
    public XmlValueType ValueType => XmlValueType.SfxPercentage;

    public XmlValidationResult Validate(string rawValue, XmlTagDefinition tag)
    {
        if (!int.TryParse(rawValue.Trim(), out var value) || value < 0 || value > 100)
            return XmlValidationResult.Failure(
                $"'{rawValue.Trim()}' is not a valid SFX percentage for <{tag.Tag}>. Expected an integer in [0, 100].");
        return XmlValidationResult.Valid();
    }
}
// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Validation;

namespace PG.StarWarsGame.LSP.Xml.Validation.Validators;

public sealed class BooleanValueValidator : IXmlValueValidator
{
    private static readonly HashSet<string> ValidValues =
        new(StringComparer.OrdinalIgnoreCase) { "true", "false", "yes", "no", "1", "0" };

    public XmlValueType ValueType => XmlValueType.Boolean;

    public XmlValidationResult Validate(string rawValue, XmlTagDefinition tag)
    {
        var trimmed = rawValue.Trim();
        return ValidValues.Contains(trimmed)
            ? XmlValidationResult.Valid()
            : XmlValidationResult.Failure(
                $"'{trimmed}' is not a valid Boolean for <{tag.Tag}>. Expected: True, False, Yes or No.");
    }
}
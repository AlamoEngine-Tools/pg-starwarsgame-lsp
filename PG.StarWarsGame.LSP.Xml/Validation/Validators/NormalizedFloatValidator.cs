// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Globalization;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Validation;

namespace PG.StarWarsGame.LSP.Xml.Validation.Validators;

public sealed class NormalizedFloatValidator : IXmlValueValidator
{
    public XmlValueType ValueType => XmlValueType.NormalizedFloat;

    public XmlValidationResult Validate(string rawValue, XmlTagDefinition tag)
    {
        var trimmed = rawValue.Trim().TrimEnd('f', 'F');
        if (!double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
            return XmlValidationResult.Failure($"'{rawValue.Trim()}' is not a valid number for <{tag.Tag}>.");
        if (d is < 0.0 or > 1.0)
            return XmlValidationResult.Failure($"Value {d} is out of range [0, 1] for <{tag.Tag}>.");
        return XmlValidationResult.Valid();
    }
}
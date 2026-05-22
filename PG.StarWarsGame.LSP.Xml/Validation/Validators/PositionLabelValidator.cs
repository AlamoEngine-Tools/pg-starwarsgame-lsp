// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Validation;

namespace PG.StarWarsGame.LSP.Xml.Validation.Validators;

public sealed class PositionLabelValidator : IXmlValueValidator
{
    public XmlValueType ValueType => XmlValueType.PositionLabel;

    public XmlValidationResult Validate(string rawValue, XmlTagDefinition tag)
    {
        if (rawValue.Trim().Length == 0)
            return XmlValidationResult.Failure($"'' is not a valid position label for <{tag.Tag}>.");
        return XmlValidationResult.Valid();
    }
}
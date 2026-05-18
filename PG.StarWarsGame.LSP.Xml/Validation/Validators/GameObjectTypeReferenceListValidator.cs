// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Validation;

namespace PG.StarWarsGame.LSP.Xml.Validation.Validators;

public sealed class GameObjectTypeReferenceListValidator : IXmlValueValidator
{
    public XmlValueType ValueType => XmlValueType.GameObjectTypeReferenceList;

    public XmlValidationResult Validate(string rawValue, XmlTagDefinition tag)
    {
        var trimmed = rawValue.Trim();
        if (trimmed.Length == 0)
            return XmlValidationResult.Failure($"'' is not a valid game object type reference list for <{tag.Tag}>.");
        return XmlValidationResult.Valid();
    }
}

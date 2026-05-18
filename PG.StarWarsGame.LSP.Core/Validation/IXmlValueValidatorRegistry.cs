// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Core.Validation;

public interface IXmlValueValidatorRegistry
{
    XmlValidationResult Validate(XmlValueType valueType, string rawValue, XmlTagDefinition tag);
}
// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Xml.Validation;

public interface IXmlStructuralValidator
{
    IReadOnlyList<XmlStructureError> Validate(string text);
}

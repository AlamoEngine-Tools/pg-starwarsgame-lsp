// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

public sealed class TypeReferenceListHandler : NonEmptyReferenceHandlerBase
{
    protected override XmlValueType TargetType => XmlValueType.TypeReferenceList;
    protected override string ReferenceNoun => "type reference list";
}
// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>Validates <c>SFXEvent_Weather_Begin</c> / <c>SFXEvent_Weather_End</c> — non-empty SFX event references.</summary>
public sealed class Type37Handler : NonEmptyReferenceHandlerBase
{
    protected override XmlValueType TargetType => XmlValueType.Type37;
    protected override string ReferenceNoun => "SFX event";
}
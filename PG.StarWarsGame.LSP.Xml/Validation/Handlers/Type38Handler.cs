// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;

using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>Validates <c>Ambient_SFXEvent_Intermittent</c> - a non-empty SFX event reference.</summary>
public sealed class Type38Handler : NonEmptyReferenceHandlerBase
{
    /// <inheritdoc />
    public override DiagnosticId? DefaultId => DiagnosticIds.Type38;

    protected override XmlValueType TargetType => XmlValueType.Type38;
    protected override string ReferenceNoun => "SFX event";
}
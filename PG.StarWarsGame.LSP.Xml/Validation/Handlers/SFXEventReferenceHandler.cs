// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

public sealed class SFXEventReferenceHandler : NonEmptyReferenceHandlerBase
{
    /// <inheritdoc />
    public override DiagnosticId? DefaultId => DiagnosticIds.SFXEventReference;

    protected override XmlValueType TargetType => XmlValueType.SFXEventReference;
    protected override string ReferenceNoun => "SFX event reference";
}
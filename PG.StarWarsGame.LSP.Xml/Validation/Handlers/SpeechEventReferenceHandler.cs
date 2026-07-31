// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;

using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

public sealed class SpeechEventReferenceHandler : NonEmptyReferenceHandlerBase
{
    /// <inheritdoc />
    public override DiagnosticId? DefaultId => DiagnosticIds.SpeechEventReference;

    protected override XmlValueType TargetType => XmlValueType.SpeechEventReference;
    protected override string ReferenceNoun => "speech event reference";
}
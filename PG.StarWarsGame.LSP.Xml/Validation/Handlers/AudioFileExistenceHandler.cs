// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;

using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

public sealed class AudioFileExistenceHandler : AssetFileExistenceHandlerBase
{
    /// <inheritdoc />
    public override DiagnosticId? DefaultId => DiagnosticIds.AudioFileExistence;

    protected override ReferenceKind TargetKind => ReferenceKind.AudioFile;
    protected override string AssetNoun => "Audio";
    protected override IReadOnlyList<string> AllowedExtensions => [".wav", ".mp3"];
}
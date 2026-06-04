// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

public sealed class AudioFileExistenceHandler : AssetFileExistenceHandlerBase
{
    protected override ReferenceKind TargetKind => ReferenceKind.AudioFile;
    protected override string AssetNoun => "Audio";
    protected override IReadOnlyList<string> AllowedExtensions => [".wav", ".mp3"];
}

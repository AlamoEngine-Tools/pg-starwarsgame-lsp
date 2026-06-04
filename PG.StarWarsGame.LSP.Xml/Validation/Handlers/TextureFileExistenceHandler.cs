// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

public sealed class TextureFileExistenceHandler : AssetFileExistenceHandlerBase
{
    protected override ReferenceKind TargetKind => ReferenceKind.TextureFile;
    protected override string AssetNoun => "Texture";
    protected override IReadOnlyList<string> AllowedExtensions => [".tga", ".dds"];
}

// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

public sealed class ModelFileExistenceHandler : AssetFileExistenceHandlerBase
{
    protected override ReferenceKind TargetKind => ReferenceKind.ModelFile;
    protected override string AssetNoun => "Model";
    protected override IReadOnlyList<string> AllowedExtensions => [".alo"];
}

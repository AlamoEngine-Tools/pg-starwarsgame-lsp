// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using MessagePack;

namespace PG.StarWarsGame.LSP.Core.Symbols;

[MessagePackObject]
public sealed record MegArchiveOrigin(
    [property: Key(0)] string ArchivePath,
    [property: Key(1)] string InternalPath,
    [property: Key(2)] int? Line,
    [property: Key(3)] int? Column
) : SymbolOrigin;
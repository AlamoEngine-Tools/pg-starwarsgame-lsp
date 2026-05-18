// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using MessagePack;

namespace PG.StarWarsGame.LSP.Core.Symbols;

[MessagePackObject]
public sealed record GameSymbol(
    [property: Key(0)] string Id,
    [property: Key(1)] GameSymbolKind Kind,
    [property: Key(2)] string? TypeName,
    [property: Key(3)] SymbolOrigin Origin,
    [property: Key(4)] string? Description
);
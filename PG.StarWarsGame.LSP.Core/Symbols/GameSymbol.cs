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
    [property: Key(4)] string? Description,
    // Id of the base object this symbol is a variant of (from Variant_Of_Existing_Type), or null.
    // Nullable with a default so existing 5-arg construction and older MessagePack snapshots remain valid.
    [property: Key(5)] string? VariantBaseId = null
);
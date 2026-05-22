// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using MessagePack;

namespace PG.StarWarsGame.LSP.Core.Symbols;

[MessagePackObject]
public sealed record FileOrigin(
    [property: Key(0)] string Uri,
    [property: Key(1)] int Line,
    [property: Key(2)] int? Column
) : SymbolOrigin;
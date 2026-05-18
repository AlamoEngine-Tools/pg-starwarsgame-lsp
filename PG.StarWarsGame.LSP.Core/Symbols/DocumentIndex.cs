// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;

namespace PG.StarWarsGame.LSP.Core.Symbols;

public sealed record DocumentIndex(
    string DocumentUri,
    int Version,
    ImmutableArray<GameSymbol> Symbols,
    ImmutableArray<GameReference> References
);
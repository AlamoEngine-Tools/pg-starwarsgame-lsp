// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;

namespace PG.StarWarsGame.LSP.Core.Symbols;

public sealed record DocumentIndex(
    string DocumentUri,
    int Version,
    ImmutableArray<GameSymbol> Symbols,
    ImmutableArray<GameReference> References,
    ImmutableArray<string> RequireArgs = default,
    ImmutableArray<DocumentGroupMembership> GroupMemberships = default,
    // Precedence rank of the owning project layer (dependencies low, root project highest). Drives
    // same-id override resolution in GameIndex; 0 by default so existing construction stays valid.
    int LayerRank = 0,
    // Display name of the owning project layer, used in the "overrides X from <project>" hint.
    string? LayerName = null
);
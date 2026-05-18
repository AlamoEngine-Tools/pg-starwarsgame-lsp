// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;

namespace PG.StarWarsGame.LSP.Core.Symbols;

public sealed record BaselineIndex(
    ImmutableDictionary<string, GameSymbol> Symbols,
    DateTimeOffset BuiltAt,
    string SourceManifestHash,
    ImmutableDictionary<string, ImmutableArray<string>> DynamicEnumValues,
    ImmutableDictionary<string, ImmutableArray<string>> HardcodedEnumValues
)
{
    public static readonly BaselineIndex Empty = new(
        ImmutableDictionary<string, GameSymbol>.Empty,
        DateTimeOffset.MinValue,
        string.Empty,
        ImmutableDictionary<string, ImmutableArray<string>>.Empty,
        ImmutableDictionary<string, ImmutableArray<string>>.Empty);
}
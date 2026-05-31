// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using MessagePack;

namespace PG.StarWarsGame.LSP.Core.Symbols;

/// <summary>
///     Records that a specific XML object (identified by <see cref="MemberOrigin" />) belongs to a named reference group.
///     The group key is the shared tag value (e.g. the value of an <c>Overlap_Test</c> tag).
///     Multiple members share the same <see cref="GroupKey" /> to form a mutual-exclusion set.
/// </summary>
[MessagePackObject]
public sealed record GroupMembership(
    [property: Key(0)] string GroupKey,
    [property: Key(1)] string? MemberTypeName,
    [property: Key(2)] SymbolOrigin MemberOrigin
);
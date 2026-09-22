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
    [property: Key(5)] string? VariantBaseId = null,
    // The behaviour tokens this object declares ITSELF, from its own Behavior, SpaceBehavior and
    // LandBehavior tags; null when it declares none and for everything that is not a game object.
    // Read it through GameIndex.BehaviorsOf, never directly: a variant that declares none inherits
    // its base's whole list, and only the index can walk that chain.
    //
    // A plain array rather than an ImmutableArray because a GameSymbol is serialised by MessagePack
    // as-is, into both the baseline and the project index cache, and no immutable-collection
    // resolver is configured. Additive key, so both formats keep reading what was written before it.
    [property: Key(6)] string[]? Behaviors = null,
    // The boolean tags that are TRUE on this object, out of the ones some kind actually asks about
    // (Is_Named_Hero, Is_Generic_Hero today). The set comes from the schema's kinds, so adding a
    // kind that tests a new flag starts capturing it without a code change - which is also why the
    // schema fingerprint covers kinds, or a cached index would replay the old set forever.
    //
    // EMPTY means the object was inspected and has none. NULL means nobody looked - a baseline
    // built before flags - and a kind that rests on one then answers Unknown rather than "no",
    // because putting an error on every shipped hero is the worse way to be wrong.
    [property: Key(7)] string[]? Flags = null
);
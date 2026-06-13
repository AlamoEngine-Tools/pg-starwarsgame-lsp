// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;

namespace PG.StarWarsGame.LSP.Core.Symbols;

/// <summary>
///     The result of merging an object with its variant base chain. When <see cref="Found" /> is false the
///     object id is not in the index. When <see cref="Cyclic" /> is true the base chain loops back on
///     itself at <see cref="CycleObjectId" />; <see cref="Tags" /> then holds the partial merge up to the loop.
/// </summary>
/// <param name="ObjectId">The canonical id of the resolved object.</param>
/// <param name="TypeName">The resolved object's GameObject type, or null.</param>
/// <param name="Found">Whether the object id resolved to a symbol.</param>
/// <param name="Cyclic">Whether a cycle was detected while walking the base chain.</param>
/// <param name="CycleObjectId">The id at which the chain looped, or null.</param>
/// <param name="Chain">The base chain from most-derived (this object) to the root base.</param>
/// <param name="Tags">The effective tag set, in first-seen order.</param>
public sealed record EffectiveObject(
    string ObjectId,
    string? TypeName,
    bool Found,
    bool Cyclic,
    string? CycleObjectId,
    ImmutableArray<string> Chain,
    ImmutableArray<EffectiveTag> Tags
);

// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Symbols;

/// <summary>
///     Supplies the direct child tags of an object by id. Implemented per layer: a workspace source
///     (backed by live XML documents) and, internally to the resolver, the baseline source (backed by
///     <see cref="BaselineIndex.ObjectTags" />). Workspace tags shadow baseline tags for the same id.
/// </summary>
public interface IVariantTagSource
{
    /// <summary>Returns the object's direct child tags, or null when this source does not know the object.</summary>
    IReadOnlyList<VariantTag>? TryGetTags(string objectId);
}
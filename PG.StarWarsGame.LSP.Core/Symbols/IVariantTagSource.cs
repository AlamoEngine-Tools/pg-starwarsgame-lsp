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

    /// <summary>
    ///     The same lookup, scoped to the project owning <paramref name="contextUri" />. An object id
    ///     is only unique within a project - two unrelated mods can both define
    ///     <c>REBEL_TROOPER</c> - so a bare id has to be resolved against the project the request
    ///     came from. Passing null (or omitting it) keeps the primary project's answer, which is the
    ///     single-project behaviour and what the default implementation preserves for test fakes.
    /// </summary>
    IReadOnlyList<VariantTag>? TryGetTags(string objectId, string? contextUri)
    {
        return TryGetTags(objectId);
    }
}
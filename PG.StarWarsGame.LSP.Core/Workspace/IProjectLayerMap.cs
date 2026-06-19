// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Workspace;

/// <summary>
///     Maps a document URI to the precedence <c>Rank</c> of the project layer that owns it, so the
///     index can resolve same-id collisions in favour of the highest layer (workspace over its
///     dependencies over the baseline). Mutable shared state, repopulated on every workspace
///     (re)load via <see cref="SetLayers" />, mirroring <see cref="EaWXmlContext" />.
/// </summary>
public interface IProjectLayerMap
{
    /// <summary>Replaces the current layer set (e.g. after a project reload).</summary>
    void SetLayers(IReadOnlyList<ProjectLayer> layers);

    /// <summary>
    ///     Precedence rank of the layer whose directories contain <paramref name="fileUri" />
    ///     (longest-prefix match). Files not under any layer default to the highest known rank so an
    ///     ad-hoc opened file still wins. Returns 0 when no layers are set.
    /// </summary>
    int GetRank(string fileUri);

    /// <summary>Display name of the layer with the given rank, or <see langword="null" />.</summary>
    string? GetLayerName(int rank);
}
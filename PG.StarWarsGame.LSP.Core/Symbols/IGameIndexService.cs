// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Localisation;

namespace PG.StarWarsGame.LSP.Core.Symbols;

public interface IGameIndexService
{
    GameIndex Current { get; }

    Task UpdateDocumentAsync(string uri, string text, int version, CancellationToken ct);
    void RemoveDocument(string uri);
    void ApplyBaseline(BaselineIndex baseline);
    void ApplyLocalisation(ILocalisationIndex index);

    /// <summary>
    ///     Suppresses <see cref="IndexChanged" /> while the returned scope is alive.
    ///     Fires exactly one <see cref="IndexChanged" /> on <see cref="IDisposable.Dispose" />
    ///     if at least one update occurred. Used by the workspace scanner to avoid
    ///     O(N²) diagnostic republishing during bulk initial indexing.
    /// </summary>
    IDisposable BeginBulkUpdate();

    // Fires on the thread that completes a CAS write. Subscribers must not do heavy work
    // inline; queue a background diagnostics publish instead.
    event Action<GameIndex>? IndexChanged;
}
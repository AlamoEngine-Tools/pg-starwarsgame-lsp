// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Localisation;

namespace PG.StarWarsGame.LSP.Core.Symbols;

public interface IGameIndexService
{
    GameIndex Current { get; }

    Task UpdateDocumentAsync(string uri, string text, int version, CancellationToken ct);

    /// <summary>
    ///     Applies a pre-built <see cref="DocumentIndex" /> (e.g. from a project index cache) without
    ///     re-parsing. The document's <see cref="DocumentIndex.LayerRank" />, <see cref="DocumentIndex.LayerName" />,
    ///     and all symbol/reference data are preserved as-is.
    /// </summary>
    void InjectDocument(DocumentIndex document);

    void RemoveDocument(string uri);
    void ApplyBaseline(BaselineIndex baseline);
    void ApplyLocalisation(ILocalisationIndex index);
    void ApplyAssetFiles(IAssetFileIndex index);
    void ApplyModelBones(ImmutableDictionary<string, ImmutableArray<string>> bones);
    void ApplyWorkspaceDynamicEnumValues(ImmutableDictionary<string, ImmutableArray<string>> values);
    void ApplyWorkspaceEnumValueDefinitions(
        ImmutableDictionary<string, ImmutableDictionary<string, FileOrigin>> definitions);

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
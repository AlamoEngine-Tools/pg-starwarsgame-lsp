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
    ///     Indexes a freshly opened document (didOpen). Unlike <see cref="UpdateDocumentAsync" />,
    ///     the committed version never suppresses this update: LSP client versions restart at 1 for
    ///     every open session, while the committed version deliberately survives a didClose
    ///     re-index — so a didOpen starts a new version epoch. An unchanged-content open skips the
    ///     re-parse but still re-stamps the stored version, so the new session's subsequent
    ///     didChanges are not dropped as stale.
    ///     Default implementation delegates to <see cref="UpdateDocumentAsync" /> so existing test
    ///     fakes keep working without changes.
    /// </summary>
    Task OpenDocumentAsync(string uri, string text, int version, CancellationToken ct)
    {
        return UpdateDocumentAsync(uri, text, version, ct);
    }

    /// <summary>
    ///     Applies a pre-built <see cref="DocumentIndex" /> (e.g. from a project index cache) without
    ///     re-parsing. All symbol/reference data is preserved as-is;
    ///     <see cref="DocumentIndex.LayerRank" /> and <see cref="DocumentIndex.LayerName" /> are
    ///     re-stamped from the live layer map when one is registered (snapshot ranks go stale when
    ///     the dependency graph changes between sessions) and preserved only in map-less setups.
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

    // Fires only on ApplyLocalisation — never on document/asset/enum/baseline updates. Lets
    // subscribers that only care about localisation (e.g. the client-facing
    // aet/localisationIndexUpdated notification) avoid reacting to unrelated workspace churn.
    // Not suppressed by BeginBulkUpdate: localisation applies are coarse-grained (once per
    // reload), so the O(N²) concern that suppression exists for doesn't apply here.
    event Action<ILocalisationIndex>? LocalisationChanged;

    // Fires only on ApplyBaseline and ApplyWorkspaceDynamicEnumValues — the two calls that can
    // change the merged dynamic-enum value set. Lets subscribers that cache dynamic-enum
    // completion candidates (see DynamicEnumValueProposalProvider) invalidate precisely instead
    // of on every unrelated document edit via IndexChanged. Not suppressed by BeginBulkUpdate:
    // both applies are coarse-grained (once per project load/reload or per changed enum source
    // file), not per-document.
    event Action<GameIndex>? DynamicEnumChanged;
}
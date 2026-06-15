// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Localisation;

namespace PG.StarWarsGame.LSP.Core.Symbols;

public sealed record GameIndex(
    BaselineIndex Baseline,
    ImmutableDictionary<string, DocumentIndex> Documents,
    ImmutableDictionary<string, ImmutableArray<GameSymbol>> WorkspaceDefinitions,
    ImmutableDictionary<string, ImmutableArray<GameReference>> WorkspaceReferences
)
{
    // WorkspaceDefinitions and WorkspaceReferences are keyed by game object Name, which
    // the engine resolves case-insensitively. Use OrdinalIgnoreCase so "X-wing" and
    // "X-Wing" always refer to the same slot. Documents is keyed by canonical URI
    // (already lowercased by IFileHelper.NormalizeUri) and stays ordinal.
    public static readonly GameIndex Empty = new(
        BaselineIndex.Empty,
        ImmutableDictionary<string, DocumentIndex>.Empty,
        ImmutableDictionary.Create<string, ImmutableArray<GameSymbol>>(StringComparer.OrdinalIgnoreCase),
        ImmutableDictionary.Create<string, ImmutableArray<GameReference>>(StringComparer.OrdinalIgnoreCase));

    /// <summary>
    ///     Group memberships aggregated across all workspace documents. Keyed case-insensitively by
    ///     <see cref="GroupMembership.GroupKey" />. Multiple <see cref="GroupMembership" /> entries under
    ///     the same key represent all objects that belong to that reference group.
    /// </summary>
    public ImmutableDictionary<string, ImmutableArray<GroupMembership>> WorkspaceGroupMemberships { get; init; } =
        ImmutableDictionary.Create<string, ImmutableArray<GroupMembership>>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    ///     Merged (baseline ∪ workspace) localisation key set. Used by
    ///     <c>LocalisationKeyExistenceHandler</c> and <c>LocalisationKeyCompletionProvider</c>.
    /// </summary>
    public ILocalisationIndex Localisation { get; init; } = EmptyLocalisationIndex.Instance;

    /// <summary>
    ///     Merged (baseline ∪ workspace) asset-file catalog. Used by <c>AssetFileExistenceHandler</c>
    ///     and <c>AssetFileCompletionProvider</c> to validate and complete <c>textureFile</c>,
    ///     <c>modelFile</c>, <c>audioFile</c> and <c>mapFile</c> references.
    /// </summary>
    public IAssetFileIndex AssetFiles { get; init; } = EmptyAssetFileIndex.Instance;

    /// <summary>
    ///     Merged (baseline ∪ workspace) bone-name catalog, keyed by normalised <c>.alo</c> model path
    ///     (lowercase, forward-slash). Used by <c>BoneNameCompletionHelper</c> to complete
    ///     <c>boneName</c> references against the model(s) referenced by sibling model tags.
    /// </summary>
    public ImmutableDictionary<string, ImmutableArray<string>> ModelBones { get; init; } =
        ImmutableDictionary.Create<string, ImmutableArray<string>>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    ///     Merged (baseline ∪ workspace) group memberships. Prefer this over
    ///     <see cref="WorkspaceGroupMemberships" /> in LSP handlers so shipped-game members are included
    ///     alongside mod-workspace members when resolving group keys.
    /// </summary>
    public ImmutableDictionary<string, ImmutableArray<GroupMembership>> AllGroupMemberships
    {
        get
        {
            if (Baseline.GroupMemberships.IsEmpty) return WorkspaceGroupMemberships;
            if (WorkspaceGroupMemberships.IsEmpty) return Baseline.GroupMemberships;

            var result = WorkspaceGroupMemberships;
            foreach (var (key, baselineMembers) in Baseline.GroupMemberships)
                result = result.TryGetValue(key, out var ws)
                    ? result.SetItem(key, ws.AddRange(baselineMembers))
                    : result.Add(key, baselineMembers);
            return result;
        }
    }

    public GameSymbol? Resolve(string id)
    {
        if (WorkspaceDefinitions.TryGetValue(id, out var ws) && ws.Length > 0)
            // Highest project layer wins. OrderByDescending is stable, so a same-rank collision
            // (a real duplicate) deterministically yields the first-inserted definition.
            return ws.Length == 1 ? ws[0] : ws.OrderByDescending(LayerRankOf).First();
        return Baseline.Symbols.GetValueOrDefault(id);
    }

    // Workspace definitions ordered highest layer first, then the baseline. Multiple workspace
    // entries at the SAME rank are a duplicate-ID error; the diagnostics publisher uses this to find
    // all offending sites. Cross-layer entries are valid overrides.
    public IEnumerable<GameSymbol> ResolveAll(string id)
    {
        if (WorkspaceDefinitions.TryGetValue(id, out var ws))
            foreach (var s in ws.OrderByDescending(LayerRankOf))
                yield return s;
        if (Baseline.Symbols.TryGetValue(id, out var b))
            yield return b;
    }

    public (GameSymbol Winner, GameSymbol? Shadowed)? ResolveWithShadow(string id)
    {
        var winner = Resolve(id);
        if (winner is null) return null;

        // The shadowed definition is the next layer below the winner: a lower-ranked workspace
        // definition if one exists, otherwise the baseline symbol.
        GameSymbol? shadowed = null;
        if (WorkspaceDefinitions.TryGetValue(id, out var ws) && ws.Length > 1)
        {
            var winnerRank = LayerRankOf(winner);
            shadowed = ws.OrderByDescending(LayerRankOf).FirstOrDefault(s => LayerRankOf(s) < winnerRank);
        }

        shadowed ??= Baseline.Symbols.GetValueOrDefault(id);

        // Only report a shadow when it is a different object than the winner.
        return (winner, !ReferenceEquals(winner, shadowed) ? shadowed : null);
    }

    // Precedence rank of the project layer that owns a workspace symbol, read from its originating
    // document. Symbols not tied to a tracked document (e.g. baseline) resolve to 0.
    public int LayerRankOf(GameSymbol symbol)
    {
        return symbol.Origin is FileOrigin fo && Documents.TryGetValue(fo.Uri, out var doc)
            ? doc.LayerRank
            : 0;
    }
}
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
    ///     Dynamic enum values discovered by scanning the workspace XML enum files — values the mod has
    ///     added or overridden beyond the shipped baseline. Keyed case-insensitively by enum name
    ///     (e.g. <c>SurfaceFXTriggerType</c>). Unioned with <see cref="BaselineIndex.DynamicEnumValues" />
    ///     by <c>NamedEnumValueHandlerBase</c> so workspace-defined values are never flagged as unknown.
    /// </summary>
    public ImmutableDictionary<string, ImmutableArray<string>> WorkspaceDynamicEnumValues { get; init; } =
        ImmutableDictionary.Create<string, ImmutableArray<string>>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    ///     File origins for individual values in workspace-defined <see cref="EnumKind.DynamicXml" /> enum
    ///     files. Outer key: enum name (case-insensitive, e.g. <c>SurfaceFXTriggerType</c>).
    ///     Inner key: value name (case-insensitive). Used by go-to-definition on enum-value references.
    /// </summary>
    public ImmutableDictionary<string, ImmutableDictionary<string, FileOrigin>> WorkspaceEnumValueDefinitions
    { get; init; } =
        ImmutableDictionary.Create<string, ImmutableDictionary<string, FileOrigin>>(
            StringComparer.OrdinalIgnoreCase);

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

    // The highest LayerRank among all indexed documents; 0 when no documents exist.
    public int LeafLayerRank =>
        Documents.Count == 0 ? 0 : Documents.Values.Max(d => d.LayerRank);

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

    // Same lookup as LayerRankOf, but keyed directly by URI — for origins that don't carry a
    // GameSymbol (e.g. WorkspaceEnumValueDefinitions' FileOrigin). Every workspace .xml file is
    // indexed as a Document regardless of whether it defines any symbols (XmlGameDocumentParser
    // accepts any .xml), so dynamic-enum source files land here with a correctly stamped rank too.
    public int LayerRankOfUri(string uri)
    {
        return Documents.TryGetValue(uri, out var doc) ? doc.LayerRank : 0;
    }

    // True iff every workspace definition of <id> is a FileOrigin symbol in the leaf layer.
    // Returns false when the id is absent, any definition has a non-FileOrigin origin, or any
    // definition lives in a dependency layer (rank < LeafLayerRank).
    public bool IsLeafOwned(string id)
    {
        return WorkspaceDefinitions.TryGetValue(id, out var defs)
               && defs.Length > 0
               && defs.All(s => s.Origin is FileOrigin && LayerRankOf(s) == LeafLayerRank);
    }

    // True iff the given symbol belongs to the leaf (highest-rank) layer.
    public bool IsLeafSymbol(GameSymbol symbol)
    {
        return LayerRankOf(symbol) == LeafLayerRank;
    }

    // Resolve with optional type preference. Among all definitions of <id>, prefer those whose
    // TypeName matches <preferredTypeName> (highest rank among type-matches); fall back to the
    // untyped winner if no type-match exists.
    public GameSymbol? Resolve(string id, string? preferredTypeName)
    {
        if (preferredTypeName is null) return Resolve(id);

        if (WorkspaceDefinitions.TryGetValue(id, out var ws) && ws.Length > 0)
        {
            var typeMatch = ws
                .Where(s => string.Equals(s.TypeName, preferredTypeName, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(LayerRankOf)
                .FirstOrDefault();
            if (typeMatch is not null) return typeMatch;
        }

        if (Baseline.Symbols.TryGetValue(id, out var b) &&
            string.Equals(b.TypeName, preferredTypeName, StringComparison.OrdinalIgnoreCase))
            return b;

        // No type-matched definition found — fall back to untyped resolution so TypeMismatchHandler
        // fires rather than UnresolvedReferenceHandler.
        return Resolve(id);
    }
}
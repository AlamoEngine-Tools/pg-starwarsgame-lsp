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
            return ws[0];
        return Baseline.Symbols.GetValueOrDefault(id);
    }

    // Array length > 1 in WorkspaceDefinitions is a duplicate-ID error, not a valid
    // pattern. ResolveAll is used by the diagnostics publisher to find all offending sites.
    public IEnumerable<GameSymbol> ResolveAll(string id)
    {
        if (WorkspaceDefinitions.TryGetValue(id, out var ws))
            foreach (var s in ws)
                yield return s;
        if (Baseline.Symbols.TryGetValue(id, out var b))
            yield return b;
    }

    public (GameSymbol Winner, GameSymbol? Shadowed)? ResolveWithShadow(string id)
    {
        var winner = Resolve(id);
        if (winner is null) return null;
        var shadowed = Baseline.Symbols.GetValueOrDefault(id);
        // Only report as shadowed if the winner came from workspace (different object reference)
        return (winner, !ReferenceEquals(winner, shadowed) ? shadowed : null);
    }
}
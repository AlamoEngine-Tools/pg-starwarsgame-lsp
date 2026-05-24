// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;

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
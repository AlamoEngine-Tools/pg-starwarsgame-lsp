using System.Collections.Immutable;

namespace PG.StarWarsGame.LSP.Core.Symbols;

public sealed record GameIndex(
    BaselineIndex Baseline,
    ImmutableDictionary<string, DocumentIndex> Documents,
    ImmutableDictionary<string, ImmutableArray<GameSymbol>> WorkspaceDefinitions,
    ImmutableDictionary<string, ImmutableArray<GameReference>> WorkspaceReferences
)
{
    public static readonly GameIndex Empty = new(
        BaselineIndex.Empty,
        ImmutableDictionary<string, DocumentIndex>.Empty,
        ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty,
        ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty);

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
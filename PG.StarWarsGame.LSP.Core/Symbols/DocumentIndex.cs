using System.Collections.Immutable;

namespace PG.StarWarsGame.LSP.Core.Symbols;

public sealed record DocumentIndex(
    string DocumentUri,
    int Version,
    ImmutableArray<GameSymbol> Symbols,
    ImmutableArray<GameReference> References
);
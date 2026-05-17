using System.Collections.Immutable;

namespace PG.StarWarsGame.LSP.Core.Symbols;

public sealed record BaselineIndex(
    ImmutableDictionary<string, GameSymbol> Symbols,
    DateTimeOffset                          BuiltAt,
    string                                  SourceManifestHash
)
{
    public static readonly BaselineIndex Empty = new(
        ImmutableDictionary<string, GameSymbol>.Empty,
        DateTimeOffset.MinValue,
        string.Empty);
}

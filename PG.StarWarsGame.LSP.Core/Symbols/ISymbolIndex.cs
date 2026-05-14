namespace PG.StarWarsGame.LSP.Core.Symbols;

public interface ISymbolIndex
{
    IReadOnlyList<GameSymbol> All { get; }
    IReadOnlyList<GameSymbol> Lookup(string name);
    IReadOnlyList<GameSymbol> LookupByType(string typeName);
}

public record GameSymbol
{
    public required string Name { get; init; }
    public required string TypeName { get; init; }
    public required SymbolLocation Location { get; init; }
}

public record SymbolLocation(string FilePath, int Line);
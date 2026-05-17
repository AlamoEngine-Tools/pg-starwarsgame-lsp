namespace PG.StarWarsGame.LSP.Core.Symbols;

public interface ISymbolIndex
{
    IReadOnlyList<IndexedSymbol> All { get; }
    IReadOnlyList<IndexedSymbol> Lookup(string name);
    IReadOnlyList<IndexedSymbol> LookupByType(string typeName);
}

// Temporary scaffold — deleted in Chunk 9 cleanup.
public record IndexedSymbol
{
    public required string Name { get; init; }
    public required string TypeName { get; init; }
    public required IndexedSymbolLocation Location { get; init; }
}

public record IndexedSymbolLocation(string FilePath, int Line);
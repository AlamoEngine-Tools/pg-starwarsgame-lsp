using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Assets;

public sealed class SymbolIndex : ISymbolIndex
{
    private readonly Dictionary<string, List<IndexedSymbol>> _byName =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, List<IndexedSymbol>> _byType =
        new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<IndexedSymbol> All =>
        _byName.Values.SelectMany(v => v).ToList();

    public IReadOnlyList<IndexedSymbol> Lookup(string name)
    {
        return _byName.TryGetValue(name, out var list) ? list : [];
    }

    public IReadOnlyList<IndexedSymbol> LookupByType(string typeName)
    {
        return _byType.TryGetValue(typeName, out var list) ? list : [];
    }

    internal void Add(IndexedSymbol symbol)
    {
        AddTo(_byName, symbol.Name, symbol);
        AddTo(_byType, symbol.TypeName, symbol);
    }

    private static void AddTo(Dictionary<string, List<IndexedSymbol>> dict, string key, IndexedSymbol symbol)
    {
        if (!dict.TryGetValue(key, out var list))
        {
            list = [];
            dict[key] = list;
        }

        list.Add(symbol);
    }
}
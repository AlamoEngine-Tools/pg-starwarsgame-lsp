using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Assets;

public sealed class SymbolIndex : ISymbolIndex
{
    private readonly Dictionary<string, List<GameSymbol>> _byName =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, List<GameSymbol>> _byType =
        new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<GameSymbol> All =>
        _byName.Values.SelectMany(v => v).ToList();

    public IReadOnlyList<GameSymbol> Lookup(string name)
    {
        return _byName.TryGetValue(name, out var list) ? list : [];
    }

    public IReadOnlyList<GameSymbol> LookupByType(string typeName)
    {
        return _byType.TryGetValue(typeName, out var list) ? list : [];
    }

    internal void Add(GameSymbol symbol)
    {
        AddTo(_byName, symbol.Name, symbol);
        AddTo(_byType, symbol.TypeName, symbol);
    }

    private static void AddTo(Dictionary<string, List<GameSymbol>> dict, string key, GameSymbol symbol)
    {
        if (!dict.TryGetValue(key, out var list))
        {
            list = [];
            dict[key] = list;
        }

        list.Add(symbol);
    }
}
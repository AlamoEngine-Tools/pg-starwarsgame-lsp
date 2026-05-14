using Microsoft.Extensions.Logging;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Assets;

public sealed class BaselinePopulator
{
    private readonly ILogger<BaselinePopulator> _logger;

    public BaselinePopulator(ILogger<BaselinePopulator> logger)
    {
        _logger = logger;
    }

    public void PopulateFromBaseline(ISymbolIndex index, SerializedBaseline baseline)
    {
        if (index is not SymbolIndex symbolIndex)
            throw new ArgumentException("Expected a SymbolIndex instance.", nameof(index));

        _logger.LogDebug("Populating symbol index from baseline ({SymbolCount} symbols)", baseline.Symbols.Count);

        foreach (var sym in baseline.Symbols)
            symbolIndex.Add(new GameSymbol
            {
                Name = sym.Name,
                TypeName = sym.TypeName,
                Location = new SymbolLocation(sym.FilePath, sym.Line)
            });

        _logger.LogInformation("Symbol index populated: {Count} symbols added", baseline.Symbols.Count);
    }
}
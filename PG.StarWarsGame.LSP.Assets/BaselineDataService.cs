using Microsoft.Extensions.Logging;
using PG.StarWarsGame.Engine;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Assets;

/// <summary>
///     Fallback scanner: initializes the game engine from a local installation
///     and populates the symbol index. Used only when no pre-built baseline is available.
/// </summary>
public sealed class BaselineDataService
{
    private readonly ILspConfigurationProvider _config;
    private readonly IPetroglyphStarWarsGameEngineService _engineService;
    private readonly SymbolIndex _index;
    private readonly ILogger<BaselineDataService> _logger;

    public BaselineDataService(
        ILspConfigurationProvider config,
        ISymbolIndex index,
        IPetroglyphStarWarsGameEngineService engineService,
        ILogger<BaselineDataService> logger)
    {
        _config = config;
        _index = (SymbolIndex)index;
        _engineService = engineService;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        var cfg = _config.Current;

        if (string.IsNullOrWhiteSpace(cfg.GamePath))
        {
            _logger.LogDebug("No game path configured; skipping engine scan");
            return;
        }

        _logger.LogDebug("Initialising game engine from {GamePath}", cfg.GamePath);

        var locations = new GameLocations(
            cfg.ModPaths.ToList(),
            cfg.GamePath,
            Array.Empty<string>());

        var engine = await _engineService.InitializeAsync(
            GameEngineType.Foc, locations, cancellationToken: ct);

        var count = 0;
        foreach (var entry in engine.GameObjectTypeManager.Entries)
        {
            _index.Add(new GameSymbol
            {
                Name = entry.Name,
                TypeName = entry.ClassificationName,
                Location = new SymbolLocation(
                    entry.Location.XmlFile ?? string.Empty,
                    entry.Location.Line ?? 0)
            });
            count++;
        }

        _logger.LogInformation("Engine scan complete: {Count} symbols indexed from {GamePath}", count, cfg.GamePath);
    }
}
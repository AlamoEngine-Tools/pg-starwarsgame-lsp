using System.Text.Json;
using Microsoft.Extensions.Logging;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Assets.Baseline;

public sealed class LocalBaselineProvider : IBaselineProvider
{
    private readonly ILspConfigurationProvider _config;
    private readonly ILogger<LocalBaselineProvider> _logger;

    public LocalBaselineProvider(ILspConfigurationProvider config, ILogger<LocalBaselineProvider> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task<SerializedBaseline?> LoadAsync(CancellationToken ct = default)
    {
        var path = _config.Current.BaselineSource.LocalPath;

        if (string.IsNullOrWhiteSpace(path))
        {
            _logger.LogDebug("No local baseline path configured");
            return null;
        }

        if (!File.Exists(path))
        {
            _logger.LogWarning("Local baseline file not found at {Path}", path);
            return null;
        }

        _logger.LogDebug("Loading local baseline from {Path}", path);
        await using var stream = File.OpenRead(path);
        var result = await JsonSerializer.DeserializeAsync<SerializedBaseline>(stream, cancellationToken: ct);

        _logger.LogInformation("Local baseline loaded: {SymbolCount} symbols from {Path}",
            result?.Symbols.Count ?? 0, path);
        return result;
    }
}
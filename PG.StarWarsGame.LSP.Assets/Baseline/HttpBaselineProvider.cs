using System.Text.Json;
using Microsoft.Extensions.Logging;
using PG.StarWarsGame.LSP.Assets.Cache;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Assets.Baseline;

public sealed class HttpBaselineProvider : IBaselineProvider
{
    private readonly BaselineHttpCache _cache;
    private readonly ILspConfigurationProvider _config;
    private readonly HttpClient _http;
    private readonly ILogger<HttpBaselineProvider> _logger;

    public HttpBaselineProvider(
        HttpClient http,
        ILspConfigurationProvider config,
        BaselineHttpCache cache,
        ILogger<HttpBaselineProvider> logger)
    {
        _http = http;
        _config = config;
        _cache = cache;
        _logger = logger;
    }

    public async Task<SerializedBaseline?> LoadAsync(CancellationToken ct = default)
    {
        var cfg = _config.Current.BaselineSource;
        var url = string.IsNullOrWhiteSpace(cfg.FocUrl) ? cfg.EawUrl : cfg.FocUrl;

        _logger.LogDebug("Fetching baseline from {Url}", url);
        try
        {
            var json = await _http.GetStringAsync(url, ct);

            if (_cache.TryLoad(json, out var cached))
            {
                _logger.LogInformation("Baseline loaded from local cache ({SymbolCount} symbols)",
                    cached!.Symbols.Count);
                return cached;
            }

            var baseline = JsonSerializer.Deserialize<SerializedBaseline>(json);
            if (baseline is not null)
            {
                _cache.Update(json);
                _logger.LogInformation("Baseline fetched and cached: {SymbolCount} symbols from {Url}",
                    baseline.Symbols.Count, url);
            }

            return baseline;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch baseline from {Url}", url);
            return null;
        }
    }
}
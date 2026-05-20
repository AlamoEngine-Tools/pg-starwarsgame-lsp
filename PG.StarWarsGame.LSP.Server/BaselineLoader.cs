// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions;
using Microsoft.Extensions.Logging;
using PG.StarWarsGame.LSP.Assets.Serialization;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Server;

public sealed class BaselineLoader
{
    private readonly IFileSystem _fs;
    private readonly HttpClient _httpClient;
    private readonly ILogger<BaselineLoader> _logger;

    private string CacheDir => _fs.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".pg-swg-lsp", "baselines");

    public BaselineLoader(HttpClient httpClient, IFileSystem fs, ILogger<BaselineLoader> logger)
    {
        _httpClient = httpClient;
        _fs = fs;
        _logger = logger;
    }

    public Task<BaselineIndex> LoadAsync(BaselineSourceConfig config, CancellationToken ct) =>
        config.Type switch
        {
            BaselineSourceType.Local => LoadLocalAsync(config.LocalPath, ct),
            BaselineSourceType.Http => LoadHttpAsync(config.FocUrl, ct),
            _ => Task.FromResult(BaselineIndex.Empty)
        };

    private async Task<BaselineIndex> LoadLocalAsync(string? path, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(path))
            return BaselineIndex.Empty;

        try
        {
            var bytes = await _fs.File.ReadAllBytesAsync(path, ct);
            return BaselineSerializer.Deserialize(bytes);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load local baseline from '{Path}'", path);
            return BaselineIndex.Empty;
        }
    }

    private async Task<BaselineIndex> LoadHttpAsync(string url, CancellationToken ct)
    {
        var cacheFile = _fs.Path.Combine(CacheDir, _fs.Path.GetFileName(url));

        try
        {
            var bytes = await _httpClient.GetByteArrayAsync(url, ct);
            _fs.Directory.CreateDirectory(CacheDir);
            await _fs.File.WriteAllBytesAsync(cacheFile, bytes, ct);
            return BaselineSerializer.Deserialize(bytes);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to download baseline from '{Url}'; trying cache", url);
        }

        if (_fs.File.Exists(cacheFile))
        {
            try
            {
                var cached = await _fs.File.ReadAllBytesAsync(cacheFile, ct);
                return BaselineSerializer.Deserialize(cached);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load cached baseline from '{Path}'", cacheFile);
            }
        }

        return BaselineIndex.Empty;
    }
}

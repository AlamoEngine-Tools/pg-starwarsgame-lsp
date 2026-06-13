// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using PG.StarWarsGame.LSP.Assets.Serialization;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;

namespace PG.StarWarsGame.LSP.Server;

public sealed class BaselineLoader
{
    private readonly IFileHelper _fileHelper;
    private readonly HttpClient _httpClient;
    private readonly ILogger<BaselineLoader> _logger;

    public BaselineLoader(HttpClient httpClient, IFileHelper fileHelper, ILogger<BaselineLoader> logger)
    {
        _httpClient = httpClient;
        _fileHelper = fileHelper;
        _logger = logger;
    }

    private string CacheDir => _fileHelper.FileSystem.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".pg-swg-lsp", "baselines");

    public Task<BaselineIndex> LoadAsync(BaselineSourceConfig config, CancellationToken ct)
    {
        return config.Type switch
        {
            BaselineSourceType.Local => LoadLocalAsync(config.LocalPath, ct),
            BaselineSourceType.Http => LoadHttpAsync(config.Url, ct),
            _ => Task.FromResult(BaselineIndex.Empty)
        };
    }

    private async Task<BaselineIndex> LoadLocalAsync(string? path, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(path))
            return BaselineIndex.Empty;

        try
        {
            var bytes = await _fileHelper.FileSystem.File.ReadAllBytesAsync(path, ct);
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
        var cacheFile = _fileHelper.FileSystem.Path.Combine(CacheDir, _fileHelper.FileSystem.Path.GetFileName(url));

        try
        {
            var bytes = await _httpClient.GetByteArrayAsync(url, ct);
            _fileHelper.FileSystem.Directory.CreateDirectory(CacheDir);
            await _fileHelper.FileSystem.File.WriteAllBytesAsync(cacheFile, bytes, ct);
            return BaselineSerializer.Deserialize(bytes);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to download baseline from '{Url}'; trying cache", url);
        }

        if (_fileHelper.FileSystem.File.Exists(cacheFile))
            try
            {
                var cached = await _fileHelper.FileSystem.File.ReadAllBytesAsync(cacheFile, ct);
                return BaselineSerializer.Deserialize(cached);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load cached baseline from '{Path}'", cacheFile);
            }

        return BaselineIndex.Empty;
    }
}
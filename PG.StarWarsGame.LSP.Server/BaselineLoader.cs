// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using PG.StarWarsGame.LSP.Assets.Serialization;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Server.Status;

namespace PG.StarWarsGame.LSP.Server;

public sealed class BaselineLoader
{
    private readonly IFileHelper _fileHelper;
    private readonly HttpClient _httpClient;
    private readonly ICrossProcessLock _lock;
    private readonly ILogger<BaselineLoader> _logger;
    private readonly ServerStatusRecorder? _status;

    public BaselineLoader(HttpClient httpClient, IFileHelper fileHelper, ILogger<BaselineLoader> logger,
        ServerStatusRecorder? status = null, ICrossProcessLock? processLock = null)
    {
        _httpClient = httpClient;
        _fileHelper = fileHelper;
        _logger = logger;
        _status = status;
        _lock = processLock ?? new NullCrossProcessLock();
    }

    private string CacheDir => _fileHelper.FileSystem.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".aetswg", "baselines");

    public async Task<BaselineIndex> LoadAsync(BaselineSourceConfig config, CancellationToken ct)
    {
        var (baseline, source) = await (config.Type switch
        {
            BaselineSourceType.Local => LoadLocalAsync(config.LocalPath, ct),
            BaselineSourceType.Http => LoadHttpAsync(config.Url, ct),
            _ => Task.FromResult((BaselineIndex.Empty, StatusAssetSource.Empty))
        });

        _status?.RecordBaseline(source);

        WarnIfWithoutBehaviors(baseline);
        return baseline;
    }

    /// <summary>
    ///     Says so, once, when the loaded baseline predates behaviour tokens. It still loads - the
    ///     key is additive - but every shipped object then answers "no behaviours", so anything
    ///     asking what an object IS gets nothing back for the whole base game. That reads as a
    ///     feature quietly doing nothing, which is worth a line in the log.
    /// </summary>
    private void WarnIfWithoutBehaviors(BaselineIndex baseline)
    {
        if (baseline.Symbols.IsEmpty) return;
        if (baseline.Symbols.Values.Any(s => s.Behaviors is { Length: > 0 })) return;

        _logger.LogWarning(
            "Baseline holds {Count} symbol(s) but no behaviours - it predates behaviour indexing, so no " +
            "shipped object can be matched by kind; rebuild it with the BaselineBuilder to restore that",
            baseline.Symbols.Count);
    }

    private async Task<(BaselineIndex, StatusAssetSource)> LoadLocalAsync(string? path, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(path))
            return (BaselineIndex.Empty, StatusAssetSource.Empty);

        try
        {
            var bytes = await _fileHelper.FileSystem.File.ReadAllBytesAsync(path, ct);
            var baseline = BaselineSerializer.Deserialize(bytes);
            if (baseline is not null)
                return (baseline, StatusAssetSource.Local);
            _logger.LogWarning("Baseline at '{Path}' is stale or incompatible; using empty baseline", path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load local baseline from '{Path}'", path);
        }

        return (BaselineIndex.Empty, StatusAssetSource.Empty);
    }

    private async Task<(BaselineIndex, StatusAssetSource)> LoadHttpAsync(string url, CancellationToken ct)
    {
        var cacheFile = _fileHelper.FileSystem.Path.Combine(CacheDir, _fileHelper.FileSystem.Path.GetFileName(url));

        try
        {
            var bytes = await _httpClient.GetByteArrayAsync(url, ct);
            var baseline = BaselineSerializer.Deserialize(bytes);
            if (baseline is not null)
            {
                // Only persist to cache once confirmed loadable - a stale/incompatible download must
                // never overwrite a previously-good cached copy that the fallback below could still
                // use. Atomic and under the lock: ~/.aetswg is shared by every server on the machine.
                using (_lock.Acquire(CacheDir))
                {
                    AtomicFile.WriteAllBytes(_fileHelper.FileSystem, cacheFile, bytes);
                }

                return (baseline, StatusAssetSource.Network);
            }

            _logger.LogWarning("Downloaded baseline from '{Url}' is stale or incompatible; trying cache", url);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to download baseline from '{Url}'; trying cache", url);
        }

        if (_fileHelper.FileSystem.File.Exists(cacheFile))
            try
            {
                var cached = await _fileHelper.FileSystem.File.ReadAllBytesAsync(cacheFile, ct);
                var baseline = BaselineSerializer.Deserialize(cached);
                if (baseline is not null)
                    return (baseline, StatusAssetSource.Cache);
                _logger.LogWarning("Cached baseline at '{Path}' is stale or incompatible", cacheFile);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load cached baseline from '{Path}'", cacheFile);
            }

        return (BaselineIndex.Empty, StatusAssetSource.Empty);
    }
}
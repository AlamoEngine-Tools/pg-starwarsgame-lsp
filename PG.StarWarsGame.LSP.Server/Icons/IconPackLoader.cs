// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using PG.StarWarsGame.LSP.Assets.Serialization;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Server.Status;

namespace PG.StarWarsGame.LSP.Server.Icons;

/// <summary>
///     Fetches the icon sidecar that sits beside whichever baseline the server is configured to use.
/// </summary>
/// <remarks>
///     <para>
///         Deliberately mirrors <see cref="BaselineLoader" />'s source handling - local path or HTTP
///         with an on-disk cache - because the sidecar has to follow its baseline wherever that comes
///         from. It is a SEPARATE fetch rather than part of the baseline load so that the ~3 MB of
///         PNGs is only paid for by sessions that actually open a preview.
///     </para>
///     <para>
///         Absence is entirely normal, not an error: baselines published before icon baking existed
///         have no sidecar at all. Every failure path returns <see cref="IconPack.Empty" />, which
///         degrades previews to the embedded placeholder rather than failing the request.
///     </para>
/// </remarks>
public sealed class IconPackLoader
{
    private readonly IFileHelper _fileHelper;
    private readonly HttpClient _httpClient;
    private readonly ICrossProcessLock _lock;
    private readonly ILogger<IconPackLoader> _logger;
    private readonly ServerStatusRecorder? _status;

    public IconPackLoader(HttpClient httpClient, IFileHelper fileHelper, ILogger<IconPackLoader> logger,
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

    public async Task<IconPack> LoadAsync(BaselineSourceConfig config, CancellationToken ct)
    {
        var (pack, source) = await (config.Type switch
        {
            BaselineSourceType.Local => LoadLocalAsync(config.LocalPath, ct),
            BaselineSourceType.Http => LoadHttpAsync(config.Url, ct),
            _ => Task.FromResult((IconPack.Empty, StatusAssetSource.Empty))
        });

        _status?.RecordIconPack(source);
        return pack;
    }

    private async Task<(IconPack, StatusAssetSource)> LoadLocalAsync(string? baselinePath, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(baselinePath))
            return (IconPack.Empty, StatusAssetSource.Empty);

        var path = IconPackSerializer.SidecarPathFor(baselinePath);
        if (!_fileHelper.FileSystem.File.Exists(path))
            return (IconPack.Empty, StatusAssetSource.Empty);

        try
        {
            var pack = IconPackSerializer.Deserialize(
                await _fileHelper.FileSystem.File.ReadAllBytesAsync(path, ct));
            if (pack is not null)
                return (pack, StatusAssetSource.Local);

            _logger.LogWarning("Icon pack at '{Path}' is stale or incompatible; previews will use " +
                               "the built-in placeholder.", path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load icon pack from '{Path}'", path);
        }

        return (IconPack.Empty, StatusAssetSource.Empty);
    }

    private async Task<(IconPack, StatusAssetSource)> LoadHttpAsync(string baselineUrl, CancellationToken ct)
    {
        var url = IconPackSerializer.SidecarPathFor(baselineUrl);
        var cacheFile = _fileHelper.FileSystem.Path.Combine(
            CacheDir, _fileHelper.FileSystem.Path.GetFileName(url));

        try
        {
            var bytes = await _httpClient.GetByteArrayAsync(url, ct);
            var pack = IconPackSerializer.Deserialize(bytes);
            if (pack is not null)
            {
                // Only cache once confirmed loadable, so a bad download never clobbers a good copy.
                // Atomic and under the lock: ~/.aetswg is shared by every server on the machine.
                using (_lock.Acquire(CacheDir))
                {
                    AtomicFile.WriteAllBytes(_fileHelper.FileSystem, cacheFile, bytes);
                }

                return (pack, StatusAssetSource.Network);
            }

            _logger.LogWarning("Downloaded icon pack from '{Url}' is stale or incompatible; trying cache", url);
        }
        catch (Exception ex)
        {
            // Includes the 404 for a baseline published without a sidecar - expected, not alarming.
            _logger.LogDebug(ex, "No icon pack available at '{Url}'; trying cache", url);
        }

        if (!_fileHelper.FileSystem.File.Exists(cacheFile))
            return (IconPack.Empty, StatusAssetSource.Empty);

        try
        {
            var pack = IconPackSerializer.Deserialize(
                await _fileHelper.FileSystem.File.ReadAllBytesAsync(cacheFile, ct));
            if (pack is not null)
                return (pack, StatusAssetSource.Cache);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load cached icon pack from '{Path}'", cacheFile);
        }

        return (IconPack.Empty, StatusAssetSource.Empty);
    }
}
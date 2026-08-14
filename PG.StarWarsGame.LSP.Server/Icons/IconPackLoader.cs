// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using PG.StarWarsGame.LSP.Assets.Serialization;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Util;

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
    private readonly ILogger<IconPackLoader> _logger;

    public IconPackLoader(HttpClient httpClient, IFileHelper fileHelper, ILogger<IconPackLoader> logger)
    {
        _httpClient = httpClient;
        _fileHelper = fileHelper;
        _logger = logger;
    }

    private string CacheDir => _fileHelper.FileSystem.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".aetswg", "baselines");

    public Task<IconPack> LoadAsync(BaselineSourceConfig config, CancellationToken ct)
    {
        return config.Type switch
        {
            BaselineSourceType.Local => LoadLocalAsync(config.LocalPath, ct),
            BaselineSourceType.Http => LoadHttpAsync(config.Url, ct),
            _ => Task.FromResult(IconPack.Empty)
        };
    }

    private async Task<IconPack> LoadLocalAsync(string? baselinePath, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(baselinePath))
            return IconPack.Empty;

        var path = IconPackSerializer.SidecarPathFor(baselinePath);
        if (!_fileHelper.FileSystem.File.Exists(path))
            return IconPack.Empty;

        try
        {
            var pack = IconPackSerializer.Deserialize(
                await _fileHelper.FileSystem.File.ReadAllBytesAsync(path, ct));
            if (pack is not null)
                return pack;

            _logger.LogWarning("Icon pack at '{Path}' is stale or incompatible; previews will use " +
                               "the built-in placeholder.", path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load icon pack from '{Path}'", path);
        }

        return IconPack.Empty;
    }

    private async Task<IconPack> LoadHttpAsync(string baselineUrl, CancellationToken ct)
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
                _fileHelper.FileSystem.Directory.CreateDirectory(CacheDir);
                await _fileHelper.FileSystem.File.WriteAllBytesAsync(cacheFile, bytes, ct);
                return pack;
            }

            _logger.LogWarning("Downloaded icon pack from '{Url}' is stale or incompatible; trying cache", url);
        }
        catch (Exception ex)
        {
            // Includes the 404 for a baseline published without a sidecar - expected, not alarming.
            _logger.LogDebug(ex, "No icon pack available at '{Url}'; trying cache", url);
        }

        if (!_fileHelper.FileSystem.File.Exists(cacheFile))
            return IconPack.Empty;

        try
        {
            var pack = IconPackSerializer.Deserialize(
                await _fileHelper.FileSystem.File.ReadAllBytesAsync(cacheFile, ct));
            if (pack is not null)
                return pack;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load cached icon pack from '{Path}'", cacheFile);
        }

        return IconPack.Empty;
    }
}

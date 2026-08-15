// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using PG.StarWarsGame.Files.MTD.Services;
using PG.StarWarsGame.LSP.Assets.Icons;
using PG.StarWarsGame.LSP.Assets.Serialization;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Project;
using PG.StarWarsGame.LSP.Core.Util;

namespace PG.StarWarsGame.LSP.Server.Icons;

public interface IIconCatalogProvider : IIconRepackStatusProvider
{
    /// <summary>
    ///     The icon catalog for <paramref name="projectRoot" />, building it on first use.
    /// </summary>
    Task<IconCatalog> GetAsync(string projectRoot, IconProjectSettings? settings, CancellationToken ct);

    /// <summary>Drops every cached catalog, so the next request re-reads from disk.</summary>
    void Invalidate();
}

/// <summary>
///     Builds and caches one <see cref="IconCatalog" /> per project root.
/// </summary>
/// <remarks>
///     <para>
///         Everything here is lazy on purpose. Assembling a catalog means decoding a 2048x2048 atlas
///         and every raw source image the project ships, and most editing sessions never open a
///         preview - so nothing is read until the first request asks for an icon. The baked baseline
///         sidecar is fetched once and shared across project roots, since it does not vary by
///         project.
///     </para>
///     <para>
///         Keyed by project root rather than held as a single instance because one server hosts N
///         workspaces, each with its own mega texture and source folders.
///     </para>
/// </remarks>
public sealed class IconCatalogProvider : IIconCatalogProvider
{
    private readonly Dictionary<string, IconCatalog> _catalogs = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILspConfigurationProvider _config;
    private readonly IFileHelper _fileHelper;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ILogger<IconCatalogProvider> _logger;
    private readonly IMtdFileService _mtdFileService;
    private readonly IconPackLoader _packLoader;
    private IconPack? _baseline;

    public IconCatalogProvider(
        IconPackLoader packLoader,
        IMtdFileService mtdFileService,
        IFileHelper fileHelper,
        ILspConfigurationProvider config,
        ILogger<IconCatalogProvider> logger)
    {
        _packLoader = packLoader;
        _mtdFileService = mtdFileService;
        _fileHelper = fileHelper;
        _config = config;
        _logger = logger;
    }

    public async Task<IconCatalog> GetAsync(
        string projectRoot, IconProjectSettings? settings, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_catalogs.TryGetValue(projectRoot, out var cached))
                return cached;

            _baseline ??= await _packLoader.LoadAsync(_config.Current.BaselineSource, ct);

            var catalog = IconCatalogLoader.Load(
                _fileHelper.FileSystem,
                _mtdFileService,
                projectRoot,
                settings ?? IconProjectSettings.Default,
                _baseline,
                _logger);

            _catalogs[projectRoot] = catalog;
            return catalog;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    ///     The union of every built catalog's stale icons.
    /// </summary>
    /// <remarks>
    ///     Reads only what has ALREADY been built - it never triggers a catalog build. Diagnostics
    ///     run on every keystroke, and decoding a mega texture on that path would be indefensible, so
    ///     the repack warning appears once something else (a preview) has warmed the catalog. That is
    ///     a deliberate trade: a late warning beats a stalled editor.
    /// </remarks>
    public IReadOnlySet<string> IconsAwaitingRepack
    {
        get
        {
            _gate.Wait();
            try
            {
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var catalog in _catalogs.Values)
                    names.UnionWith(catalog.IconsAwaitingRepack);
                return names;
            }
            finally
            {
                _gate.Release();
            }
        }
    }

    public void Invalidate()
    {
        _gate.Wait();
        try
        {
            _catalogs.Clear();

            // The sidecar is deliberately KEPT: it tracks the configured baseline, not the
            // workspace, so a project reload has no bearing on whether it is still current.
        }
        finally
        {
            _gate.Release();
        }
    }
}

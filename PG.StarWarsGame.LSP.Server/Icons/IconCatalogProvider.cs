// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using PG.StarWarsGame.Files.MTD.Services;
using PG.StarWarsGame.LSP.Assets.Icons;
using PG.StarWarsGame.LSP.Assets.Serialization;
using PG.StarWarsGame.LSP.Core.Caching;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Project;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Server.Startup;

namespace PG.StarWarsGame.LSP.Server.Icons;

public interface IIconCatalogProvider : IIconRepackStatusProvider
{
    /// <summary>
    ///     The icon catalog for <paramref name="layers" />, highest precedence first, built on first
    ///     use and cached against the whole layer set.
    /// </summary>
    Task<IconCatalog> GetAsync(IReadOnlyList<IconLayer> layers, CancellationToken ct);

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
///         sidecar is fetched once per game and shared across project roots, since it varies by the
///         game the project targets and by nothing else.
///     </para>
///     <para>
///         Keyed by project root rather than held as a single instance because one server hosts N
///         workspaces, each with its own mega texture and source folders.
///     </para>
/// </remarks>
public sealed class IconCatalogProvider : IIconCatalogProvider, ICacheStatisticsSource
{
    private readonly Dictionary<string, IconCatalog> _catalogs = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILspConfigurationProvider _config;
    private readonly IFileHelper _fileHelper;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ILogger<IconCatalogProvider> _logger;
    private readonly IMtdService _mtdFileService;
    private readonly IconPackLoader _packLoader;
    private readonly IBaselineBootstrapper _baselines;
    private IconPack? _baseline;
    private TargetGame _baselineGame;

    public IconCatalogProvider(
        IconPackLoader packLoader,
        IMtdService mtdFileService,
        IFileHelper fileHelper,
        ILspConfigurationProvider config,
        ILogger<IconCatalogProvider> logger,
        IBaselineBootstrapper baselines)
    {
        _packLoader = packLoader;
        _mtdFileService = mtdFileService;
        _fileHelper = fileHelper;
        _config = config;
        _logger = logger;
        _baselines = baselines;
    }

    /// <summary>Catalogs built, one per layer set seen; each holds decoded icon bytes.</summary>
    public CacheStatistics Snapshot()
    {
        return new CacheStatistics("icon-catalogs", _catalogs.Count);
    }

    public async Task<IconCatalog> GetAsync(IReadOnlyList<IconLayer> layers, CancellationToken ct)
    {
        // Every layer's root, in order: two workspaces that share a dependency still differ by
        // their leaf, and the same leaf with a reference added is a different catalog.
        var key = string.Join('|', layers.Select(l => l.RootPath));

        await _gate.WaitAsync(ct);
        try
        {
            if (_catalogs.TryGetValue(key, out var cached))
                return cached;

            // The pack sits beside the baseline of the project's game, so a project switched to the
            // other game reads the other pack.
            var game = _baselines.LoadedGame ?? TargetGame.FOC;
            if (_baseline is null || _baselineGame != game)
            {
                _baseline = await _packLoader.LoadAsync(_config.Current.BaselineSource.ForGame(game), ct);
                _baselineGame = game;
            }

            var catalog = IconCatalogLoader.Load(
                _fileHelper.FileSystem,
                _mtdFileService,
                layers,
                _baseline,
                _logger);

            _catalogs[key] = catalog;
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

            // The sidecar is deliberately KEPT: it tracks the baseline's game, which GetAsync checks
            // on every build, so a reload that leaves the game alone does not fetch it again.
        }
        finally
        {
            _gate.Release();
        }
    }
}

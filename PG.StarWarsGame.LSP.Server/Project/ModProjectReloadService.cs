// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Server.Localisation;
using PG.StarWarsGame.LSP.Server.Startup;

namespace PG.StarWarsGame.LSP.Server.Project;

/// <summary>
///     The single workspace index path, shared by startup (via <see cref="StartupPipeline" />), the
///     reload command, and the <c>.pgproj</c> file watcher. Resolves the project configuration and
///     drives the <see cref="IWorkspaceIndexer" /> stages in a fixed order. No <c>.pgproj</c> means
///     no directories to index — the resolver returns null and this becomes a no-op.
/// </summary>
public sealed class ModProjectReloadService : IModProjectReloadService
{
    private readonly IWorkspaceIndexer _indexer;
    private readonly IProjectLayerMap _layerMap;
    private readonly ILocalisationLoader _localisation;
    private readonly ILogger<ModProjectReloadService> _logger;
    private readonly IProjectConfigurationResolver _resolver;

    private List<string>? _lastRoots;

    public ModProjectReloadService(
        IProjectConfigurationResolver resolver,
        IWorkspaceIndexer indexer,
        ILocalisationLoader localisation,
        IProjectLayerMap layerMap,
        ILogger<ModProjectReloadService> logger)
    {
        _resolver = resolver;
        _indexer = indexer;
        _localisation = localisation;
        _layerMap = layerMap;
        _logger = logger;
    }

    public IReadOnlyList<string>? LastAssetRoots { get; private set; }
    public WorkspaceConfiguration? LastWorkspaceConfig { get; private set; }
    public IReadOnlyList<string>? LastWorkspaceRoots { get; private set; }

    public async Task LoadAsync(IEnumerable<string> workspaceRoots, CancellationToken ct)
    {
        var roots = workspaceRoots.ToList();
        _lastRoots = roots;
        LastWorkspaceRoots = roots;

        var config = _resolver.Resolve(roots);
        if (config is null)
            // No project file (the resolver already logged); nothing to index.
            return;

        LastWorkspaceConfig = config;
        // Publish layer precedence before indexing so each document is stamped with its rank
        // (indexing itself stays parallel — correctness comes from the rank, not insertion order).
        _layerMap.SetLayers(config.Layers);
        _indexer.PreScanMetafiles(config, roots);
        await _indexer.IndexDocumentsAsync(config, ct);
        _indexer.ApplyDynamicEnumCatalog(config.XmlDirectories);
        _indexer.ApplyAssetCatalog(config.AssetRoots);
        _indexer.ApplyModelBoneCatalog(config.AssetRoots);
        LastAssetRoots = config.AssetRoots;

        try
        {
            await _localisation.LoadAsync(config, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Workspace localisation load failed.");
        }
    }

    public async Task ReloadAsync(CancellationToken ct)
    {
        var roots = _lastRoots;
        if (roots is null)
        {
            _logger.LogWarning("ReloadAsync called before LoadAsync; ignoring reload request.");
            return;
        }

        try
        {
            await LoadAsync(roots, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Mod project reload failed.");
        }
    }

    public async Task ReloadLocalisationAsync(CancellationToken ct)
    {
        var config = LastWorkspaceConfig;
        if (config is null)
        {
            _logger.LogWarning("ReloadLocalisationAsync called before LoadAsync; ignoring reload request.");
            return;
        }

        try
        {
            await _localisation.LoadAsync(config, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Localisation-only reload failed.");
        }
    }
}
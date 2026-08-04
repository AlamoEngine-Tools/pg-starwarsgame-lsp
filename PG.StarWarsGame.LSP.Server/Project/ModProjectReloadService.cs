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
///     no directories to index - the resolver returns null and this becomes a no-op.
/// </summary>
public sealed class ModProjectReloadService : IModProjectReloadService
{
    private readonly IWorkspaceIndexer _indexer;
    private readonly ILocalisationLoader _localisation;
    private readonly ILogger<ModProjectReloadService> _logger;
    private readonly IClientRefreshNotifier? _refresh;
    private readonly IProjectRegistry _registry;
    private readonly IProjectConfigurationResolver _resolver;

    private List<string>? _lastRoots;

    // refresh is optional so the many minimal test setups can omit it; production always wires it.
    public ModProjectReloadService(
        IProjectConfigurationResolver resolver,
        IWorkspaceIndexer indexer,
        ILocalisationLoader localisation,
        IProjectRegistry registry,
        ILogger<ModProjectReloadService> logger,
        IClientRefreshNotifier? refresh = null)
    {
        _resolver = resolver;
        _indexer = indexer;
        _localisation = localisation;
        _registry = registry;
        _logger = logger;
        _refresh = refresh;
    }

    public IReadOnlyList<string>? LastAssetRoots { get; private set; }

    /// <summary>
    ///     The primary project's configuration. Prefer <see cref="LastWorkspaceConfigs" /> - this
    ///     discards every project but one in a multi-root workspace.
    /// </summary>
    public WorkspaceConfiguration? LastWorkspaceConfig { get; private set; }

    /// <summary>Every resolved project configuration from the last load, in root order.</summary>
    public IReadOnlyList<WorkspaceConfiguration> LastWorkspaceConfigs { get; private set; } = [];

    public IReadOnlyList<string>? LastWorkspaceRoots { get; private set; }

    public async Task LoadAsync(IEnumerable<string> workspaceRoots, CancellationToken ct)
    {
        var roots = workspaceRoots.ToList();
        _lastRoots = roots;
        LastWorkspaceRoots = roots;

        var configs = _resolver.ResolveAll(roots);

        // Publish the project set before indexing so every document is routed to its project and
        // stamped with its layer rank (indexing itself stays parallel - correctness comes from the
        // rank, not insertion order). This also seeds each project's xml context and layer map.
        _registry.SetProjects(configs);
        LastWorkspaceConfigs = configs;

        if (configs.Count == 0)
            // No project file (the resolver already logged); nothing to index.
            return;

        LastWorkspaceConfig = configs[0];

        // Every project is indexed into its own state. Projects are independent by construction, so
        // one that fails must not abort the rest - in a multi-root workspace that would mean a
        // single broken mod silently disabling the others.
        var assetRoots = new List<string>();
        foreach (var config in configs)
        {
            var project = _registry.ForConfiguration(config);
            try
            {
                _indexer.PreScanMetafiles(config, roots);
                await _indexer.IndexDocumentsAsync(config, ct);
                _indexer.ApplyDynamicEnumCatalog(config.XmlDirectories, project);
                _indexer.ApplyAssetCatalog(config.AssetRoots, project);
                _indexer.ApplyModelBoneCatalog(config.AssetRoots, project);
                assetRoots.AddRange(config.AssetRoots);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Indexing project '{Project}' failed; other projects are unaffected.",
                    config.ProjectPath ?? "<unnamed>");
            }
        }

        LastAssetRoots = assetRoots;

        // Localisation loads per project so each keeps its own resource type and layer set; the
        // loader merges into the shared localisation index.
        foreach (var config in configs)
            try
            {
                await _localisation.LoadAsync(config, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Workspace localisation load failed for '{Project}'.",
                    config.ProjectPath ?? "<unnamed>");
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
            return;
        }

        // Loca-key inlay hints and code lenses were computed against the previous translation data
        // and nothing about the open documents changed, so the client has no reason to re-request
        // them on its own (#45). Ask it to.
        _refresh?.RefreshDerivedState();
    }
}
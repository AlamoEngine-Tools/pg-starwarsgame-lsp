// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Server.Indexing;
using PG.StarWarsGame.LSP.Server.Project;
using PG.StarWarsGame.LSP.Server.Startup;

namespace PG.StarWarsGame.LSP.Server;

/// <summary>
///     The pgproj-driven indexing stages behind one seam: the <see cref="MetafileRegistrar" /> types
///     the files, the <see cref="DocumentIndexer" /> parses them, and the three catalog builders
///     publish the assets, the models and the dynamic enums. Holds no logic of its own - no waiting,
///     no events, no buffering, no heuristic fallback; the <see cref="IModProjectReloadService" />
///     drives the stages in a fixed order.
/// </summary>
public sealed class WorkspaceIndexer(
    IGameIndexService indexService,
    MetafileRegistrar metafiles,
    DocumentIndexer documents,
    AssetCatalogBuilder assets,
    ModelCatalogBuilder models,
    DynamicEnumCatalogBuilder dynamicEnums) : IWorkspaceIndexer
{
    /// <inheritdoc />
    public IndexCacheStats? LastIndexCache => documents.LastIndexCache;

    /// <inheritdoc />
    public BoneCatalogStats? LastBoneCatalog => models.LastBoneCatalog;

    /// <summary>The metafile stage, over the baseline loaded now; publishes the load order it finds.</summary>
    public void PreScanMetafiles(WorkspaceConfiguration config, IReadOnlyList<string> roots)
    {
        indexService.ApplyLoadOrder(metafiles.Register(config, roots, indexService.Current.Baseline));
    }

    public Task<int> IndexDocumentsAsync(WorkspaceConfiguration config, CancellationToken ct,
        Action<int, int>? progress = null)
    {
        return documents.IndexAsync(config, ct, progress);
    }

    public void ApplyAssetCatalog(WorkspaceConfiguration config)
    {
        assets.Apply(config);
    }

    public void ApplyModelBoneCatalog(WorkspaceConfiguration config)
    {
        models.Apply(config);
    }

    public void ApplyDynamicEnumCatalog(IReadOnlyList<string> xmlRoots)
    {
        dynamicEnums.Apply(xmlRoots);
    }
}
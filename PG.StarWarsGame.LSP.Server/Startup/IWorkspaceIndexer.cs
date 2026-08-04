// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Workspace;

namespace PG.StarWarsGame.LSP.Server.Startup;

/// <summary>
///     The pgproj-driven indexing work invoked by the <see cref="StartupPipeline" />. Implemented
///     by <see cref="WorkspaceIndexer" />; abstracted so the pipeline's stage ordering can be
///     verified in isolation.
/// </summary>
public interface IWorkspaceIndexer
{
    void PreScanMetafiles(WorkspaceConfiguration config, IReadOnlyList<string> roots);

    Task<int> IndexDocumentsAsync(WorkspaceConfiguration config, CancellationToken ct,
        Action<int, int>? progress = null);

    // The catalogs below are built from one project's roots and replace the whole catalog, so they
    // must be written to that project's index. Passing null targets the shared (routing) index
    // service, which is the single-project behaviour and what the minimal test setups rely on.
    void ApplyAssetCatalog(IReadOnlyList<string> roots, ProjectWorkspace? project = null);

    void ApplyModelBoneCatalog(IReadOnlyList<string> roots, ProjectWorkspace? project = null);

    void ApplyDynamicEnumCatalog(IReadOnlyList<string> xmlRoots, ProjectWorkspace? project = null);
}
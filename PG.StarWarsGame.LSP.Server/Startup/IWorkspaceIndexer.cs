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

    void ApplyAssetCatalog(IReadOnlyList<string> roots);

    void ApplyModelBoneCatalog(IReadOnlyList<string> roots);

    void ApplyDynamicEnumCatalog(IReadOnlyList<string> xmlRoots);
}
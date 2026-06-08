// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using MediatR;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Workspace;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Server.Project;
using PG.StarWarsGame.LSP.Server.Startup;
using LspFileSystemWatcher = OmniSharp.Extensions.LanguageServer.Protocol.Models.FileSystemWatcher;

namespace PG.StarWarsGame.LSP.Server;

public sealed class GameDidChangeWatchedFilesHandler : DidChangeWatchedFilesHandlerBase
{
    private readonly IFileHelper _fileHelper;
    private readonly IWorkspaceIndexer _indexer;
    private readonly IGameIndexService _indexService;
    private readonly ILogger<GameDidChangeWatchedFilesHandler> _logger;
    private readonly IModProjectReloadService _reloadService;
    private readonly IGameWorkspaceHost _workspaceHost;

    public GameDidChangeWatchedFilesHandler(
        IGameIndexService indexService,
        IGameWorkspaceHost workspaceHost,
        IFileHelper fileHelper,
        IWorkspaceIndexer indexer,
        IModProjectReloadService reloadService,
        ILogger<GameDidChangeWatchedFilesHandler> logger)
    {
        _indexService = indexService;
        _workspaceHost = workspaceHost;
        _fileHelper = fileHelper;
        _indexer = indexer;
        _reloadService = reloadService;
        _logger = logger;
    }

    public override async Task<Unit> Handle(DidChangeWatchedFilesParams request, CancellationToken ct)
    {
        using var bulk = _indexService.BeginBulkUpdate();
        foreach (var change in request.Changes)
        {
            var uri = _fileHelper.NormalizeUri(change.Uri.ToString());

            // A changed/created/deleted .pgproj re-derives the whole workspace configuration.
            if (uri.EndsWith(".pgproj", StringComparison.OrdinalIgnoreCase))
            {
                await _reloadService.ReloadAsync(ct);
                continue;
            }

            if (change.Type == FileChangeType.Deleted)
            {
                _indexService.RemoveDocument(uri);
                continue;
            }

            var path = _fileHelper.FileUriToPath(uri);
            if (path is null) continue;

            // A changed loose asset file re-globs the asset catalog from the last-scanned roots.
            if (WorkspaceIndexer.IsAssetFile(path))
            {
                _indexer.ApplyAssetCatalog(_reloadService.LastAssetRoots ?? []);
                continue;
            }

            // Opened documents are kept in sync by didOpen/didChange — skip them.
            if (_workspaceHost.TryGet(uri, out _)) continue;

            if (!_fileHelper.FileSystem.File.Exists(path)) continue;

            try
            {
                var text = await _fileHelper.FileSystem.File.ReadAllTextAsync(path, ct);
                await _indexService.UpdateDocumentAsync(uri, text, 0, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to re-index watched file {Uri}", uri);
            }
        }

        return Unit.Value;
    }

    protected override DidChangeWatchedFilesRegistrationOptions CreateRegistrationOptions(
        DidChangeWatchedFilesCapability capability, ClientCapabilities clientCapabilities)
    {
        return new DidChangeWatchedFilesRegistrationOptions
        {
            Watchers = new Container<LspFileSystemWatcher>(
                new LspFileSystemWatcher { GlobPattern = "**/*.xml" },
                new LspFileSystemWatcher { GlobPattern = "**/*.lua" },
                new LspFileSystemWatcher { GlobPattern = "**/*.pgproj" })
        };
    }
}
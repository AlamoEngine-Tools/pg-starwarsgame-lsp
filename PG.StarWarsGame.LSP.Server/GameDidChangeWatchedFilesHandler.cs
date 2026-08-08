// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using MediatR;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Workspace;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Server.Localisation;
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
    private readonly ISchemaProvider _schema;
    private readonly IStartupGate _startupGate;
    private readonly IGameWorkspaceHost _workspaceHost;
    private readonly ILocalisationWriteLedger _writeLedger;

    public GameDidChangeWatchedFilesHandler(
        IGameIndexService indexService,
        IGameWorkspaceHost workspaceHost,
        IFileHelper fileHelper,
        IWorkspaceIndexer indexer,
        IModProjectReloadService reloadService,
        ISchemaProvider schema,
        ILocalisationWriteLedger writeLedger,
        IStartupGate startupGate,
        ILogger<GameDidChangeWatchedFilesHandler> logger)
    {
        _startupGate = startupGate;
        _writeLedger = writeLedger;
        _indexService = indexService;
        _workspaceHost = workspaceHost;
        _fileHelper = fileHelper;
        _indexer = indexer;
        _reloadService = reloadService;
        _schema = schema;
        _logger = logger;
    }

    public override async Task<Unit> Handle(DidChangeWatchedFilesParams request, CancellationToken ct)
    {
        // Buffered until the startup pipeline has run. Before it has, LastWorkspaceConfig is null, so
        // nothing can be classified as being under a text root and every change was silently dropped -
        // the startup load covers most of that by reading the disk afterwards, but a change landing
        // between its enumeration and its completion was lost outright.
        await _startupGate.RunOrBufferAsync(token => HandleChangesAsync(request, token), ct);
        return Unit.Value;
    }

    private async Task HandleChangesAsync(DidChangeWatchedFilesParams request, CancellationToken ct)
    {
        // Workspace-wide reactions (project reload, asset re-glob, enum re-scan, localisation
        // reload) are batched: the loop only sets flags and each reaction runs at most once per
        // notification - a git checkout can deliver hundreds of changes in a single request.
        // Per-document updates/removals stay per-change inside one bulk scope.
        var projectChanged = false;
        var assetsChanged = false;
        var dynamicEnumsChanged = false;
        var localisationTextChanged = false;

        // Built on first use so the schema's enum list is walked once per notification, not once
        // per changed file.
        HashSet<string>? enumSourceFileNames = null;

        using (_indexService.BeginBulkUpdate())
        {
            foreach (var change in request.Changes)
            {
                var uri = _fileHelper.NormalizeUri(change.Uri.ToString());

                // A changed/created/deleted .pgproj re-derives the whole workspace configuration.
                if (uri.EndsWith(".pgproj", StringComparison.OrdinalIgnoreCase))
                {
                    projectChanged = true;
                    continue;
                }

                var path = _fileHelper.FileUriToPath(uri);

                // A changed/created/deleted file under any project layer's text root (csv/properties/
                // loc-xml) needs a scoped localisation reload, never document indexing - no
                // IGameDocumentParser understands those formats, so routing them through the generic
                // path below is always a silent no-op.
                if (path is not null && IsUnderTextRoot(path))
                {
                    // Unless this is the server reading back its own write - saving from the
                    // localisation editor already reloads the index, so reacting to the watcher
                    // event for that same write reloaded everything a second time and told every
                    // open tab the index had moved twice over.
                    if (!await IsOwnWriteEchoAsync(path, ct))
                        localisationTextChanged = true;

                    continue;
                }

                if (change.Type == FileChangeType.Deleted)
                {
                    _indexService.RemoveDocument(uri);
                    continue;
                }

                if (path is null) continue;

                // A changed loose asset file re-globs the asset catalog from the last-scanned roots.
                if (WorkspaceIndexer.IsAssetFile(path))
                {
                    assetsChanged = true;
                    continue;
                }

                // A changed dynamic-enum source file (e.g. gameconstants.xml, surfacefxtriggertype.xml)
                // re-scans the enum catalog so referencing tags re-validate against the new value set —
                // no IGameDocumentParser understands these files, so routing them through the generic
                // path below is always a silent no-op.
                enumSourceFileNames ??= BuildDynamicEnumSourceFileNames();
                if (enumSourceFileNames.Contains(_fileHelper.FileSystem.Path.GetFileName(path)))
                {
                    dynamicEnumsChanged = true;
                    continue;
                }

                // Opened documents are kept in sync by didOpen/didChange - skip them.
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
        }

        // Run after the bulk scope has closed so the reload's own indexing (and its project-cache
        // snapshots, which read Current after their inner scope) see the merged index rather than
        // still-deferred operations.
        if (projectChanged)
        {
            // The full reload re-resolves the configuration and re-runs every catalog (assets,
            // dynamic enums, model bones, localisation) - the scoped reloads below would be
            // redundant work against the outdated configuration.
            await _reloadService.ReloadAsync(ct);
            return;
        }

        if (assetsChanged)
            _indexer.ApplyAssetCatalog(_reloadService.LastAssetRoots ?? []);

        if (dynamicEnumsChanged)
            _indexer.ApplyDynamicEnumCatalog(_reloadService.LastWorkspaceConfig?.XmlDirectories ?? []);

        if (localisationTextChanged)
            await _reloadService.ReloadLocalisationAsync(ct);
    }

    // Matches by filename only, same as WorkspaceIndexer.ApplyDynamicEnumCatalog's file search —
    // the schema's SourceFile carries an optional "$Element" anchor that isn't part of the path.
    private HashSet<string> BuildDynamicEnumSourceFileNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var enumDef in _schema.AllEnums)
        {
            if (enumDef.Kind != EnumKind.DynamicXml || string.IsNullOrEmpty(enumDef.SourceFile))
                continue;

            var sourceFile = enumDef.SourceFile;
            var anchorIdx = sourceFile.IndexOf('$');
            var filePath = anchorIdx >= 0 ? sourceFile[..anchorIdx] : sourceFile;
            names.Add(_fileHelper.FileSystem.Path.GetFileName(filePath.Replace('/', '\\')));
        }

        return names;
    }

    /// <summary>
    ///     Whether this file currently holds exactly what the server last wrote to it.
    /// </summary>
    /// <remarks>
    ///     Content, not timing: an edit that landed between the write and the watcher event has a
    ///     different hash and is reloaded as it should be. A file that has since been deleted is a
    ///     real change too.
    /// </remarks>
    private async Task<bool> IsOwnWriteEchoAsync(string path, CancellationToken ct)
    {
        try
        {
            if (!_fileHelper.FileSystem.File.Exists(path)) return false;

            var current = await _fileHelper.FileSystem.File.ReadAllTextAsync(path, ct);
            return _writeLedger.ConsumeEcho(path, LocalisationContentHash.Compute(current));
        }
        catch (Exception ex)
        {
            // Unreadable here means "reload and let the loader report it properly".
            _logger.LogDebug(ex, "Could not read {Path} to check for an own-write echo.", path);
            return false;
        }
    }

    private bool IsUnderTextRoot(string path)
    {
        var textRoots = _reloadService.LastWorkspaceConfig?.TextRoots;
        if (textRoots is null || textRoots.Count == 0) return false;

        // Direct children only, matching LocalisationLoader.EnumerateFromTextRoots, which globs
        // TopDirectoryOnly. A recursive test here meant a change anywhere below a text root paid for
        // a full localisation rebuild that could not load the file that triggered it.
        var directory = _fileHelper.FileSystem.Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(directory)) return false;

        var directoryUri = _fileHelper.PathToFileUri(directory).TrimEnd('/');
        foreach (var root in textRoots)
        {
            if (string.Equals(
                    _fileHelper.PathToFileUri(root).TrimEnd('/'), directoryUri,
                    StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    protected override DidChangeWatchedFilesRegistrationOptions CreateRegistrationOptions(
        DidChangeWatchedFilesCapability capability, ClientCapabilities clientCapabilities)
    {
        return new DidChangeWatchedFilesRegistrationOptions
        {
            Watchers = new Container<LspFileSystemWatcher>(
                new LspFileSystemWatcher { GlobPattern = "**/*.xml" },
                new LspFileSystemWatcher { GlobPattern = "**/*.lua" },
                new LspFileSystemWatcher { GlobPattern = "**/*.pgproj" },
                new LspFileSystemWatcher { GlobPattern = "**/*.csv" },
                new LspFileSystemWatcher { GlobPattern = "**/*.properties" },
                // DAT is a localisation format like the rest - a project can declare it, and the
                // engine's own files are DAT. Leaving it unwatched meant the one format that is
                // genuinely split per language never noticed a change on disk.
                new LspFileSystemWatcher { GlobPattern = "**/*.dat" })
        };
    }
}
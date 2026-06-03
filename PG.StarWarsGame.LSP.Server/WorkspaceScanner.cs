// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.Diagnostics;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server.WorkDone;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Server.Localisation;

namespace PG.StarWarsGame.LSP.Server;

public sealed class WorkspaceScanner
{
    private readonly EaWXmlContext _eaWXmlContext;
    private readonly IFileHelper _fileHelper;
    private readonly IFileTypeRegistry _fileTypeRegistry;
    private readonly IGameIndexService _indexService;
    private readonly ILocalisationLoader _localisationLoader;
    private readonly ILogger<WorkspaceScanner> _logger;
    private readonly IEnumerable<IGameDocumentParser> _parsers;
    private readonly IPreOpenBuffer _preOpenBuffer;
    private readonly ISchemaProvider _schema;
    private readonly IServerWorkDoneManager? _workDone;
    private readonly IGameWorkspaceHost _workspaceHost;

    // Stored after a successful ScanAsync so OnSchemaRefreshed can trigger a re-scan.
    // Null until the first scan completes, which prevents re-scans during the initial
    // WaitForSchemaAsync phase (the initial scan handles schema readiness itself).
    private volatile List<string>? _lastRoots;

    public WorkspaceScanner(IFileHelper fileHelper, IEnumerable<IGameDocumentParser> parsers,
        IGameIndexService indexService, IGameWorkspaceHost workspaceHost,
        ILogger<WorkspaceScanner> logger,
        IServerWorkDoneManager? workDone,
        IFileTypeRegistry fileTypeRegistry, ISchemaProvider schema,
        EaWXmlContext eaWXmlContext, IPreOpenBuffer preOpenBuffer,
        ILocalisationLoader localisationLoader)
    {
        _fileHelper = fileHelper;
        _parsers = parsers;
        _indexService = indexService;
        _workspaceHost = workspaceHost;
        _logger = logger;
        _workDone = workDone;
        _fileTypeRegistry = fileTypeRegistry;
        _schema = schema;
        _eaWXmlContext = eaWXmlContext;
        _preOpenBuffer = preOpenBuffer;
        _localisationLoader = localisationLoader;
        _schema.SchemaRefreshed += OnSchemaRefreshed;
    }

    public async Task ScanAsync(IEnumerable<string> workspaceFolders, CancellationToken ct)
    {
        var tx = SentrySdk.StartTransaction("lsp.workspace.scan", "workspace.index");
        try
        {
            var sw = Stopwatch.StartNew();
            var roots = workspaceFolders.ToList();
            _logger.LogInformation(
                "Workspace scan starting. Roots: [{Roots}]. AllMetafiles={MetafileCount}, AllTags={TagCount}",
                string.Join(", ", roots), _schema.AllMetafiles.Count, _schema.AllTags.Count);

            var schemaWaitSpan = tx.StartChild("lsp.schema.wait", "Wait for schema to be ready");
            await WaitForSchemaAsync(ct);
            schemaWaitSpan.Finish(SpanStatus.Ok);
            _logger.LogInformation("WaitForSchemaAsync complete at {Elapsed} ms", sw.ElapsedMilliseconds);

            var preScanSpan = tx.StartChild("lsp.workspace.prescan", "Pre-scan metafiles and register file types");
            PreScanMetafiles(roots);
            preScanSpan.Finish(SpanStatus.Ok);
            _logger.LogInformation("PreScanMetafiles complete at {Elapsed} ms", sw.ElapsedMilliseconds);

            // Replay didOpen events that arrived before EaWXmlContext was configured.
            // Only add files that are now recognised as EaW XML and not already in the host.
            var preOpened = _preOpenBuffer.DrainAndClose();
            foreach (var (preUri, preText, preVersion) in preOpened)
            {
                if (!_eaWXmlContext.IsEaWXmlFile(preUri)) continue;
                if (!_workspaceHost.TryGet(preUri, out _))
                    _workspaceHost.AddOrUpdate(preUri, preText, preVersion);
            }

            _logger.LogInformation("PreOpenBuffer drained: {Count} file(s) seeded at {Elapsed} ms",
                preOpened.Count, sw.ElapsedMilliseconds);

            var files = roots
                .SelectMany(folder =>
                    _fileHelper.FileSystem.Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
                .Where(f =>
                {
                    var ext = _fileHelper.FileSystem.Path.GetExtension(f);
                    if (!_parsers.Any(p => p.CanParse(ext))) return false;
                    // XML files are gated to known EaW data directories to avoid indexing
                    // unrelated XML files (e.g. project settings). Other file types (e.g. .lua)
                    // have no such restriction — any parseable file in the workspace is a game file.
                    if (ext.Equals(".xml", StringComparison.OrdinalIgnoreCase))
                        return _eaWXmlContext.IsEaWXmlFile(_fileHelper.PathToFileUri(f));
                    return true;
                })
                .ToList();

            _logger.LogInformation("Workspace scan: {Count} parseable file(s) found at {Elapsed} ms", files.Count,
                sw.ElapsedMilliseconds);

            IWorkDoneObserver? progress = null;
            if (_workDone?.IsSupported == true)
            {
                // window/workDoneProgress/create awaits a client response. Some clients (e.g. VSCode)
                // delay or never respond, which would block the entire scan. Cap at 2 s and skip the
                // progress indicator rather than stalling the indexer.
                var createTask = _workDone.Create(
                    new WorkDoneProgressBegin
                    {
                        Title = "Indexing workspace",
                        Message = $"Scanning {files.Count} file(s)…",
                        Cancellable = false,
                        Percentage = 0
                    },
                    null!,
                    null!,
                    ct);
                var winner = await Task.WhenAny(createTask, Task.Delay(TimeSpan.FromSeconds(2), ct));
                if (winner == createTask && createTask.IsCompletedSuccessfully)
                    progress = await createTask; // task is already completed — await is non-blocking
            }

            var indexed = 0;
            var options = new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct };
            var indexSpan = tx.StartChild("lsp.workspace.index", $"Bulk-index {files.Count} file(s)");
            try
            {
                _logger.LogInformation("Bulk-index starting at {Elapsed} ms", sw.ElapsedMilliseconds);
                using (_indexService.BeginBulkUpdate())
                {
                    await Parallel.ForEachAsync(files, options, async (file, token) =>
                    {
                        var text = await _fileHelper.FileSystem.File.ReadAllTextAsync(file, token);
                        var uri = _fileHelper.PathToFileUri(file);
                        await _indexService.UpdateDocumentAsync(uri, text, 0, token);
                        // [lgr] Seeding removed again, that triggers a fuill workspace scan. We don't want that.
                        // Seed the workspace host so hover/completion work without a prior didOpen.
                        // Skip if the client already sent didOpen (higher-version editor content wins).
                        // if (!_workspaceHost.TryGet(uri, out _))
                        //     _workspaceHost.AddOrUpdate(uri, text, 0);
                        var done = Interlocked.Increment(ref indexed);
                        _logger.LogDebug("Scanned {File} ({Done}/{Total})", file, done, files.Count);
                        progress?.OnNext(null, files.Count > 0 ? (int?)((decimal)done / files.Count * 100) : null,
                            null);
                    });
                } // fires one IndexChanged with the complete final state

                _logger.LogInformation("Bulk-index complete: {Indexed} file(s) at {Elapsed} ms", indexed,
                    sw.ElapsedMilliseconds);
                progress?.OnNext($"Indexed {indexed} file(s)", 100, null);
                indexSpan.Finish(SpanStatus.Ok);
            }
            finally
            {
                progress?.Dispose();
            }

            var locSpan = tx.StartChild("lsp.workspace.localisation", "Load workspace localisation keys");
            try
            {
                await _localisationLoader.LoadAsync(ct);
                locSpan.Finish(SpanStatus.Ok);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Workspace localisation load failed");
                locSpan.Finish(SpanStatus.InternalError);
            }

            _lastRoots = roots;
            tx.Finish(SpanStatus.Ok);
        }
        catch (Exception)
        {
            tx.Finish(SpanStatus.InternalError);
            throw;
        }
    }

    private void OnSchemaRefreshed(object? sender, EventArgs e)
    {
        var roots = _lastRoots;
        if (roots is null) return;

        // Immediately update EaWXmlContext directories so IsEaWXmlFile is correct
        // for any document opens that arrive before the background re-scan completes.
        PreScanMetafiles(roots);

        _ = Task.Run(async () =>
        {
            try
            {
                await ScanAsync(roots, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Schema-refresh workspace re-scan failed");
            }
        });
    }

    private async Task WaitForSchemaAsync(CancellationToken ct)
    {
        _logger.LogInformation(
            "WaitForSchemaAsync: awaiting ReadyAsync (AllTags={TagCount}, AllMetafiles={MetafileCount})",
            _schema.AllTags.Count, _schema.AllMetafiles.Count);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            await _schema.ReadyAsync.WaitAsync(timeout.Token);
            _logger.LogInformation(
                "WaitForSchemaAsync: ready (AllTags={TagCount}, AllMetafiles={MetafileCount})",
                _schema.AllTags.Count, _schema.AllMetafiles.Count);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Schema not ready after 30 s (AllTags={TagCount}); proceeding without metafile pre-scan",
                _schema.AllTags.Count);
        }
    }

    private void PreScanMetafiles(IList<string> roots)
    {
        var baseline = _indexService.Current.Baseline;

        foreach (var def in _schema.AllMetafiles)
        {
            if (def.MetafileType == MetafileType.Special) continue;

            if (def.MetafileType == MetafileType.FileRegistry)
            {
                var metafilePath = _fileHelper.FindInWorkspace(roots, def.Path);
                if (metafilePath is not null)
                {
                    var dir = _fileHelper.FileSystem.Path.GetDirectoryName(metafilePath)!;
                    _eaWXmlContext.AddDirectory(dir);
                    RegisterFromMetafile(metafilePath, def);
                }
                else
                {
                    FallbackFromBaseline(baseline, def, roots);
                }
            }
            else // DirectContent
            {
                var contentPath = _fileHelper.FindInWorkspace(roots, def.Path);
                if (contentPath is not null)
                {
                    var dir = _fileHelper.FileSystem.Path.GetDirectoryName(contentPath)!;
                    _eaWXmlContext.AddDirectory(dir);
                    _fileTypeRegistry.RegisterFile(_fileHelper.PathToFileUri(contentPath),
                        def.Types.ToImmutableArray());
                }
                else
                {
                    FallbackFromBaseline(baseline, def, roots);
                }
            }
        }
    }

    private void RegisterFromMetafile(string metafilePath, MetafileDefinition def)
    {
        string xmlContent;
        try
        {
            xmlContent = _fileHelper.FileSystem.File.ReadAllText(metafilePath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Could not read metafile '{Path}': {Message}", metafilePath, ex.Message);
            return;
        }

        XDocument xdoc;
        try
        {
            xdoc = XDocument.Parse(xmlContent);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Could not parse metafile '{Path}': {Message}", metafilePath, ex.Message);
            return;
        }

        // All game entries live in the same directory as the metafile (e.g. DATA\XML\).
        // Resolving relative to the metafile's directory works whether the workspace root
        // is the game root (…/data/xml/foo.xml) or is the XML directory itself (…/foo.xml).
        var metafileDir = _fileHelper.FileSystem.Path.GetDirectoryName(metafilePath) ?? string.Empty;

        var types = def.Types.ToImmutableArray();
        foreach (var elem in xdoc.Descendants()
                     .Where(e => e.Name.LocalName.Equals("File", StringComparison.OrdinalIgnoreCase)))
        {
            var filename = elem.Value.Trim();
            if (string.IsNullOrEmpty(filename)) continue;
            var normalizedRel = _fileHelper.NormalizeGamePath(filename);
            var entryName = _fileHelper.FileSystem.Path.GetFileName(
                normalizedRel.Replace('/', _fileHelper.FileSystem.Path.DirectorySeparatorChar));
            _fileTypeRegistry.RegisterFile(
                _fileHelper.PathToFileUri(_fileHelper.FileSystem.Path.Combine(metafileDir, entryName)), types);
        }
    }

    private void FallbackFromBaseline(BaselineIndex baseline, MetafileDefinition def, IList<string> roots)
    {
        foreach (var (relativePath, types) in baseline.FileTypeMap)
        {
            if (!types.Any(t => def.Types.Contains(t, StringComparer.OrdinalIgnoreCase))) continue;
            var relSystemPath = relativePath.Replace('/', Path.DirectorySeparatorChar);
            foreach (var root in roots)
                _fileTypeRegistry.RegisterFile(
                    _fileHelper.PathToFileUri(_fileHelper.FileSystem.Path.Combine(root, relSystemPath)), types);
        }
    }
}
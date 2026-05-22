// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.Diagnostics;
using System.IO.Abstractions;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server.WorkDone;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using Sentry;

namespace PG.StarWarsGame.LSP.Server;

public sealed class WorkspaceScanner
{
    private readonly IFileTypeRegistry _fileTypeRegistry;
    private readonly IFileSystem _fs;
    private readonly IGameIndexService _indexService;
    private readonly ILogger<WorkspaceScanner> _logger;
    private readonly IEnumerable<IGameDocumentParser> _parsers;
    private readonly ISchemaProvider _schema;
    private readonly IServerWorkDoneManager? _workDone;

    public WorkspaceScanner(IFileSystem fs, IEnumerable<IGameDocumentParser> parsers,
        IGameIndexService indexService, ILogger<WorkspaceScanner> logger,
        IServerWorkDoneManager? workDone,
        IFileTypeRegistry fileTypeRegistry, ISchemaProvider schema)
    {
        _fs = fs;
        _parsers = parsers;
        _indexService = indexService;
        _logger = logger;
        _workDone = workDone;
        _fileTypeRegistry = fileTypeRegistry;
        _schema = schema;
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

            var files = roots
                .SelectMany(folder => _fs.Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
                .Where(f => _parsers.Any(p => p.CanParse(_fs.Path.GetExtension(f))))
                .ToList();

            _logger.LogInformation("Workspace scan: {Count} parseable file(s) found at {Elapsed} ms", files.Count, sw.ElapsedMilliseconds);

            IWorkDoneObserver? progress = null;
            if (_workDone?.IsSupported == true)
                progress = await _workDone.Create(
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
                        var text = await _fs.File.ReadAllTextAsync(file, token);
                        await _indexService.UpdateDocumentAsync(PathToFileUri(file), text, 0, token);
                        var done = Interlocked.Increment(ref indexed);
                        _logger.LogDebug("Scanned {File} ({Done}/{Total})", file, done, files.Count);
                        progress?.OnNext(null, files.Count > 0 ? (int?)((decimal)done / files.Count * 100) : null, null);
                    });
                } // fires one IndexChanged with the complete final state

                _logger.LogInformation("Bulk-index complete: {Indexed} file(s) at {Elapsed} ms", indexed, sw.ElapsedMilliseconds);
                progress?.OnNext($"Indexed {indexed} file(s)", 100, null);
                indexSpan.Finish(SpanStatus.Ok);
            }
            finally
            {
                progress?.Dispose();
            }

            tx.Finish(SpanStatus.Ok);
        }
        catch (Exception)
        {
            tx.Finish(SpanStatus.InternalError);
            throw;
        }
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
                var metafilePath = FindInWorkspace(roots, def.Path);
                if (metafilePath is not null)
                    RegisterFromMetafile(metafilePath, def);
                else
                    FallbackFromBaseline(baseline, def, roots);
            }
            else // DirectContent
            {
                var contentPath = FindInWorkspace(roots, def.Path);
                if (contentPath is not null)
                    _fileTypeRegistry.RegisterFile(NormalizeAbsolutePath(contentPath),
                        def.Types.ToImmutableArray());
                else
                    FallbackFromBaseline(baseline, def, roots);
            }
        }
    }

    private void RegisterFromMetafile(string metafilePath, MetafileDefinition def)
    {
        string xmlContent;
        try
        {
            xmlContent = _fs.File.ReadAllText(metafilePath);
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
        var metafileDir = _fs.Path.GetDirectoryName(metafilePath) ?? string.Empty;

        var types = def.Types.ToImmutableArray();
        foreach (var elem in xdoc.Descendants()
                     .Where(e => e.Name.LocalName.Equals("File", StringComparison.OrdinalIgnoreCase)))
        {
            var filename = elem.Value.Trim();
            if (string.IsNullOrEmpty(filename)) continue;
            var normalizedRel = NormalizeGamePath(filename);
            var entryName = _fs.Path.GetFileName(normalizedRel.Replace('/', _fs.Path.DirectorySeparatorChar));
            _fileTypeRegistry.RegisterFile(
                NormalizeAbsolutePath(_fs.Path.Combine(metafileDir, entryName)), types);
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
                    NormalizeAbsolutePath(_fs.Path.Combine(root, relSystemPath)), types);
        }
    }

    private string? FindInWorkspace(IList<string> roots, string normalizedRelPath)
    {
        var parts = normalizedRelPath.Split('/');
        foreach (var root in roots)
        {
            var found = TraverseCaseInsensitive(root, parts);
            if (found is not null) return found;
        }

        // XML-dir fallback: workspace root IS the data/xml/ directory — strip the prefix.
        const string xmlPrefix = "data/xml/";
        if (normalizedRelPath.StartsWith(xmlPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var tail = normalizedRelPath[xmlPrefix.Length..].Split('/');
            foreach (var root in roots)
            {
                var found = TraverseCaseInsensitive(root, tail);
                if (found is not null) return found;
            }
        }

        return null;
    }

    // Case-insensitive path traversal so game files with mixed case work on Linux too.
    private string? TraverseCaseInsensitive(string dir, string[] parts)
    {
        var current = dir;
        for (var i = 0; i < parts.Length; i++)
        {
            if (!_fs.Directory.Exists(current)) return null;
            var match = _fs.Directory.EnumerateFileSystemEntries(current)
                .FirstOrDefault(e => string.Equals(
                    _fs.Path.GetFileName(e), parts[i], StringComparison.OrdinalIgnoreCase));
            if (match is null) return null;
            if (i == parts.Length - 1)
                return _fs.File.Exists(match) ? match : null;
            current = match;
        }

        return null;
    }

    private static string NormalizeGamePath(string raw)
    {
        return raw.Replace('\\', '/').ToLowerInvariant().TrimStart('/');
    }

    private static string NormalizeAbsolutePath(string path)
    {
        return path.Replace('\\', '/').ToLowerInvariant();
    }

    private static string PathToFileUri(string path)
    {
        var forward = path.Replace('\\', '/');
        return forward.StartsWith('/') ? "file://" + forward : "file:///" + forward;
    }
}
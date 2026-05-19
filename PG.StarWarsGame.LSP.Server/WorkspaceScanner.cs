// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Abstractions;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server.WorkDone;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Server;

public sealed class WorkspaceScanner
{
    private readonly IFileSystem _fs;
    private readonly IFileTypeRegistry _fileTypeRegistry;
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
        var roots = workspaceFolders.ToList();
        PreScanMetafiles(roots);

        var files = roots
            .SelectMany(folder => _fs.Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            .Where(f => _parsers.Any(p => p.CanParse(_fs.Path.GetExtension(f))))
            .ToList();

        _logger.LogInformation("Workspace scan: {Count} parseable file(s) found", files.Count);

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
        try
        {
            using (_indexService.BeginBulkUpdate())
            {
                await Parallel.ForEachAsync(files, options, async (file, token) =>
                {
                    var text = await _fs.File.ReadAllTextAsync(file, token);
                    await _indexService.UpdateDocumentAsync(file, text, 0, token);
                    var done = Interlocked.Increment(ref indexed);
                    _logger.LogDebug("Scanned {File} ({Done}/{Total})", file, done, files.Count);
                    progress?.OnNext(null, files.Count > 0 ? (int?)((decimal)done / files.Count * 100) : null, null);
                });
            } // fires one IndexChanged with the complete final state

            progress?.OnNext($"Indexed {indexed} file(s)", 100, null);
        }
        finally
        {
            progress?.Dispose();
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
                    RegisterFromMetafile(metafilePath, def, roots);
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

    private void RegisterFromMetafile(string metafilePath, MetafileDefinition def, IList<string> roots)
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

        var types = def.Types.ToImmutableArray();
        foreach (var elem in xdoc.Descendants()
                     .Where(e => e.Name.LocalName.Equals("File", StringComparison.OrdinalIgnoreCase)))
        {
            var filename = elem.Attributes()
                .FirstOrDefault(a => a.Name.LocalName.Equals("filename", StringComparison.OrdinalIgnoreCase))?.Value;
            if (string.IsNullOrEmpty(filename)) continue;
            var normalizedRel = NormalizeGamePath(filename);
            var relSystemPath = normalizedRel.Replace('/', Path.DirectorySeparatorChar);
            foreach (var root in roots)
                _fileTypeRegistry.RegisterFile(
                    NormalizeAbsolutePath(_fs.Path.Combine(root, relSystemPath)), types);
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
        var relPath = normalizedRelPath.Replace('/', Path.DirectorySeparatorChar);
        foreach (var root in roots)
        {
            var candidate = _fs.Path.Combine(root, relPath);
            if (_fs.File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private static string NormalizeGamePath(string raw) =>
        raw.Replace('\\', '/').ToLowerInvariant().TrimStart('/');

    private static string NormalizeAbsolutePath(string path) =>
        path.Replace('\\', '/').ToLowerInvariant();
}
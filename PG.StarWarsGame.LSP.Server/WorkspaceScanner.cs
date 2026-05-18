// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server.WorkDone;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Server;

public sealed class WorkspaceScanner
{
    private readonly IFileSystem _fs;
    private readonly IGameIndexService _indexService;
    private readonly ILogger<WorkspaceScanner> _logger;
    private readonly IEnumerable<IGameDocumentParser> _parsers;
    private readonly IServerWorkDoneManager? _workDone;

    public WorkspaceScanner(IFileSystem fs, IEnumerable<IGameDocumentParser> parsers,
        IGameIndexService indexService, ILogger<WorkspaceScanner> logger,
        IServerWorkDoneManager? workDone)
    {
        _fs = fs;
        _parsers = parsers;
        _indexService = indexService;
        _logger = logger;
        _workDone = workDone;
    }

    public async Task ScanAsync(IEnumerable<string> workspaceFolders, CancellationToken ct)
    {
        var files = workspaceFolders
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
}
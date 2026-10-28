// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using PG.StarWarsGame.LSP.Core.Caching;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Server.Startup;

namespace PG.StarWarsGame.LSP.Server.Indexing;

/// <summary>
///     The document stage: parses every file the project's layers declare into the game index in one
///     bulk update, layer by layer, replaying a layer's index snapshot for each file whose content is
///     unchanged and writing the layer's snapshot afresh afterwards.
/// </summary>
public sealed class DocumentIndexer
{
    private readonly IProjectIndexCache _cache;
    private readonly IFileHelper _fileHelper;
    private readonly ParseableFiles _files;
    private readonly IndexFingerprints _fingerprints;
    private readonly IGameIndexService _indexService;
    private readonly ILogger<DocumentIndexer> _logger;

    public DocumentIndexer(IFileHelper fileHelper, IGameIndexService indexService, IProjectIndexCache cache,
        ParseableFiles files, IndexFingerprints fingerprints, ILogger<DocumentIndexer> logger)
    {
        _fileHelper = fileHelper;
        _indexService = indexService;
        _cache = cache;
        _files = files;
        _fingerprints = fingerprints;
        _logger = logger;
    }

    /// <summary>How the last scan used the index snapshots. Null before the first scan.</summary>
    public IndexCacheStats? LastIndexCache { get; private set; }

    /// <summary>
    ///     Bulk-indexes every parseable XML file under the declared XML directories (gated by
    ///     <see cref="IEaWXmlContext.IsEaWXmlFile" />) and every parseable script file under
    ///     the declared script roots, in a single <see cref="IGameIndexService.BeginBulkUpdate" />.
    ///     Returns the number of files indexed.
    /// </summary>
    public async Task<int> IndexAsync(WorkspaceConfiguration config, CancellationToken ct,
        Action<int, int>? progress = null)
    {
        return config.Layers.Count > 0
            ? await IndexByLayerAsync(config, progress, ct)
            : await IndexFlatAsync(config, progress, ct);
    }

    // Original flat scan used when there is no pgproj dependency graph.
    private async Task<int> IndexFlatAsync(WorkspaceConfiguration config,
        Action<int, int>? progress, CancellationToken ct)
    {
        var files = _files.Collect(config.XmlDirectories, config.ScriptRoots);
        if (_logger.IsEnabled(LogLevel.Information))
            _logger.LogInformation("WorkspaceIndexer: {Count} parseable file(s) found (flat scan)", files.Count);

        var indexed = 0;
        var options = new ParallelOptions
            { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = ct };
        using (_indexService.BeginBulkUpdate())
        {
            await Parallel.ForEachAsync(files, options, async (file, token) =>
            {
                var text = await _fileHelper.FileSystem.File.ReadAllTextAsync(file, token);
                var uri = _fileHelper.PathToFileUri(file);
                await _indexService.UpdateDocumentAsync(uri, text, 0, token);
                var done = Interlocked.Increment(ref indexed);
                progress?.Invoke(done, files.Count);
            });
        }

        // No layers means no snapshots: everything was parsed.
        LastIndexCache = new IndexCacheStats(0, 0, 0, indexed);
        if (_logger.IsEnabled(LogLevel.Information))
            _logger.LogInformation("WorkspaceIndexer: bulk-index complete, {Indexed} file(s)", indexed);
        return indexed;
    }

    // Per-layer scan with project index cache support.
    private async Task<int> IndexByLayerAsync(WorkspaceConfiguration config,
        Action<int, int>? progress, CancellationToken ct)
    {
        // Build per-layer file lists (dependencies first - ordered by rank).
        var layerFileLists = config.Layers
            .OrderBy(l => l.Rank)
            .Select(l => (Layer: l, Files: _files.Collect(l.XmlDirectories, l.ScriptRoots)))
            .ToList();

        var totalFiles = layerFileLists.Sum(l => l.Files.Count);
        if (_logger.IsEnabled(LogLevel.Information))
            _logger.LogInformation("WorkspaceIndexer: {Count} parseable file(s) found (layered scan)", totalFiles);

        var indexed = 0;
        var reused = 0;
        var layersFromSnapshot = 0;
        var options = new ParallelOptions
            { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = ct };

        // The schema drives what the parser emits (which tags produce references, which files
        // carry types). Computed once per scan.
        var schemaFingerprint = _fingerprints.Schema();

        // (pgprojPath -> overallHash), used to skip a redundant re-save of an unchanged snapshot.
        // Folded: a dependency is looked up here by the path a DIFFERENT project spelled in its
        // projectReferences, so the two sides only agree by luck under an ordinal comparison.
        var layerOverallHashes = new Dictionary<string, string>(DocumentUris.Comparer);

        // What each layer's documents read from OUTSIDE their own layer. Computed here rather than
        // per file: PreScanMetafiles has already run, so the file-type registry is complete, and
        // the xml directory union is fixed for the scan. The value is also the snapshot's CONTEXT
        // KEY - a dependency indexed under two different leaves keeps one file per context.
        var crossLayerFingerprints = layerFileLists.ToDictionary(
            l => l.Layer,
            l => _fingerprints.CrossLayer(l.Files, l.Layer.XmlDirectories, config.XmlDirectories));

        // (pgprojPath -> OverallHash) of snapshots that survived dependency validation - when the
        // recomputed hash matches, the snapshot on disk is already current and is not re-saved.
        var validSnapshotHashes = new Dictionary<string, string>(DocumentUris.Comparer);

        // Collect per-layer results for snapshot writing after the bulk update.
        var layerResults = new List<(ProjectLayer Layer, List<(string RelPath, string AbsUri, string Hash)> Entries)>();

        using (_indexService.BeginBulkUpdate())
        {
            foreach (var (layer, files) in layerFileLists)
            {
                // Normalize the pgproj path (production always normalizes via ProjectDependencyGraph,
                // but defensive normalization ensures robustness in tests and edge cases).
                var pgprojPath = layer.ProjectPath is { } projectPath ? GamePaths.Forward(projectPath) : null;

                var crossLayer = crossLayerFingerprints[layer];
                var snapshot = pgprojPath is not null ? _cache.TryLoad(pgprojPath, crossLayer) : null;

                // Snapshot built under a different (or pre-fingerprint) schema - discard it.
                if (snapshot is not null && snapshot.SchemaFingerprint != schemaFingerprint)
                {
                    if (_logger.IsEnabled(LogLevel.Information))
                        _logger.LogInformation(
                            "Schema changed since layer '{Layer}' was cached; discarding its snapshot",
                            layer.Name);
                    snapshot = null;
                }

                // Cached parses are not content-pure: what a document emits depends on inputs
                // decided outside its own layer, so the layer's own file hashes cannot catch a
                // change to those. The key names those inputs exactly - the file-type
                // registrations for THIS layer's files, and the xml roots that can match them -
                // rather than asking the far coarser "did any dependency change at all", which
                // discarded a whole dependent layer for one changed line in a shared core. The
                // file was found BY this key, so a mismatch here is a collision of its short form
                // in the file name - a re-parse, never a stale index.
                if (snapshot is not null && snapshot.CrossLayerFingerprint != crossLayer)
                {
                    if (_logger.IsEnabled(LogLevel.Information))
                        _logger.LogInformation(
                            "Cross-layer inputs changed since layer '{Layer}' was cached; discarding its snapshot",
                            layer.Name);
                    snapshot = null;
                }

                // Overall hash of the validated snapshot, for skipping the redundant re-save below.
                if (snapshot is not null && pgprojPath is not null)
                    validSnapshotHashes[pgprojPath] = snapshot.OverallHash;
                if (snapshot is not null) layersFromSnapshot++;

                // Build lookup from relPath -> cached entry.
                var projectDir = pgprojPath is not null ? GetDirectory(pgprojPath) : null;
                // Folded, like every other lookup keyed by a document path: the snapshot may spell
                // a path in a case neither this scan nor this version would produce, and the
                // engine calls those the same file. Built by hand rather than with ToDictionary
                // because a case-sensitive host can hold Units.xml and units.xml at once - two
                // distinct entries that collide under the fold, which ToDictionary would throw on
                // in the middle of indexing. The engine cannot tell them apart either, so keeping
                // one of the two is the honest answer; a re-parse is the worst case.
                Dictionary<string, ProjectFileEntry>? cacheHits = null;
                if (snapshot is not null)
                {
                    cacheHits = new Dictionary<string, ProjectFileEntry>(DocumentUris.Comparer);
                    foreach (var cached in snapshot.Files) cacheHits[cached.RelativePath] = cached;
                }

                var layerEntries = new ConcurrentBag<(string RelPath, string AbsUri, string Hash)>();

                await Parallel.ForEachAsync(files, options, async (file, token) =>
                {
                    var uri = _fileHelper.PathToFileUri(file);
                    var relPath = projectDir is not null
                        ? NormalizePath(_fileHelper.FileSystem.Path.GetRelativePath(projectDir, file))
                        : file;

                    var (hash, text) = ProjectFileHasher.ReadAndHash(file, _fileHelper.FileSystem);
                    layerEntries.Add((relPath, uri, hash));

                    if (cacheHits is not null
                        && cacheHits.TryGetValue(relPath, out var cachedEntry)
                        && cachedEntry.ContentHash == hash)
                    {
                        _indexService.InjectDocument(ProjectIndexSerializer.FromEntry(cachedEntry, uri));
                        // Injecting skips the parser, so anything the parse publishes ELSEWHERE
                        // never happened. Replay it, or a cached Lua file contributes none of its
                        // annotations for the whole session.
                        RestoreParserState(file, uri, cachedEntry.Document.ParserState);
                        Interlocked.Increment(ref reused);
                    }
                    else
                    {
                        await _indexService.UpdateDocumentAsync(uri, text, 0, token);
                    }

                    var done = Interlocked.Increment(ref indexed);
                    progress?.Invoke(done, totalFiles);
                });

                // Compute this layer's overall hash and record it for later layers' dep-hash tracking.
                if (pgprojPath is not null)
                {
                    var overallHash = ProjectFileHasher.ComputeProjectHash(
                        layerEntries.Select(e => (e.RelPath, e.Hash)));
                    layerOverallHashes[pgprojPath] = overallHash;
                }

                layerResults.Add((layer, [.. layerEntries]));
            }
        } // fires one IndexChanged with the complete final state

        LastIndexCache = new IndexCacheStats(
            layersFromSnapshot, layerFileLists.Count - layersFromSnapshot, reused, indexed - reused);

        // Write snapshots after the bulk update so Current.Documents is fully populated.
        foreach (var (layer, entries) in layerResults)
        {
            var pgprojPath = layer.ProjectPath is { } projectPath ? GamePaths.Forward(projectPath) : null;
            if (pgprojPath is null) continue;

            var overallHash = layerOverallHashes.GetValueOrDefault(pgprojPath, string.Empty);

            // Nothing changed since the snapshot on disk was written - skip the re-serialization.
            if (validSnapshotHashes.TryGetValue(pgprojPath, out var priorHash) && priorHash == overallHash)
            {
                if (_logger.IsEnabled(LogLevel.Debug))
                    _logger.LogDebug("Layer snapshot '{Pgproj}' unchanged; skipping save", pgprojPath);
                continue;
            }

            var snapshotFiles = entries
                .Select(e =>
                {
                    if (_indexService.Current.Documents.TryGetValue(e.AbsUri, out var doc))
                        return ProjectIndexSerializer.ToEntry(
                            e.RelPath, e.Hash, doc, CaptureParserState(e.RelPath, e.AbsUri));
                    return null;
                })
                .Where(f => f is not null)
                .Cast<ProjectFileEntry>()
                .ToArray();

            var newSnapshot = new ProjectIndexSnapshot
            {
                SchemaVersion = ProjectIndexSnapshot.CurrentSchemaVersion,
                OverallHash = overallHash,
                // Superseded by CrossLayerFingerprint; written empty so the Key stays append-only.
                DependencyHashes = [],
                Files = snapshotFiles,
                SchemaFingerprint = schemaFingerprint,
                CrossLayerFingerprint = crossLayerFingerprints[layer]
            };
            _cache.Save(pgprojPath, crossLayerFingerprints[layer], newSnapshot);
            _cache.EnsureGitHygiene(pgprojPath);
        }

        if (_logger.IsEnabled(LogLevel.Information))
            _logger.LogInformation("WorkspaceIndexer: bulk-index complete, {Indexed} file(s)", indexed);
        return indexed;
    }

    /// <summary>
    ///     The state the parser that handles this file published outside the document's own index,
    ///     for persisting beside it. Empty for every parser that has none.
    /// </summary>
    private byte[]? CaptureParserState(string relPath, string absUri)
    {
        var parser = _files.ParserFor(relPath);
        return parser?.CaptureParserState(absUri);
    }

    /// <summary>Replays a cached parser state, if the owning parser has one to replay.</summary>
    private void RestoreParserState(string file, string absUri, byte[] state)
    {
        if (state.Length == 0) return;
        _files.ParserFor(file)?.RestoreParserState(absUri, state);
    }

    // Separators only. The fold that used to live here belongs to the COMPARISON - see
    // DocumentUris - because this value is a project-relative path we hand back to the filesystem,
    // and a case-sensitive host cannot open a name we flattened on the way in.
    private static string NormalizePath(string path)
    {
        return GamePaths.Forward(path);
    }

    private static string GetDirectory(string normalizedPath)
    {
        var idx = normalizedPath.LastIndexOf('/');
        return idx < 0 ? string.Empty : normalizedPath[..idx];
    }
}

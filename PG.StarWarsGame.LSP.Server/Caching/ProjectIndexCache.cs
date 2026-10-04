// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using PG.StarWarsGame.LSP.Core.Caching;
using PG.StarWarsGame.LSP.Core.Util;

namespace PG.StarWarsGame.LSP.Server.Caching;

/// <summary>
///     Index snapshots on disk, one file per (layer, context key), bounded per layer. Writes are
///     atomic and the directory maintenance runs under the cross-process lock: one server runs per
///     open project, and several of them start together on a shared dependency's <c>.aetswg/</c>.
/// </summary>
public sealed class ProjectIndexCache : IProjectIndexCache
{
    /// <summary>
    ///     How many contexts of one layer are kept. MEASURED 2026-10-04 on EaWX: core has four
    ///     distinct contexts across its five openings (alone, Rev, FotR, TR, CoreSaga), so four
    ///     would sit exactly at the cap and one more leaf would bring the re-parse back. Eight
    ///     leaves room; a context not used for a while is simply re-parsed once, and a core
    ///     snapshot is under a megabyte.
    /// </summary>
    private const int SnapshotsKeptPerLayer = 8;

    // Every generated directory under .aetswg. Listed rather than a bare "*" so a project can
    // still keep something of its own in here.
    private static readonly string[] GitignoreEntries = ["indices/", "bones/"];

    private static readonly string GitignoreHeader =
        "# Remove a line below to share that cache with your team via version control\n";

    private static readonly string GitattributesContent = "*.msgpack binary\n";

    private readonly IFileHelper _fileHelper;
    private readonly ICrossProcessLock _lock;
    private readonly ILogger<ProjectIndexCache> _logger;

    public ProjectIndexCache(IFileHelper fileHelper, ICrossProcessLock processLock,
        ILogger<ProjectIndexCache> logger)
    {
        _fileHelper = fileHelper;
        _lock = processLock;
        _logger = logger;
    }

    public ProjectIndexSnapshot? TryLoad(string pgprojPath, string contextKey)
    {
        var indexPath = ProjectIndexLocator.GetIndexFilePath(pgprojPath, contextKey);
        if (!_fileHelper.FileSystem.File.Exists(indexPath))
            return null;

        try
        {
            var bytes = _fileHelper.FileSystem.File.ReadAllBytes(indexPath);
            var snapshot = ProjectIndexSerializer.Deserialize(bytes);
            if (snapshot is null)
                _logger.LogDebug("Project index snapshot at '{Path}' is stale or corrupt; will re-index", indexPath);
            return snapshot;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read project index snapshot from '{Path}'", indexPath);
            return null;
        }
    }

    public void Save(string pgprojPath, string contextKey, ProjectIndexSnapshot snapshot)
    {
        var fs = _fileHelper.FileSystem;
        var indexPath = ProjectIndexLocator.GetIndexFilePath(pgprojPath, contextKey);
        var indexDir = ProjectIndexLocator.GetIndexDirectory(pgprojPath);
        var bytes = ProjectIndexSerializer.Serialize(snapshot);

        // The write itself is atomic; the lock is for the housekeeping around it, where two
        // servers listing and deleting the same directory at once would trip over each other.
        using (_lock.Acquire(indexDir))
        {
            try
            {
                AtomicFile.WriteAllBytes(fs, indexPath, bytes);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Another server may hold this very file open for its own scan right now, and on
                // Windows the move over it fails. The snapshot is a start-up accelerator, not the
                // index: the scan that produced it is complete, and the next start saves again.
                _logger.LogWarning(ex, "Could not save project index snapshot to '{Path}'; the next start will retry",
                    indexPath);
                return;
            }

            RemoveLegacySnapshot(pgprojPath);
            PruneOlderContexts(pgprojPath, indexPath);
        }

        _logger.LogDebug("Saved project index snapshot to '{Path}' ({Files} files)", indexPath,
            snapshot.Files.Length);
    }

    public void EnsureGitHygiene(string pgprojPath)
    {
        var aetswgDir = ProjectIndexLocator.GetAetswgDirectory(pgprojPath);
        _fileHelper.FileSystem.Directory.CreateDirectory(aetswgDir);

        // Read-compare-write: without the lock two servers both read "missing" and both append.
        using (_lock.Acquire(aetswgDir))
        {
            EnsureIgnored(aetswgDir + "/.gitignore");
            WriteIfAbsent(aetswgDir + "/.gitattributes", GitattributesContent);
        }
    }

    /// <summary>
    ///     Versions before the context key wrote <c>&lt;stem&gt;.msgpack</c>. Nothing reads it any
    ///     more; left behind it looks like a cache and is not one.
    /// </summary>
    private void RemoveLegacySnapshot(string pgprojPath)
    {
        var legacy = ProjectIndexLocator.GetLegacyIndexFilePath(pgprojPath);
        if (!_fileHelper.FileSystem.File.Exists(legacy)) return;
        try
        {
            _fileHelper.FileSystem.File.Delete(legacy);
        }
        catch (IOException ex)
        {
            _logger.LogDebug(ex, "Could not remove the legacy snapshot '{Path}'", legacy);
        }
    }

    /// <summary>
    ///     Keeps the newest <see cref="SnapshotsKeptPerLayer" /> contexts of this layer, the one
    ///     just written always among them, so a project opened under many short-lived contexts
    ///     does not collect snapshots forever. Only this layer's files are candidates.
    /// </summary>
    private void PruneOlderContexts(string pgprojPath, string justWritten)
    {
        var fs = _fileHelper.FileSystem;
        var indexDir = ProjectIndexLocator.GetIndexDirectory(pgprojPath);
        var pattern = ProjectIndexLocator.GetIndexFilePattern(pgprojPath);

        // By file name, not full path: the directory listing may spell the root differently
        // from the path this class built (drive letter, separators), and the one file that must
        // survive is the one just written.
        var justWrittenName = fs.Path.GetFileName(justWritten);
        var others = fs.Directory.GetFiles(indexDir, pattern)
            .Where(p => !string.Equals(fs.Path.GetFileName(p), justWrittenName, StringComparison.OrdinalIgnoreCase))
            .Select(p => (Path: p, Written: fs.File.GetLastWriteTimeUtc(p)))
            .OrderByDescending(e => e.Written)
            .Skip(SnapshotsKeptPerLayer - 1);

        foreach (var (path, _) in others)
            try
            {
                fs.File.Delete(path);
                _logger.LogDebug("Pruned old project index snapshot '{Path}'", path);
            }
            catch (IOException ex)
            {
                // Another server may be reading it right now; it is tried again on the next save.
                _logger.LogDebug(ex, "Could not prune '{Path}'", path);
            }
    }

    /// <summary>
    ///     Makes sure every generated directory is ignored, APPENDING to a .gitignore that already
    ///     exists rather than leaving it as it was.
    /// </summary>
    /// <remarks>
    ///     Write-if-absent was enough while <c>indices/</c> was the only entry, but it silently
    ///     skips every project set up before a new cache was added - so bone snapshots would have
    ///     been committed by everyone who had ever opened the project before today. An entry the
    ///     author deliberately deleted comes back; that is the lesser harm, and the header says how
    ///     to opt out.
    /// </remarks>
    private void EnsureIgnored(string path)
    {
        var fs = _fileHelper.FileSystem;
        var existing = fs.File.Exists(path) ? fs.File.ReadAllText(path) : string.Empty;

        var lines = existing.Replace("\r\n", "\n").Split('\n');
        var missing = GitignoreEntries
            .Where(entry => !lines.Any(line => line.Trim().Equals(entry, StringComparison.Ordinal)))
            .ToArray();

        if (missing.Length == 0) return;

        var content = existing.Length == 0
            ? GitignoreHeader + string.Join('\n', missing) + "\n"
            : existing.TrimEnd('\n', '\r') + "\n" + string.Join('\n', missing) + "\n";

        AtomicFile.WriteAllText(fs, path, content);
    }

    private void WriteIfAbsent(string path, string content)
    {
        if (!_fileHelper.FileSystem.File.Exists(path))
            AtomicFile.WriteAllText(_fileHelper.FileSystem, path, content);
    }
}
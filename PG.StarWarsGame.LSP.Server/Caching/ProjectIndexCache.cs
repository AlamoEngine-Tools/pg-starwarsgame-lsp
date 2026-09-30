// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using PG.StarWarsGame.LSP.Core.Caching;
using PG.StarWarsGame.LSP.Core.Util;

namespace PG.StarWarsGame.LSP.Server.Caching;

public sealed class ProjectIndexCache : IProjectIndexCache
{
    // Every generated directory under .aetswg. Listed rather than a bare "*" so a project can
    // still keep something of its own in here.
    private static readonly string[] GitignoreEntries = ["indices/", "bones/"];

    private static readonly string GitignoreHeader =
        "# Remove a line below to share that cache with your team via version control\n";

    private static readonly string GitattributesContent = "*.msgpack binary\n";

    private readonly IFileHelper _fileHelper;
    private readonly ILogger<ProjectIndexCache> _logger;

    public ProjectIndexCache(IFileHelper fileHelper, ILogger<ProjectIndexCache> logger)
    {
        _fileHelper = fileHelper;
        _logger = logger;
    }

    public ProjectIndexSnapshot? TryLoad(string pgprojPath)
    {
        var indexPath = ProjectIndexLocator.GetIndexFilePath(pgprojPath);
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

    public void Save(string pgprojPath, ProjectIndexSnapshot snapshot)
    {
        var indexPath = ProjectIndexLocator.GetIndexFilePath(pgprojPath);
        var indexDir = _fileHelper.FileSystem.Path.GetDirectoryName(indexPath)!;
        _fileHelper.FileSystem.Directory.CreateDirectory(indexDir);

        var bytes = ProjectIndexSerializer.Serialize(snapshot);

        // Atomic write: serialize to a temp file then rename over the target.
        var tempPath = indexPath + ".tmp";
        _fileHelper.FileSystem.File.WriteAllBytes(tempPath, bytes);
        if (_fileHelper.FileSystem.File.Exists(indexPath))
            _fileHelper.FileSystem.File.Delete(indexPath);
        _fileHelper.FileSystem.File.Move(tempPath, indexPath);

        _logger.LogDebug("Saved project index snapshot to '{Path}' ({Files} files)", indexPath,
            snapshot.Files.Length);
    }

    public void EnsureGitHygiene(string pgprojPath)
    {
        var aetswgDir = ProjectIndexLocator.GetAetswgDirectory(pgprojPath);
        _fileHelper.FileSystem.Directory.CreateDirectory(aetswgDir);

        EnsureIgnored(aetswgDir + "/.gitignore");
        WriteIfAbsent(aetswgDir + "/.gitattributes", GitattributesContent);
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

        fs.File.WriteAllText(path, content);
    }

    private void WriteIfAbsent(string path, string content)
    {
        if (!_fileHelper.FileSystem.File.Exists(path))
            _fileHelper.FileSystem.File.WriteAllText(path, content);
    }
}
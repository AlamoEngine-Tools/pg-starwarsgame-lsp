// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using PG.StarWarsGame.LSP.Core.Caching;
using PG.StarWarsGame.LSP.Core.Util;

namespace PG.StarWarsGame.LSP.Server.Caching;

public sealed class ProjectIndexCache : IProjectIndexCache
{
    private static readonly string GitignoreContent =
        "# Remove the line below to share index snapshots with your team via version control\nindices/\n";

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

        WriteIfAbsent(aetswgDir + "/.gitignore", GitignoreContent);
        WriteIfAbsent(aetswgDir + "/.gitattributes", GitattributesContent);
    }

    private void WriteIfAbsent(string path, string content)
    {
        if (!_fileHelper.FileSystem.File.Exists(path))
            _fileHelper.FileSystem.File.WriteAllText(path, content);
    }
}
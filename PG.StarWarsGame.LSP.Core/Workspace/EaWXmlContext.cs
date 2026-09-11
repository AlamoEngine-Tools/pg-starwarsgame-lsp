// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Util;

namespace PG.StarWarsGame.LSP.Core.Workspace;

public sealed class EaWXmlContext : IEaWXmlContext
{
    private readonly IFileHelper _fileHelper;
    private ImmutableHashSet<string> _directories = ImmutableHashSet<string>.Empty;
    private ImmutableHashSet<string> _leafDirectories = ImmutableHashSet<string>.Empty;

    public EaWXmlContext(IFileHelper fileHelper)
    {
        _fileHelper = fileHelper;
    }

    public bool HasDirectories => !_directories.IsEmpty;

    public bool IsEaWXmlFile(string fileUri)
    {
        var normalized = _fileHelper.NormalizeUri(fileUri);
        if (!normalized.StartsWith("file:///", StringComparison.Ordinal)) return false;

        // TODO: implement proper AI XML parsing - AI files use a different format that
        //       requires a dedicated parser; exclude them until that parser exists.
        // Segment and prefix tests over a URI fold, like every other comparison of one: the value
        // keeps the file's real case now, so an ordinal test here would miss "/AI/" on disk.
        if (DocumentUris.Contains(normalized, "/ai/")) return false;

        return _directories.Any(dir => DocumentUris.StartsWith(normalized, dir));
    }

    public bool IsLeafFile(string fileUri)
    {
        var normalized = _fileHelper.NormalizeUri(fileUri);
        return _leafDirectories.Any(dir => DocumentUris.StartsWith(normalized, dir));
    }

    public string? TryGetXmlRelativePath(string fileUri)
    {
        var normalized = _fileHelper.NormalizeUri(fileUri);

        // Longest matching directory wins, so a file under a nested xml root is made relative to
        // that root rather than an ancestor (directories are stored with a trailing '/').
        var root = _directories
            .Where(dir => DocumentUris.StartsWith(normalized, dir))
            .OrderByDescending(dir => dir.Length)
            .FirstOrDefault();
        if (root is null) return null;

        return Uri.UnescapeDataString(normalized[root.Length..]);
    }

    public void AddDirectory(string absolutePath)
    {
        var uri = absolutePath.StartsWith("file://", StringComparison.OrdinalIgnoreCase)
            ? _fileHelper.NormalizeUri(absolutePath)
            : _fileHelper.PathToFileUri(absolutePath);
        var prefix = uri.TrimEnd('/') + '/';
        ImmutableInterlocked.Update(ref _directories, d => d.Add(prefix));
    }

    public void SetDirectories(IEnumerable<string> absolutePaths)
    {
        var next = ImmutableHashSet<string>.Empty;
        foreach (var path in absolutePaths)
        {
            var uri = path.StartsWith("file://", StringComparison.OrdinalIgnoreCase)
                ? _fileHelper.NormalizeUri(path)
                : _fileHelper.PathToFileUri(path);
            next = next.Add(uri.TrimEnd('/') + '/');
        }

        Volatile.Write(ref _directories, next);
    }

    public void SetLeafDirectories(IEnumerable<string> absolutePaths)
    {
        var next = ImmutableHashSet<string>.Empty;
        foreach (var path in absolutePaths)
        {
            var uri = path.StartsWith("file://", StringComparison.OrdinalIgnoreCase)
                ? _fileHelper.NormalizeUri(path)
                : _fileHelper.PathToFileUri(path);
            next = next.Add(uri.TrimEnd('/') + '/');
        }

        Volatile.Write(ref _leafDirectories, next);
    }
}
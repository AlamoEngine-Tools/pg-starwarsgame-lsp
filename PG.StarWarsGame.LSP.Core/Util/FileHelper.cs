// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions;

namespace PG.StarWarsGame.LSP.Core.Util;

public sealed class FileHelper : IFileHelper
{
    public FileHelper(IFileSystem fileSystem)
    {
        FileSystem = fileSystem;
    }

    public IFileSystem FileSystem { get; }

    public string PathToFileUri(string path)
    {
        var forward = path.Replace('\\', '/').ToLowerInvariant();
        return forward.StartsWith('/') ? "file://" + forward : "file:///" + forward;
    }

    public string NormalizeUri(string pathOrUri)
    {
        if (pathOrUri.StartsWith("file:///", StringComparison.OrdinalIgnoreCase))
            return PathToFileUri(Uri.UnescapeDataString(pathOrUri[8..]));
        if (pathOrUri.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            return PathToFileUri(Uri.UnescapeDataString(pathOrUri[7..]));
        return PathToFileUri(pathOrUri);
    }

    public bool UrisEqual(string a, string b)
    {
        return string.Equals(NormalizeUri(a), NormalizeUri(b), StringComparison.Ordinal);
    }

    public string? FindInWorkspace(IList<string> roots, string normalizedRelPath)
    {
        var parts = normalizedRelPath.Split('/');
        foreach (var root in roots)
        {
            var found = TraverseCaseInsensitive(FileSystem, root, parts);
            if (found is not null) return found;
        }

        // XML-dir fallback: workspace root IS the data/xml/ directory — strip the prefix.
        const string xmlPrefix = "data/xml/";
        if (normalizedRelPath.StartsWith(xmlPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var tail = normalizedRelPath[xmlPrefix.Length..].Split('/');
            foreach (var root in roots)
            {
                var found = TraverseCaseInsensitive(FileSystem, root, tail);
                if (found is not null) return found;
            }
        }

        return null;
    }

    public string NormalizeGamePath(string raw)
    {
        return raw.Replace('\\', '/').ToLowerInvariant().TrimStart('/');
    }

    public string? FileUriToPath(string fileUri)
    {
        if (string.IsNullOrEmpty(fileUri)) return null;
        if (!fileUri.StartsWith("file://", StringComparison.OrdinalIgnoreCase)) return null;
        var normalized = NormalizeUri(fileUri);
        if (!normalized.StartsWith("file:///", StringComparison.Ordinal)) return null;
        return normalized[8..].Replace('/', FileSystem.Path.DirectorySeparatorChar);
    }

    // Case-insensitive path traversal so game files with mixed case work on Linux too.
    public string? TraverseCaseInsensitive(IFileSystem _fs, string dir, string[] parts)
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
}
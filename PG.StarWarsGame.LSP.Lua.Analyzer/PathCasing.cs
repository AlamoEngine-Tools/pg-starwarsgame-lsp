// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Lua.Analyzer;

/// <summary>A directory path as the file system spells it.</summary>
public static class PathCasing
{
    /// <summary>
    ///     <paramref name="path" /> with every segment that exists in its case on disk and the rest as
    ///     given; unchanged when not even its first segment exists. The project layers keep their
    ///     roots lower-cased for comparison; the analyzer compares paths as written, so it has to be
    ///     told the spelling the editor will use - also for a declared folder not created yet.
    /// </summary>
    public static string OnDisk(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            var root = Path.GetPathRoot(full) ?? "";
            var segments = full[root.Length..].Split(Path.DirectorySeparatorChar,
                StringSplitOptions.RemoveEmptyEntries);
            var current = root.ToUpperInvariant();
            var matched = 0;
            for (; matched < segments.Length; matched++)
            {
                if (!Directory.Exists(current)) break;
                var segment = segments[matched];
                var match = Directory.EnumerateFileSystemEntries(current)
                    .FirstOrDefault(e =>
                        string.Equals(Path.GetFileName(e), segment, StringComparison.OrdinalIgnoreCase));
                if (match is null) break;
                current = match;
            }

            return matched == 0 ? path : Path.Combine([current, .. segments[matched..]]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return path;
        }
    }
}
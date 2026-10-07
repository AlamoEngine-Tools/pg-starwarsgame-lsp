// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Lua.Analyzer;

/// <summary>A directory path as the file system spells it.</summary>
public static class PathCasing
{
    /// <summary>
    ///     <paramref name="path" /> with every segment in its case on disk, or unchanged when it does
    ///     not exist. The project layers keep their roots lower-cased for comparison; the analyzer
    ///     compares paths as written, so it has to be told the spelling the editor will use.
    /// </summary>
    public static string OnDisk(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            if (!Directory.Exists(full)) return path;
            var root = Path.GetPathRoot(full) ?? "";
            var current = root.ToUpperInvariant();
            foreach (var segment in full[root.Length..].Split(Path.DirectorySeparatorChar,
                         StringSplitOptions.RemoveEmptyEntries))
            {
                var match = Directory.EnumerateFileSystemEntries(current)
                    .FirstOrDefault(e => string.Equals(Path.GetFileName(e), segment, StringComparison.OrdinalIgnoreCase));
                if (match is null) return path;
                current = match;
            }

            return current;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return path;
        }
    }
}

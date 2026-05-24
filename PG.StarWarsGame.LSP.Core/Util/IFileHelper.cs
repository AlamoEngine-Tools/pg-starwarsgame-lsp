// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions;

namespace PG.StarWarsGame.LSP.Core.Util;

public interface IFileHelper
{
    IFileSystem FileSystem { get; }

    // Converts a filesystem path to a canonical file:/// URI (lowercase, forward slashes).
    string PathToFileUri(string path);

    // Converts any string (raw path or URI in any scheme/case) to a canonical file:/// URI.
    string NormalizeUri(string pathOrUri);

    // Returns true when both strings refer to the same file after normalization.
    bool UrisEqual(string a, string b);

    // Case-insensitive game-file search across workspace roots.
    string? FindInWorkspace(IList<string> roots, string normalizedRelPath);

    public string NormalizeGamePath(string raw);

    // Converts a file:/// URI back to a local filesystem path (Windows: "file:///d:/foo.xml" → "d:\foo.xml").
    // Returns null for non-file URIs or empty input.
    string? FileUriToPath(string fileUri);
}
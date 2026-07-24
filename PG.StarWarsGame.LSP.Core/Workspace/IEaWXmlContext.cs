// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Workspace;

public interface IEaWXmlContext
{
    bool HasDirectories { get; }
    bool IsEaWXmlFile(string fileUri);
    bool IsLeafFile(string fileUri);

    /// <summary>
    ///     The file's path relative to the xml directory that contains it ('/'-separated, casing
    ///     preserved), or null when the file is under no known xml directory. Used to key
    ///     workspace-file symbols so they match references written xml-dir-relative.
    /// </summary>
    string? TryGetXmlRelativePath(string fileUri);

    void AddDirectory(string absolutePath);

    void SetDirectories(IEnumerable<string> absolutePaths);
    void SetLeafDirectories(IEnumerable<string> absolutePaths);
}
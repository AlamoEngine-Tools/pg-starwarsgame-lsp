// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Workspace;

public interface IEaWXmlContext
{
    bool HasDirectories { get; }
    bool IsEaWXmlFile(string fileUri);

    void AddDirectory(string absolutePath);

    void SetDirectories(IEnumerable<string> absolutePaths);
}
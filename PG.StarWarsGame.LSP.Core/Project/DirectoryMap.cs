// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Project;

public sealed record DirectoryMap(
    IReadOnlyList<string> Xml,
    IReadOnlyList<string> Scripts,
    IReadOnlyList<string> Art,
    IReadOnlyList<string> Audio,
    IReadOnlyList<string> Ai)
{
    public DirectoryMap()
        : this([], [], [], [], [])
    {
    }
}
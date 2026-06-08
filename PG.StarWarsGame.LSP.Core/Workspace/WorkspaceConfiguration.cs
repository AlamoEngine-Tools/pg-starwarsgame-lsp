// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Workspace;

public sealed record WorkspaceConfiguration(
    IReadOnlyList<string> XmlDirectories,
    IReadOnlyList<string> ScriptRoots,
    IReadOnlyList<string> TextRoots,
    IReadOnlyList<string> AssetRoots,
    string? TextResourceType)
{
    public static readonly WorkspaceConfiguration Empty = new([], [], [], [], null);
}
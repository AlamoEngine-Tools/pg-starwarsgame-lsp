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

    /// <summary>
    ///     Directories holding story-dialog <c>.txt</c> scripts. Only files under these
    ///     directories get the story-dialog language service — the vanilla <c>Dialog_</c> filename
    ///     prefix is a naming habit, not an engine requirement, so scoping is registry-based.
    /// </summary>
    public IReadOnlyList<string> StoryDialog { get; init; } = [];
}
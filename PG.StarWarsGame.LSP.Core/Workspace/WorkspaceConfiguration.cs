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

    /// <summary>
    ///     Normalised absolute path to the root project's <c>.pgproj</c> - the stable identity of this
    ///     configuration. In a multi-root workspace one configuration exists per root project, so
    ///     something other than object identity is needed to correlate a configuration with its
    ///     project across reloads. Null only for the heuristic/test setups that build a configuration
    ///     without a project file.
    /// </summary>
    public string? ProjectPath { get; init; }

    /// <summary>
    ///     The resolved project hierarchy in precedence order (dependencies low, root project
    ///     highest). Drives layer-rank resolution and per-layer localisation loading. The flattened
    ///     directory lists above are retained as the union across all layers.
    /// </summary>
    public IReadOnlyList<ProjectLayer> Layers { get; init; } = [];

    /// <summary>
    ///     Union of every layer's resolved story-dialog directories (dependencies first, root
    ///     project last) - the registry scope for the story-dialog language service.
    /// </summary>
    public IReadOnlyList<string> StoryDialogRoots { get; init; } = [];
}
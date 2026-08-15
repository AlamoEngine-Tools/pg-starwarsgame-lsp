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

    /// <summary>
    ///     The root project's icon configuration, or <see langword="null" /> when it declares none.
    /// </summary>
    /// <remarks>
    ///     Taken from the ROOT project only, not merged across layers, because a mega texture is not
    ///     mergeable: whichever .mtd wins replaces the set wholesale rather than layering on top of a
    ///     dependency's. Null means "use <see cref="Project.IconProjectSettings.Default" />" - the
    ///     engine convention - rather than "no icons".
    /// </remarks>
    public Project.IconProjectSettings? Icons { get; init; }
}
// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Project;

namespace PG.StarWarsGame.LSP.Core.Workspace;

/// <summary>
///     One project in a resolved workspace's dependency hierarchy, with its precedence
///     <see cref="Rank" /> (dependencies low, the root project highest) and its own resolved,
///     absolute directories per kind. A document is classified into the layer whose directories
///     contain it; the winner of a same-id collision is the symbol from the highest-ranked layer.
///     <see cref="TextRoots" /> and <see cref="TextResourceType" /> are per-layer so each project's
///     localisation is loaded with its own format.
/// </summary>
public sealed record ProjectLayer(
    int Rank,
    string Name,
    IReadOnlyList<string> XmlDirectories,
    IReadOnlyList<string> ScriptRoots,
    IReadOnlyList<string> TextRoots,
    IReadOnlyList<string> AssetRoots,
    string? TextResourceType,
    // Normalised absolute path to the .pgproj file. Null when there is no pgproj (heuristic scan).
    string? ProjectPath = null)
{
    /// <summary>This layer's resolved story-dialog directories (registry scope for dialog .txt files).</summary>
    public IReadOnlyList<string> StoryDialogRoots { get; init; } = [];

    /// <summary>
    ///     This layer's credits-file classification, or null to use the naming convention. Per-layer
    ///     for the same reason as <see cref="TextResourceType" />: a dependency's naming must not be
    ///     reinterpreted by whatever the root project happens to declare.
    /// </summary>
    public LocalisationCreditsSettings? Credits { get; init; }
}
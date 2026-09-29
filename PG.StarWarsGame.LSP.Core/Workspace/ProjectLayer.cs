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
    ///     The directory holding this layer's <c>.pgproj</c>, forward-slashed, or null when the
    ///     layer has none.
    /// </summary>
    /// <remarks>
    ///     Here rather than at each call site: three of them had grown their own copy of this
    ///     two-line fold - the watch-directory handler, the icon catalog and the document keys - and
    ///     three copies of "where is this project" is three chances for one of them to disagree
    ///     about a trailing slash or a backslash.
    /// </remarks>
    public string? ProjectDirectory
    {
        get
        {
            if (ProjectPath is not { } path) return null;

            var normalized = path.Replace('\\', '/').TrimEnd('/');
            var slash = normalized.LastIndexOf('/');
            return slash > 0 ? normalized[..slash] : null;
        }
    }

    /// <summary>
    ///     This layer's credits-file classification, or null to use the naming convention. Per-layer
    ///     for the same reason as <see cref="TextResourceType" />: a dependency's naming must not be
    ///     reinterpreted by whatever the root project happens to declare.
    /// </summary>
    public LocalisationCreditsSettings? Credits { get; init; }

    /// <summary>
    ///     Where this layer keeps its icons, or null when it declares no <c>icons</c> node.
    /// </summary>
    /// <remarks>
    ///     Per-layer, and for the same reason as the two above: the paths are relative to THIS
    ///     project's directory, so a dependency resolved against the leaf's root found nothing. Only
    ///     the root project's settings were carried before, which left a referenced project's mega
    ///     texture and loose icon sources invisible - see <see cref="Project.IconLayer" /> for how
    ///     the two kinds layer.
    /// </remarks>
    public IconProjectSettings? Icons { get; init; }
}
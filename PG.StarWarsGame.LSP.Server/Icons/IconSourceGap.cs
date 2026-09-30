// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions;
using PG.StarWarsGame.LSP.Core.Project;
using PG.StarWarsGame.LSP.Core.Workspace;

namespace PG.StarWarsGame.LSP.Server.Icons;

/// <summary>
///     Which projects in a workspace can supply no icons at all.
/// </summary>
/// <remarks>
///     <para>
///         A project reaches the preview's icons one of two ways: the mega texture at the
///         conventional path, which needs no configuration, or loose source folders it names in an
///         <c>icons</c> node. A project with neither contributes nothing.
///     </para>
///     <para>
///         That used to happen in silence, and the result looked like a rendering bug: a mod with
///         1655 loose icons on disk and no <c>icons</c> node had every one of them fall through to
///         the baked base game, so its cards drew base-game artwork at base-game proportions on a
///         card the mod had widened. There is deliberately no conventional default for the loose
///         folder - the paths are the author's to declare - so the answer is to SAY the setting is
///         missing rather than to guess at it.
///     </para>
/// </remarks>
public static class IconSourceGap
{
    /// <summary>
    ///     The names of the projects that supply no icons, in the workspace's own layer order.
    /// </summary>
    /// <remarks>
    ///     A layer with no <c>.pgproj</c> is left out: that is a heuristic scan of a bare folder,
    ///     and there is no project file to add an <c>icons</c> node to.
    /// </remarks>
    public static IReadOnlyList<string> ProjectsWithoutIconSources(
        WorkspaceConfiguration? workspace, IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);

        if (workspace is null) return [];

        return workspace.Layers
            .Where(layer => layer.ProjectDirectory is not null)
            .Where(layer => !Supplies(layer, fs))
            .Select(layer => layer.Name)
            .ToList();
    }

    private static bool Supplies(ProjectLayer layer, IFileSystem fs)
    {
        // Declaring the node is enough. Whether the folders it names hold anything yet is the
        // author's business, and an empty folder is a normal state to be in mid-edit.
        if (layer.Icons is { SourceRoots.Count: > 0 }) return true;

        var settings = layer.Icons ?? IconProjectSettings.Default;
        var root = layer.ProjectDirectory!;

        // Both halves or neither: the atlas is read as a pair, so half of one supplies nothing.
        return fs.File.Exists(fs.Path.Combine(root, settings.MtdPath))
               && fs.File.Exists(fs.Path.Combine(root, settings.TexturePath));
    }
}

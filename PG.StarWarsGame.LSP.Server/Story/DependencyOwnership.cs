// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Persistence;
using PG.StarWarsGame.LSP.Core.Workspace;

namespace PG.StarWarsGame.LSP.Server.Story;

/// <summary>
///     Whether a document may be edited from here, or belongs to a project this one only references.
/// </summary>
/// <remarks>
///     A parent project is a library: inspect it, run it, do not edit it - the contract an IDE gives
///     a packaged source. Before this, edit mode opened on a dependency's graph, the work was done,
///     and the SAVE failed because the change could not be staged against a document the leaf does
///     not own; the failure arrived after the effort.
///     <para>
///         This is the BACKSTOP, not the mechanism. The client hides the editing affordances, which
///         is what makes the rule visible before anything is typed; this refuses whatever reaches
///         the command boundary by another route, and names the owning project so the refusal says
///         where the edit does belong.
///     </para>
/// </remarks>
public static class DependencyOwnership
{
    /// <summary>
    ///     Null when <paramref name="documentUri" /> may be edited; otherwise why it may not.
    /// </summary>
    public static string? Rejection(WorkspaceConfiguration? workspace, string? documentUri)
    {
        var owner = ReadOnlyOwner(workspace, documentUri);
        if (owner is null) return null;

        return $"'{owner}' is a referenced project and is read-only here. "
               + $"Edit this thread in '{owner}' itself.";
    }

    /// <summary>
    ///     The name of the referenced project that owns <paramref name="documentUri" />, or null when
    ///     this workspace may edit it.
    /// </summary>
    /// <remarks>
    ///     The name is what the graph carries, not a bare flag: a disabled control has to say why it
    ///     is disabled, and with several layers in play "which project owns this thread" is the
    ///     author's actual question.
    /// </remarks>
    public static string? ReadOnlyOwner(WorkspaceConfiguration? workspace, string? documentUri)
    {
        if (workspace is null || string.IsNullOrWhiteSpace(documentUri)) return null;

        var layers = workspace.Layers;
        if (layers.Count == 0) return null;

        var owner = OwningLayer(layers, documentUri);
        // Outside every project: not owned by a dependency, so not this guard's to refuse. Whatever
        // else is wrong with such a path is reported by the check that knows what it was for.
        if (owner is null) return null;

        var root = layers.OrderByDescending(l => l.Rank).First();
        return ReferenceEquals(owner, root) ? null : owner.Name;
    }

    /// <summary>
    ///     The layer whose project directory contains the document, innermost first - one project's
    ///     directory may contain another's, and the most specific is the one that owns the file.
    /// </summary>
    private static ProjectLayer? OwningLayer(IReadOnlyList<ProjectLayer> layers, string documentUri)
    {
        return layers
            .Where(l => l.ProjectDirectory is not null)
            .OrderByDescending(l => l.ProjectDirectory!.Length)
            .FirstOrDefault(l => DocumentKey.Relative(l.ProjectDirectory!, documentUri) is not null);
    }
}
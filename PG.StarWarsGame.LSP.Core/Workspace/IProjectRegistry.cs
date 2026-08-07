// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Workspace;

/// <summary>
///     The set of root projects open in this session, and the file-URI routing over them. There is
///     always at least one workspace (an empty default one before any <c>.pgproj</c> resolves), so
///     <see cref="Primary" /> is never null and the pre-startup window behaves exactly as it does
///     with a single project.
/// </summary>
public interface IProjectRegistry
{
    /// <summary>Every open project, in root order.</summary>
    IReadOnlyList<ProjectWorkspace> All { get; }

    /// <summary>
    ///     Raised after the project set is replaced, so services that aggregate per-project state can
    ///     re-attach to projects that appeared after they were constructed.
    /// </summary>
    event Action<IReadOnlyList<ProjectWorkspace>>? ProjectsChanged;

    /// <summary>
    ///     The fallback project for work that is not attributable to a file - and for files outside
    ///     every project. The first resolved project.
    /// </summary>
    ProjectWorkspace Primary { get; }

    /// <summary>
    ///     Every project that owns the given file, most specific first. Empty when the file is
    ///     outside all of them. More than one owner is normal: two root projects that reference a
    ///     common dependency both own that dependency's files.
    /// </summary>
    IReadOnlyList<ProjectWorkspace> Resolve(string fileUri);

    /// <summary>
    ///     The single project that should answer for the given file - its most specific owner, or
    ///     <see cref="Primary" /> when it has none.
    /// </summary>
    ProjectWorkspace ResolvePrimary(string fileUri);

    /// <summary>
    ///     The project this configuration belongs to, matched on
    ///     <see cref="WorkspaceConfiguration.ProjectPath" />. Falls back to <see cref="Primary" /> for
    ///     a configuration with no project path (the heuristic and test setups), which is what makes
    ///     a single-project session behave identically to one without any routing.
    /// </summary>
    ProjectWorkspace ForConfiguration(WorkspaceConfiguration configuration);

    /// <summary>
    ///     Replaces the open project set. Workspaces whose project is still present are reused, so a
    ///     reload triggered by one project does not discard what the others have indexed. Passing an
    ///     empty list leaves a single empty default workspace behind.
    /// </summary>
    void SetProjects(IReadOnlyList<WorkspaceConfiguration> configurations);
}

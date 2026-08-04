// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Caching;

namespace PG.StarWarsGame.LSP.Core.Workspace;

/// <summary>
///     A standalone <see cref="IProjectContext" /> over one configuration, for the setups that have a
///     configuration but no <see cref="ProjectWorkspace" /> around it. <see cref="ProjectWorkspace" />
///     implements the interface directly.
/// </summary>
public sealed class ProjectContext : IProjectContext
{
    public ProjectContext(WorkspaceConfiguration? configuration = null)
    {
        Configuration = configuration ?? WorkspaceConfiguration.Empty;
    }

    public WorkspaceConfiguration Configuration { get; }

    public string? AetswgDirectory => ResolveAetswgDirectory(Configuration);

    /// <summary>
    ///     The sidecar directory for a configuration: taken from the highest-ranked layer, which is
    ///     the root project itself, so state persists next to its own <c>.pgproj</c>.
    /// </summary>
    public static string? ResolveAetswgDirectory(WorkspaceConfiguration configuration)
    {
        var rootLayer = configuration.Layers.Count > 0
            ? configuration.Layers.OrderByDescending(l => l.Rank).First()
            : null;
        var projectPath = rootLayer?.ProjectPath ?? configuration.ProjectPath;
        return projectPath is null ? null : ProjectIndexLocator.GetAetswgDirectory(projectPath);
    }
}

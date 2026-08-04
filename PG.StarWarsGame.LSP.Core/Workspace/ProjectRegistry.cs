// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;

namespace PG.StarWarsGame.LSP.Core.Workspace;

/// <summary>
///     Default <see cref="IProjectRegistry" />. Routes by longest-URI-prefix match over each
///     project's resolved directories, mirroring <see cref="ProjectLayerMap" /> and
///     <see cref="EaWXmlContext" /> - the same matching that already decides layer precedence within
///     a project now also decides which project a file belongs to.
/// </summary>
public sealed class ProjectRegistry : IProjectRegistry
{
    private readonly IFileHelper _fileHelper;
    private readonly ILoggerFactory? _loggerFactory;
    private readonly IEnumerable<IGameDocumentParser> _parsers;

    // Snapshot replaced atomically by SetProjects; readers take a single volatile read, matching the
    // lock-free reader pattern used by ProjectLayerMap and EaWXmlContext.
    private ImmutableArray<ProjectWorkspace> _workspaces;

    /// <summary>
    ///     Builds a project's own service provider. Taken as a constructor argument rather than a
    ///     settable property because the default workspace is created here: a factory assigned
    ///     afterwards would leave that first project - the one <see cref="Primary" /> returns until a
    ///     .pgproj resolves - without a provider. Null in the minimal setups that only need the index
    ///     and scoping state.
    /// </summary>
    private readonly Func<ProjectWorkspace, IServiceProvider>? _serviceProviderFactory;

    public ProjectRegistry(
        IFileHelper fileHelper,
        IEnumerable<IGameDocumentParser>? parsers = null,
        ILoggerFactory? loggerFactory = null,
        Func<ProjectWorkspace, IServiceProvider>? serviceProviderFactory = null)
    {
        _fileHelper = fileHelper;
        _parsers = parsers ?? [];
        _loggerFactory = loggerFactory;
        _serviceProviderFactory = serviceProviderFactory;
        _workspaces = [Create(WorkspaceConfiguration.Empty)];
    }

    public IReadOnlyList<ProjectWorkspace> All => _workspaces;

    /// <summary>
    ///     Raised after the project set is replaced. Lets services that aggregate per-project state
    ///     (notably <see cref="Symbols.RoutedGameIndexService" />, which forwards each project's
    ///     events) re-attach to projects that appeared after they were constructed.
    /// </summary>
    public event Action<IReadOnlyList<ProjectWorkspace>>? ProjectsChanged;

    public ProjectWorkspace Primary => _workspaces[0];

    public IReadOnlyList<ProjectWorkspace> Resolve(string fileUri)
    {
        var workspaces = _workspaces;
        if (workspaces.Length == 1 && workspaces[0].RoutingPrefixes.Count == 0)
            // Single project with nothing resolved yet (the pre-startup window, or a workspace with
            // no .pgproj): there is nothing to attribute a file to.
            return [];

        var normalized = _fileHelper.NormalizeUri(fileUri);

        // Score each owner by its longest matching prefix so the most specific project wins when
        // one project's folder contains another's (a dependency vendored inside its dependent).
        List<(ProjectWorkspace Workspace, int Length)>? owners = null;
        foreach (var workspace in workspaces)
        {
            var best = 0;
            foreach (var prefix in workspace.RoutingPrefixes)
                if (normalized.StartsWith(prefix, StringComparison.Ordinal))
                {
                    // RoutingPrefixes is ordered longest-first, so the first hit is this project's best.
                    best = prefix.Length;
                    break;
                }

            if (best == 0) continue;
            owners ??= [];
            owners.Add((workspace, best));
        }

        if (owners is null) return [];

        owners.Sort((a, b) => b.Length.CompareTo(a.Length));
        return owners.Select(o => o.Workspace).ToList();
    }

    public ProjectWorkspace ResolvePrimary(string fileUri)
    {
        var owners = Resolve(fileUri);
        return owners.Count > 0 ? owners[0] : Primary;
    }

    public ProjectWorkspace ForConfiguration(WorkspaceConfiguration configuration)
    {
        if (configuration.ProjectPath is not { } id) return Primary;

        foreach (var workspace in _workspaces)
            if (string.Equals(workspace.Id, id, StringComparison.OrdinalIgnoreCase))
                return workspace;

        return Primary;
    }

    public void SetProjects(IReadOnlyList<WorkspaceConfiguration> configurations)
    {
        if (configurations.Count == 0)
        {
            // Never leave the registry without a workspace - Primary must stay non-null and the
            // no-project session must keep behaving like an empty single-project one.
            _workspaces = [Create(WorkspaceConfiguration.Empty)];
            ProjectsChanged?.Invoke(_workspaces);
            return;
        }

        var existing = _workspaces
            .Where(w => w.Id is not null)
            .ToDictionary(w => w.Id!, StringComparer.OrdinalIgnoreCase);

        var next = ImmutableArray.CreateBuilder<ProjectWorkspace>(configurations.Count);
        foreach (var configuration in configurations)
            if (configuration.ProjectPath is { } id && existing.TryGetValue(id, out var reused))
            {
                // Same project as before: keep the instance so its indexed state survives a reload
                // that was really about a sibling project.
                reused.Apply(configuration);
                next.Add(reused);
            }
            else
            {
                next.Add(Create(configuration));
            }

        _workspaces = next.ToImmutable();
        ProjectsChanged?.Invoke(_workspaces);
    }

    private ProjectWorkspace Create(WorkspaceConfiguration configuration)
    {
        var workspace = new ProjectWorkspace(_fileHelper, configuration, _parsers, _loggerFactory);

        // Every project gets its own provider. A service the factory does not deliberately share is
        // a fresh instance here, so cross-project bleed takes an explicit act rather than an omission.
        if (_serviceProviderFactory is { } factory)
            workspace.AttachServices(factory(workspace));

        return workspace;
    }
}

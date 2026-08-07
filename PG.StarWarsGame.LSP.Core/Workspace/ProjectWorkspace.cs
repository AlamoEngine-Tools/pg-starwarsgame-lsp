// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using PG.StarWarsGame.LSP.Core.Caching;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;

namespace PG.StarWarsGame.LSP.Core.Workspace;

/// <summary>
///     One root project (a <c>.pgproj</c> and its resolved dependency closure) and the state scoped
///     to it. A multi-root VS Code workspace produces one of these per root project; they are
///     deliberately isolated, so two unrelated mods that both define <c>REBEL_TROOPER</c> never
///     cross-resolve. Projects are linked by <c>projectReference</c>, not by co-residency in a window.
///     <para>
///         Only mod-specific, mutable state lives here. The expensive, immutable, base-game state -
///         schema, baseline, asset and model-bone catalogs - stays process-wide and is shared by
///         every workspace, which is the entire reason this is one server rather than one per folder.
///     </para>
/// </summary>
public sealed class ProjectWorkspace : IProjectContext
{
    private readonly IFileHelper _fileHelper;

    public ProjectWorkspace(
        IFileHelper fileHelper,
        WorkspaceConfiguration configuration,
        IEnumerable<IGameDocumentParser>? parsers = null,
        ILoggerFactory? loggerFactory = null)
    {
        _fileHelper = fileHelper;
        XmlContext = new EaWXmlContext(fileHelper);
        LayerMap = new ProjectLayerMap(fileHelper);
        FileTypes = new FileTypeRegistry();

        // The layer map handed to the index is this project's own, so ranks stay project-local -
        // rank 0 in one project means nothing in another.
        Index = new GameIndexService(
            fileHelper,
            parsers ?? [],
            loggerFactory?.CreateLogger<GameIndexService>()
            ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<GameIndexService>.Instance,
            LayerMap);

        Configuration = WorkspaceConfiguration.Empty;
        Apply(configuration);
    }

    /// <summary>
    ///     Stable identity across reloads: the normalised <c>.pgproj</c> path, or <see langword="null" />
    ///     for the default workspace that exists before (or without) any project file.
    /// </summary>
    public string? Id => Configuration.ProjectPath;

    public WorkspaceConfiguration Configuration { get; private set; }

    /// <summary>
    ///     This project's sidecar directory. Taken from the highest-ranked layer - the root project -
    ///     so a project's settings, suppressions and story layouts persist next to its own
    ///     <c>.pgproj</c> and never leak into a sibling project's.
    /// </summary>
    public string? AetswgDirectory => ProjectContext.ResolveAetswgDirectory(Configuration);

    public EaWXmlContext XmlContext { get; }
    public ProjectLayerMap LayerMap { get; }
    public FileTypeRegistry FileTypes { get; }

    /// <summary>
    ///     This project's symbol index. Its <see cref="GameIndex.Baseline" /> is the one shared
    ///     base-game instance - <see cref="GameIndex" /> composes the baseline as a field, so N
    ///     projects pointing at one baseline costs a reference each, not N copies.
    /// </summary>
    public GameIndexService Index { get; }

    /// <summary>
    ///     This project's own service provider, holding every service scoped to it. Separation is by
    ///     construction: the provider is built from an explicit list of shared services, so a service
    ///     that was not deliberately shared simply cannot be resolved from here - it resolves to this
    ///     project's own instance. Null in the minimal setups that never build one.
    ///     <para>
    ///         Resolve through <see cref="IProjectRegistry" /> routing rather than reaching for
    ///         <see cref="IProjectRegistry.Primary" />, or the separation is defeated at the call site.
    ///     </para>
    /// </summary>
    public IServiceProvider? Services { get; private set; }

    /// <summary>
    ///     Attaches the per-project provider. Called once by the registry immediately after
    ///     construction; the factory needs the workspace itself, so it cannot run in the constructor.
    /// </summary>
    public void AttachServices(IServiceProvider services)
    {
        Services = services;
    }

    /// <summary>
    ///     A service scoped to this project. Throws when no provider is attached, which is a wiring
    ///     bug rather than a runtime condition - silently falling back to a shared instance is what
    ///     this whole design exists to prevent.
    /// </summary>
    public T Service<T>() where T : notnull
    {
        if (Services is null)
            throw new InvalidOperationException(
                $"No project service provider attached; cannot resolve {typeof(T).Name} for project '{Id ?? "<none>"}'.");
        return (T)Services.GetService(typeof(T))!;
    }

    /// <summary>
    ///     Every directory this project owns, as normalised URI prefixes with a trailing slash,
    ///     longest first. Used by <see cref="ProjectRegistry" /> to route a file to its project.
    /// </summary>
    internal IReadOnlyList<string> RoutingPrefixes { get; private set; } = [];

    /// <summary>
    ///     Re-points this workspace at a new configuration for the same project, keeping the
    ///     workspace instance (and everything indexed into it) alive across a reload.
    /// </summary>
    public void Apply(WorkspaceConfiguration configuration)
    {
        Configuration = configuration;
        LayerMap.SetLayers(configuration.Layers);
        RoutingPrefixes = BuildRoutingPrefixes(configuration);

        // Seeded from the configuration here so the project recognises its own files from the moment
        // it exists, rather than only once the metafile pre-scan has run. The pre-scan re-applies the
        // same directories (it is the only populate path when no registry is wired) and then adds the
        // ones it discovers from metafiles on top.
        XmlContext.SetDirectories(configuration.XmlDirectories);
        XmlContext.SetLeafDirectories(LeafLayerDirectories(configuration));
    }

    // The leaf layer is the highest-ranked one - the root project itself. Its xml directories are
    // what "is this file mine rather than a dependency's" means.
    private static IReadOnlyList<string> LeafLayerDirectories(WorkspaceConfiguration configuration)
    {
        if (configuration.Layers.Count == 0) return [];
        return configuration.Layers.OrderByDescending(l => l.Rank).First().XmlDirectories;
    }

    private List<string> BuildRoutingPrefixes(WorkspaceConfiguration configuration)
    {
        var prefixes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var dir in configuration.XmlDirectories
                     .Concat(configuration.ScriptRoots)
                     .Concat(configuration.TextRoots)
                     .Concat(configuration.AssetRoots)
                     .Concat(configuration.StoryDialogRoots))
            prefixes.Add(ToPrefix(dir));

        // The project's own folder is a prefix too, so files that belong to the mod but sit outside
        // any declared directory (a readme, an excluded ai/ file) still route to their project
        // instead of falling back to the primary one. It is necessarily the shortest of this
        // project's prefixes, so longest-prefix ordering already makes it the last resort.
        if (configuration.ProjectPath is { } projectPath)
        {
            var idx = projectPath.LastIndexOf('/');
            if (idx > 0)
                prefixes.Add(ToPrefix(projectPath[..idx]));
        }

        return prefixes.OrderByDescending(p => p.Length).ToList();
    }

    private string ToPrefix(string directory)
    {
        var uri = directory.StartsWith("file://", StringComparison.OrdinalIgnoreCase)
            ? _fileHelper.NormalizeUri(directory)
            : _fileHelper.PathToFileUri(directory);
        return uri.TrimEnd('/') + '/';
    }
}

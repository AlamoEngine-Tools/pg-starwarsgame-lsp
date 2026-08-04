// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Server.Project;

namespace PG.StarWarsGame.LSP.Server.Startup;

/// <summary>
///     Resolves the session's <see cref="WorkspaceConfiguration" /> from the <c>.pgproj</c> found
///     under the given roots, following project references. Returns <see langword="null" /> when no
///     project file exists or when it fails to load - there is no directory heuristic, so without a
///     valid project there is nothing to index.
/// </summary>
public sealed class ProjectConfigurationResolver : IProjectConfigurationResolver
{
    private readonly IModProjectDetector _detector;
    private readonly ModProjectLoader _loader;
    private readonly ILogger<ProjectConfigurationResolver> _logger;
    private readonly IUserNotifier _notifier;
    private readonly ModProjectResolver _resolver;

    public ProjectConfigurationResolver(
        IModProjectDetector detector,
        ModProjectLoader loader,
        ModProjectResolver resolver,
        IUserNotifier notifier,
        ILogger<ProjectConfigurationResolver> logger)
    {
        _detector = detector;
        _loader = loader;
        _resolver = resolver;
        _notifier = notifier;
        _logger = logger;
    }

    public IReadOnlyList<WorkspaceConfiguration> ResolveAll(IReadOnlyList<string> roots)
    {
        _logger.LogDebug("Resolving project configurations under [{Roots}]", string.Join(", ", roots));

        var configs = new List<WorkspaceConfiguration>();

        // Roots overlap routinely (the configured game-data directory is usually a subdirectory of a
        // workspace folder), so the same project can be discovered more than once.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Detection and loading are attempted per root so one broken project cannot take the healthy
        // ones down with it - which is the whole point in a multi-root workspace. Both stages need to
        // surface as a user-facing notification rather than failing silently or crashing startup, so
        // both are covered by the same catch: the detector can throw (multiple .pgproj under one
        // root) just like the loader can.
        foreach (var root in roots)
            try
            {
                foreach (var pgprojPath in _detector.FindAll([root]))
                {
                    var file = _loader.Load(pgprojPath);
                    var config = _resolver.Resolve(pgprojPath, file);
                    if (config.ProjectPath is not null && !seen.Add(config.ProjectPath))
                    {
                        _logger.LogDebug("Project '{Path}' already resolved from an earlier root; skipping.",
                            config.ProjectPath);
                        continue;
                    }

                    configs.Add(config);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to resolve mod project configuration under '{Root}'; it will not be indexed.",
                    root);

                // Surface a clear, actionable message to the user as an editor notification rather
                // than failing silently. ModProjectLoadException already carries a user-facing message.
                _notifier.ShowError(ex is ModProjectLoadException
                    ? ex.Message
                    : $"Could not load mod project configuration: {ex.Message}");
            }

        if (configs.Count == 0)
            _logger.LogWarning("No .pgproj found under [{Roots}]; nothing to index.", string.Join(", ", roots));

        return configs;
    }
}
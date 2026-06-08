// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Server.Project;

namespace PG.StarWarsGame.LSP.Server.Startup;

/// <summary>
///     Resolves the session's <see cref="WorkspaceConfiguration" /> from the <c>.pgproj</c> found
///     under the given roots, following project references. Returns <see langword="null" /> when no
///     project file exists or when it fails to load — there is no directory heuristic, so without a
///     valid project there is nothing to index.
/// </summary>
public sealed class ProjectConfigurationResolver : IProjectConfigurationResolver
{
    private readonly IModProjectDetector _detector;
    private readonly ModProjectLoader _loader;
    private readonly ILogger<ProjectConfigurationResolver> _logger;
    private readonly ModProjectResolver _resolver;

    public ProjectConfigurationResolver(
        IModProjectDetector detector,
        ModProjectLoader loader,
        ModProjectResolver resolver,
        ILogger<ProjectConfigurationResolver> logger)
    {
        _detector = detector;
        _loader = loader;
        _resolver = resolver;
        _logger = logger;
    }

    public WorkspaceConfiguration? Resolve(IReadOnlyList<string> roots)
    {
        _logger.LogDebug("Resolving project configuration under [{Roots}]", string.Join(", ", roots));
        if (_detector.TryFind(roots, out var pgprojPath) && pgprojPath is not null)
            try
            {
                var file = _loader.Load(pgprojPath);
                return _resolver.Resolve(pgprojPath, file);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to load mod project '{Path}'; no directories will be indexed.", pgprojPath);
                return null;
            }

        _logger.LogWarning("No .pgproj found under [{Roots}]; nothing to index.", string.Join(", ", roots));
        return null;
    }
}
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

    public WorkspaceConfiguration? Resolve(IReadOnlyList<string> roots)
    {
        _logger.LogDebug("Resolving project configuration under [{Roots}]", string.Join(", ", roots));

        // Detection (e.g. multiple .pgproj files under one root) and loading both need to surface
        // as a user-facing notification rather than failing silently or crashing startup, so both
        // are covered by the same catch - ModProjectDetector.TryFind can throw just like the loader.
        try
        {
            if (_detector.TryFind(roots, out var pgprojPath) && pgprojPath is not null)
            {
                var file = _loader.Load(pgprojPath);
                return _resolver.Resolve(pgprojPath, file);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to resolve mod project configuration under [{Roots}]; no directories will be indexed.",
                string.Join(", ", roots));

            // Surface a clear, actionable message to the user as an editor notification rather than
            // failing silently. ModProjectLoadException already carries a user-facing message.
            var message = ex is ModProjectLoadException
                ? ex.Message
                : $"Could not load mod project configuration: {ex.Message}";
            _notifier.ShowError(message);
            return null;
        }

        // Not a mod project, so there is nothing for the server to be right about: no directories to
        // index, no baseline to compare against, and every answer it could give would be an empty
        // one. That used to be a log line nobody reads, which left the editor looking like it worked
        // and simply knew nothing.
        _logger.LogWarning("No .pgproj found under [{Roots}]; nothing to index.", string.Join(", ", roots));
        _notifier.ShowError(
            $"No .pgproj file was found under [{string.Join(", ", roots)}]. This folder is not a mod "
            + "project, so nothing has been indexed and no XML or Lua support is available here. Open "
            + "the folder that contains your .pgproj, or create one.");
        return null;
    }
}
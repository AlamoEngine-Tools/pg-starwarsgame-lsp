// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Server.Project;
using PG.StarWarsGame.LSP.Server.Status;

namespace PG.StarWarsGame.LSP.Server.Startup;

/// <summary>
///     Resolves the session's <see cref="WorkspaceConfiguration" /> from the <c>.pgproj</c> found
///     under the given roots, following project references. Returns <see langword="null" /> when no
///     project file exists or when it fails to load - there is no directory heuristic, so without a
///     valid project there is nothing to index.
/// </summary>
public sealed class ProjectConfigurationResolver : IProjectConfigurationResolver
{
    private readonly ILspConfigurationProvider? _config;
    private readonly IModProjectDetector _detector;
    private readonly ModProjectLoader _loader;
    private readonly ILogger<ProjectConfigurationResolver> _logger;
    private readonly IUserNotifier _notifier;
    private readonly ModProjectResolver _resolver;
    private readonly ServerStatusRecorder? _status;

    public ProjectConfigurationResolver(
        IModProjectDetector detector,
        ModProjectLoader loader,
        ModProjectResolver resolver,
        IUserNotifier notifier,
        ILogger<ProjectConfigurationResolver> logger,
        ServerStatusRecorder? status = null,
        // Optional so the minimal test setups can omit it; production wires it. Carries the
        // project file the client named, if it named one.
        ILspConfigurationProvider? config = null)
    {
        _status = status;
        _config = config;
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
        // The client named the project (one server per open project): load exactly that, and do
        // not treat its siblings as "other project files" - the client knows about them already.
        var named = _config?.Current.ProjectPath;
        if (!string.IsNullOrWhiteSpace(named))
        {
            if (!_resolver.FileHelper.FileSystem.File.Exists(named))
            {
                _logger.LogError("The project file named by the client does not exist: '{Path}'", named);
                _status?.RecordProject(false, ProjectProblem.Missing);
                _notifier.ShowError(
                    $"The project file '{named}' does not exist. Nothing has been indexed; reopen the "
                    + "folder once the file is back, or remove it from the workspace.");
                return null;
            }

            roots = [.. roots, _resolver.FileHelper.FileSystem.Path.GetDirectoryName(named) ?? named];
            return LoadAndResolve(named, roots);
        }

        try
        {
            if (_detector.TryFind(roots, out var pgprojPath, out var others) && pgprojPath is not null)
            {
                if (others.Count > 0)
                {
                    // Several project files under one root is an ordinary layout now - a mod and
                    // the library it extends - but only one is served by this server. Say which,
                    // and how many are not, rather than refusing the whole folder as before.
                    _status?.RecordOtherProjectFiles(others.Count);
                    _notifier.ShowWarning(
                        $"Loaded '{_resolver.FileHelper.FileSystem.Path.GetFileName(pgprojPath)}'. "
                        + $"{others.Count} other project file(s) under this workspace are not served by "
                        + "this server; open their folders to work on them.");
                }

                return LoadAndResolve(pgprojPath, roots);
            }
        }
        catch (Exception ex)
        {
            // Every failure here is a project that was found: TryFind returns false rather than
            // throwing when there is none, and throws only for more than one.
            _status?.RecordProject(true,
                ex is ModProjectLoadException load ? load.Problem : ProjectProblem.Invalid);

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

        return NoProject(roots);
    }

    private WorkspaceConfiguration? LoadAndResolve(string pgprojPath, IReadOnlyList<string> roots)
    {
        try
        {
            var file = _loader.Load(pgprojPath);
            var resolved = _resolver.Resolve(pgprojPath, file);
            _status?.RecordProject(true, ProjectProblem.None);
            return resolved;
        }
        catch (Exception ex)
        {
            _status?.RecordProject(true,
                ex is ModProjectLoadException load ? load.Problem : ProjectProblem.Invalid);
            _logger.LogError(ex,
                "Failed to resolve mod project configuration under [{Roots}]; no directories will be indexed.",
                string.Join(", ", roots));
            _notifier.ShowError(ex is ModProjectLoadException
                ? ex.Message
                : $"Could not load mod project configuration: {ex.Message}");
            return null;
        }
    }

    private WorkspaceConfiguration? NoProject(IReadOnlyList<string> roots)
    {
        // Not a mod project, so there is nothing for the server to be right about: no directories to
        // index, no baseline to compare against, and every answer it could give would be an empty
        // one. That used to be a log line nobody reads, which left the editor looking like it worked
        // and simply knew nothing.
        _logger.LogWarning("No .pgproj found under [{Roots}]; nothing to index.", string.Join(", ", roots));
        _status?.RecordProject(false, ProjectProblem.Missing);
        _notifier.ShowError(
            $"No .pgproj file was found under [{string.Join(", ", roots)}]. This folder is not a mod "
            + "project, so nothing has been indexed and no XML or Lua support is available here. Open "
            + "the folder that contains your .pgproj, or create one.");
        return null;
    }
}
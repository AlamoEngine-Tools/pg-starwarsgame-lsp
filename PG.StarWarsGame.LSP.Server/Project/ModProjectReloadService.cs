// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using PG.StarWarsGame.LSP.Core.Workspace;

namespace PG.StarWarsGame.LSP.Server.Project;

public sealed class ModProjectReloadService : IModProjectReloadService
{
    private readonly IModProjectDetector _detector;
    private readonly ModProjectLoader _loader;
    private readonly ILogger<ModProjectReloadService> _logger;
    private readonly ModProjectResolver _resolver;
    private readonly WorkspaceScanner _scanner;

    private List<string>? _lastRoots;

    public ModProjectReloadService(
        IModProjectDetector detector,
        ModProjectLoader loader,
        ModProjectResolver resolver,
        WorkspaceScanner scanner,
        ILogger<ModProjectReloadService> logger)
    {
        _detector = detector;
        _loader = loader;
        _resolver = resolver;
        _scanner = scanner;
        _logger = logger;
    }

    public async Task LoadAsync(IEnumerable<string> workspaceRoots, CancellationToken ct)
    {
        var roots = workspaceRoots.ToList();
        _lastRoots = roots;

        var config = ResolveConfiguration(roots);
        await _scanner.ScanAsync(config, roots, ct);
    }

    public async Task ReloadAsync(CancellationToken ct)
    {
        var roots = _lastRoots;
        if (roots is null)
        {
            _logger.LogWarning("ReloadAsync called before LoadAsync; ignoring reload request.");
            return;
        }

        try
        {
            await LoadAsync(roots, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Mod project reload failed.");
        }
    }

    private WorkspaceConfiguration ResolveConfiguration(List<string> roots)
    {
        if (_detector.TryFind(roots, out var pgprojPath) && pgprojPath is not null)
        {
            try
            {
                var file = _loader.Load(pgprojPath);
                return _resolver.Resolve(pgprojPath, file);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to load mod project '{Path}'; falling back to workspace heuristic.", pgprojPath);
            }
        }
        else
        {
            _logger.LogWarning("No .pgproj found — using workspace heuristic.");
        }

        return new WorkspaceConfiguration([], roots, roots, roots, null);
    }
}

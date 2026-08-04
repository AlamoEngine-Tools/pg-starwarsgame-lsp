// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;

namespace PG.StarWarsGame.LSP.Server.Project;

/// <summary>
///     Applies a <c>workspace/didChangeWorkspaceFolders</c> notification: folds the added and removed
///     folders into the scan-root set and re-runs the project load, which re-resolves every
///     <c>.pgproj</c> and rebuilds the project set. Split out from the protocol handler so the
///     root-set arithmetic is testable without a language server.
/// </summary>
public sealed class WorkspaceFolderChangeService
{
    private readonly ILogger _logger;
    private readonly IModProjectReloadService _reloadService;

    public WorkspaceFolderChangeService(IModProjectReloadService reloadService, ILogger logger)
    {
        _reloadService = reloadService;
        _logger = logger;
    }

    public async Task ApplyAsync(
        IReadOnlyList<string> added,
        IReadOnlyList<string> removed,
        CancellationToken ct)
    {
        var roots = new List<string>(_reloadService.LastWorkspaceRoots ?? []);

        // Paths come from the client verbatim, and Windows clients are not consistent about drive
        // letter casing or separators, so membership is compared case-insensitively.
        var known = new HashSet<string>(roots, StringComparer.OrdinalIgnoreCase);
        var changed = false;

        foreach (var folder in removed)
        {
            var before = roots.Count;
            roots.RemoveAll(r => string.Equals(r, folder, StringComparison.OrdinalIgnoreCase));
            if (roots.Count == before) continue;
            known.Remove(folder);
            changed = true;
        }

        foreach (var folder in added)
        {
            if (string.IsNullOrWhiteSpace(folder) || !known.Add(folder)) continue;
            roots.Add(folder);
            changed = true;
        }

        if (!changed)
        {
            // Re-resolving every project is a full rescan; a notification that leaves the root set
            // identical (a folder re-added, or one removed that was never a scan root) is not worth it.
            _logger.LogDebug("Workspace folder change left the scan roots unchanged; skipping reload.");
            return;
        }

        _logger.LogInformation(
            "Workspace folders changed (+{Added} -{Removed}); reloading projects under [{Roots}].",
            added.Count, removed.Count, string.Join(", ", roots));

        await _reloadService.LoadAsync(roots, ct);
    }
}

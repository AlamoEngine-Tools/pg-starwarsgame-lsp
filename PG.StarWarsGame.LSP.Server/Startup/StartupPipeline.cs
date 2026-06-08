// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Server.Project;

namespace PG.StarWarsGame.LSP.Server.Startup;

/// <summary>
///     The single, linear startup sequence. Every stage is awaited in a fixed order — no
///     fire-and-forget, no readiness events, no re-entrance. Launched once from
///     <c>OnInitialized</c> on a background task; while it runs, the <see cref="IStartupGate" />
///     buffers inbound client notifications, which the final stage drains. The whole body is
///     guarded so the gate always opens, even if a stage fails — a degraded server still edits.
///     Workspace indexing (including the no-pgproj case) is delegated to
///     <see cref="IModProjectReloadService" />, the one index path shared with reload and pgproj-watch.
/// </summary>
public sealed class StartupPipeline
{
    private readonly IBaselineBootstrapper _baseline;
    private readonly IStartupGate _gate;
    private readonly ILogger<StartupPipeline> _logger;
    private readonly IStartupNotifier _notifier;
    private readonly IStartupProgress _progress;
    private readonly IModProjectReloadService _reloadService;
    private readonly ISchemaBootstrapper _schema;

    public StartupPipeline(
        ISchemaBootstrapper schema,
        IBaselineBootstrapper baseline,
        IModProjectReloadService reloadService,
        IStartupGate gate,
        IStartupProgress progress,
        IStartupNotifier notifier,
        ILogger<StartupPipeline> logger)
    {
        _schema = schema;
        _baseline = baseline;
        _reloadService = reloadService;
        _gate = gate;
        _progress = progress;
        _notifier = notifier;
        _logger = logger;
    }

    public async Task RunAsync(IReadOnlyList<string> scanRoots, CancellationToken ct)
    {
        try
        {
            _progress.Report("Loading schema and baseline", 5);
            await Task.WhenAll(
                _schema.LoadAsync(ct),
                _baseline.LoadAsync(ct));

            _progress.Report("Indexing workspace", 30);
            await _reloadService.LoadAsync(scanRoots, ct);
        }
        catch (Exception ex)
        {
            // Startup is fire-and-forget; never let a stage failure leave the gate closed.
            _logger.LogError(ex, "Startup pipeline failed; opening gate in degraded state.");
        }
        finally
        {
            _progress.Report("Finalising", 95);
            await _gate.OpenAsync();
            _progress.Complete();
            _notifier.NotifyScanComplete();
            _logger.LogInformation("Startup pipeline finished");
        }
    }
}
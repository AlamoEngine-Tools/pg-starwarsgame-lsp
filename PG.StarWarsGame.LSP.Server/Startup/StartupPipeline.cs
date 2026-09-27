// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Server.Project;

namespace PG.StarWarsGame.LSP.Server.Startup;

/// <summary>
///     The single, linear startup sequence. Every stage is awaited in a fixed order - no
///     fire-and-forget, no readiness events, no re-entrance. Launched once from
///     <c>OnInitialized</c> on a background task; while it runs, the <see cref="IStartupGate" />
///     buffers inbound client notifications, which the final stage drains. The whole body is
///     guarded so the gate always opens, even if a stage fails - a degraded server still edits.
///     Workspace indexing (including the no-pgproj case) is delegated to
///     <see cref="IModProjectReloadService" />, the one index path shared with reload and pgproj-watch.
///     Anything that asks the user a question comes AFTER the gate, never inside a stage: see the
///     project-file migration offer at the end of <see cref="RunAsync" />.
/// </summary>
public sealed class StartupPipeline
{
    private readonly IBaselineBootstrapper _baseline;
    private readonly IStartupGate _gate;
    private readonly ILogger<StartupPipeline> _logger;
    private readonly IPgprojMigrationOffer? _migrationOffer;
    private readonly IStartupNotifier _notifier;
    private readonly IStartupProgress _progress;
    private readonly IModProjectReloadService _reloadService;
    private readonly IReadOnlyList<IDiagnosticsRepublisher> _republishers;
    private readonly ISchemaBootstrapper _schema;

    // migrationOffer is optional so the minimal test setups can omit it; production always wires it.
    public StartupPipeline(
        ISchemaBootstrapper schema,
        IBaselineBootstrapper baseline,
        IModProjectReloadService reloadService,
        IStartupGate gate,
        IStartupProgress progress,
        IStartupNotifier notifier,
        ILogger<StartupPipeline> logger,
        IPgprojMigrationOffer? migrationOffer = null,
        IEnumerable<IDiagnosticsRepublisher>? republishers = null)
    {
        _republishers = republishers?.ToList() ?? [];
        _migrationOffer = migrationOffer;
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

        // Publish diagnostics for the indexed workspace, behind the open gate.
        //
        // Measured 2026-09-27: nothing did this, and the ONLY path to a published diagnostic was a
        // didOpen. The server starts in a state where the XML sync handler never receives that
        // notification often enough to matter - 7 of 8 starts in one batch on the eaw workspace,
        // 1 of 4 in another - and such a session then published nothing for ANY file until it was
        // restarted. The publishers already read a closed document's text from disk, so the sweep
        // needs no editor buffer; it simply was never asked to run.
        //
        // After the gate on purpose: it touches every indexed document, and holding the gate for it
        // would put that work in front of the editor becoming usable.
        foreach (var republisher in _republishers)
        {
            try
            {
                await republisher.RepublishAllAsync(ct);
            }
            catch (Exception ex)
            {
                // Startup is fire-and-forget, so an escaping exception here is unobserved - and one
                // publisher failing must not cost the others their sweep.
                _logger.LogWarning(ex, "Could not publish workspace diagnostics for {Publisher}.",
                    republisher.GetType().Name);
            }
        }

        // Last, and deliberately outside everything above: the offer is a question, and nothing
        // else on this task runs until the user answers it. Behind an open gate that costs them
        // nothing; in front of it, it WAS the server - 22.1s of a startup that otherwise takes
        // 1.75s, measured on the eaw workspace, with every buffered notification waiting on a
        // dialog. A degraded start still asks: the file it offers to write may be the fix.
        if (_migrationOffer is null) return;
        try
        {
            await _migrationOffer.OfferPendingAsync(ct);
        }
        catch (Exception ex)
        {
            // Startup is fire-and-forget, so an escaping exception here is an unobserved one.
            _logger.LogWarning(ex, "Could not offer pending project-file migrations.");
        }
    }
}
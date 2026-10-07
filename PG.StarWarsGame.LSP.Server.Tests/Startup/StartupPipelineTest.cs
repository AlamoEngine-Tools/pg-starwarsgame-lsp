// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Server.Project;
using PG.StarWarsGame.LSP.Server.Startup;
using PG.StarWarsGame.LSP.Server.Status;

namespace PG.StarWarsGame.LSP.Server.Tests.Startup;

public sealed class StartupPipelineTest
{
    private static StartupPipeline Build(
        Log log, IModProjectReloadService reloadService, IStartupGate gate,
        IPgprojMigrationOffer? migrationOffer = null,
        IEnumerable<IDiagnosticsRepublisher>? republishers = null,
        bool workspaceDiagnosticsOnStartup = false,
        ServerStatusRecorder? status = null)
    {
        return new StartupPipeline(
            new RecordingSchemaBootstrapper(log),
            new RecordingBaselineBootstrapper(log),
            reloadService,
            gate,
            new RecordingProgress(log),
            new RecordingNotifier(log),
            NullLogger<StartupPipeline>.Instance,
            migrationOffer,
            republishers,
            new StubConfigurationProvider(workspaceDiagnosticsOnStartup),
            status);
    }

    // ── what the bug report is told ──────────────────────────────────────────

    [Fact]
    public async Task RunAsync_RecordsIndexingAsBuildingUntilItFinishes()
    {
        var status = new ServerStatusRecorder();
        Assert.Equal(StatusIndexState.Building, status.IndexState);

        await Build(new Log(), new RecordingReloadService(new Log()), new RecordingGate(new Log()), status: status)
            .RunAsync(["/ws"], CancellationToken.None);

        Assert.Equal(StatusIndexState.Complete, status.IndexState);
        Assert.NotNull(status.IndexDuration);
    }

    // The pipeline swallows a stage failure so the gate always opens, which left "degraded" visible
    // in the log alone - a report from that state looked like a healthy one with no symbols.
    [Fact]
    public async Task RunAsync_RecordsAFailedStage()
    {
        var status = new ServerStatusRecorder();

        await Build(new Log(), new ThrowingReloadService(), new RecordingGate(new Log()), status: status)
            .RunAsync(["/ws"], CancellationToken.None);

        Assert.Equal(StatusIndexState.Failed, status.IndexState);
    }

    private sealed class StubConfigurationProvider : ILspConfigurationProvider
    {
        public StubConfigurationProvider(bool workspaceDiagnosticsOnStartup)
        {
            Current = new LspConfiguration
            {
                Diagnostics = new DiagnosticsConfig { WorkspaceOnStartup = workspaceDiagnosticsOnStartup }
            };
        }

        public LspConfiguration Current { get; }

        public void LoadFrom(object? initializationOptions)
        {
        }
    }

    /// <summary>
    ///     The workspace sweep is OFF unless asked for.
    ///     <para>
    ///         Measured 2026-09-28 on the eaw workspace - the base game, not a mod: 18,281
    ///         diagnostics, 21.8 MiB on the wire, 11s of publishing after a 2s scan. FoC is larger
    ///         and a real mod larger again. That cannot be what a server does on every start.
    ///     </para>
    /// </summary>
    [Fact]
    public async Task RunAsync_ByDefault_DoesNotSweepTheWorkspace()
    {
        var log = new Log();
        var pipeline = Build(log, new RecordingReloadService(log), new RecordingGate(log),
            republishers: [new RecordingRepublisher(log, "xml")]);

        await pipeline.RunAsync(["/ws"], CancellationToken.None);

        Assert.DoesNotContain("republish.xml", log.Entries);
    }

    /// <summary>
    ///     Turned on, it sweeps - the escape hatch for the case it exists to cover: a start where
    ///     the XML sync handler never receives <c>didOpen</c>, after which the session publishes
    ///     nothing for any file until it is restarted.
    /// </summary>
    [Fact]
    public async Task RunAsync_WhenEnabled_RepublishesDiagnosticsAfterTheScan()
    {
        var log = new Log();
        var pipeline = Build(log, new RecordingReloadService(log), new RecordingGate(log),
            republishers: [new RecordingRepublisher(log, "xml"), new RecordingRepublisher(log, "lua")],
            workspaceDiagnosticsOnStartup: true);

        await pipeline.RunAsync(["/ws"], CancellationToken.None);

        Assert.Contains("republish.xml", log.Entries);
        Assert.Contains("republish.lua", log.Entries);

        // After the gate, never before it: the sweep touches every indexed document, and holding
        // the gate shut for it would put that work in front of the editor becoming usable.
        Assert.True(log.Entries.IndexOf("gate.open") < log.Entries.IndexOf("republish.xml"),
            "the gate must open before the workspace republish");
    }

    /// <summary>
    ///     Startup is fire-and-forget, so an escaping exception here is an unobserved one - and one
    ///     publisher failing must not cost the others their sweep.
    /// </summary>
    [Fact]
    public async Task RunAsync_RepublishFailure_DoesNotStopTheOthers()
    {
        var log = new Log();
        var pipeline = Build(log, new RecordingReloadService(log), new RecordingGate(log),
            republishers: [new ThrowingRepublisher(), new RecordingRepublisher(log, "xml")],
            workspaceDiagnosticsOnStartup: true);

        await pipeline.RunAsync(["/ws"], CancellationToken.None);

        Assert.Contains("republish.xml", log.Entries);
    }

    [Fact]
    public async Task RunAsync_ExecutesStagesInOrder()
    {
        var log = new Log();
        var gate = new RecordingGate(log);
        var pipeline = Build(log, new RecordingReloadService(log), gate);

        await pipeline.RunAsync(["/ws"], CancellationToken.None);

        // Schema and baseline run in parallel - their relative order is non-deterministic.
        // What IS guaranteed: both complete before indexing, and finalization preserves its chain.
        Assert.Contains("schema", log.Entries);
        Assert.Contains("baseline", log.Entries);
        var loadIdx = log.Entries.IndexOf("load");
        Assert.True(log.Entries.IndexOf("schema") < loadIdx, "'schema' must precede 'load'");
        Assert.True(log.Entries.IndexOf("baseline") < loadIdx, "'baseline' must precede 'load'");
        Assert.True(loadIdx < log.Entries.IndexOf("gate.open"), "'load' must precede 'gate.open'");
        Assert.True(log.Entries.IndexOf("gate.open") < log.Entries.IndexOf("progress.complete"));
        Assert.True(log.Entries.IndexOf("progress.complete") < log.Entries.IndexOf("notify"));
    }

    [Fact]
    public async Task RunAsync_RunsSchemaAndBaselineInParallel()
    {
        // Concurrency trap: schema blocks until baseline starts. If the pipeline runs them
        // sequentially, schema blocks forever (baseline never starts) and pipelineTask never
        // completes. If parallel, baseline starts immediately, signals the TCS, schema
        // unblocks, and the pipeline finishes promptly.
        var baselineStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var log = new Log();
        var gate = new RecordingGate(log);

        var pipeline = new StartupPipeline(
            new BlockingSchemaBootstrapper(baselineStarted.Task),
            new SignallingBaselineBootstrapper(log, baselineStarted),
            new RecordingReloadService(log),
            gate,
            new RecordingProgress(log),
            new RecordingNotifier(log),
            NullLogger<StartupPipeline>.Instance);

        var pipelineTask = pipeline.RunAsync(["/ws"], CancellationToken.None);
        var timeout = Task.Delay(TimeSpan.FromSeconds(5));
        var first = await Task.WhenAny(pipelineTask, timeout);

        Assert.True(first == pipelineTask,
            "Startup pipeline deadlocked - schema and baseline appear to be running sequentially");
        Assert.Contains("baseline", log.Entries);
    }

    [Fact]
    public async Task RunAsync_OpensGate_EvenWhenIndexingThrows()
    {
        var log = new Log();
        var gate = new RecordingGate(log);
        var pipeline = Build(log, new ThrowingReloadService(), gate);

        await pipeline.RunAsync(["/ws"], CancellationToken.None);

        Assert.Contains("gate.open", log.Entries);
        Assert.True(gate.IsOpen);
    }

    [Fact]
    public async Task RunAsync_AsksAboutPendingMigrations_OnlyOnceTheGateIsOpen()
    {
        // The offer is a modal question about the user's own project file. Asked from inside the
        // load, it sits in front of the gate: every buffered notification waits for the answer, and
        // the server says nothing for as long as the user reads. Measured at 22.1s on the eaw
        // workspace, against 1.75s for the same startup with nothing left to migrate.
        var log = new Log();
        var gate = new RecordingGate(log);
        var pipeline = Build(log, new RecordingReloadService(log), gate, new RecordingMigrationOffer(log));

        await pipeline.RunAsync(["/ws"], CancellationToken.None);

        Assert.Contains("migration.offer", log.Entries);
        Assert.True(log.Entries.IndexOf("gate.open") < log.Entries.IndexOf("migration.offer"),
            "'gate.open' must precede 'migration.offer'");
    }

    [Fact]
    public async Task RunAsync_StillAsksAboutPendingMigrations_WhenIndexingThrows()
    {
        // A project file brought forward in memory is the one thing a degraded start must still
        // offer to write down - the failure it degraded on may be the very thing it fixes.
        var log = new Log();
        var pipeline = Build(log, new ThrowingReloadService(), new RecordingGate(log),
            new RecordingMigrationOffer(log));

        await pipeline.RunAsync(["/ws"], CancellationToken.None);

        Assert.Contains("migration.offer", log.Entries);
    }

    [Fact]
    public async Task RunAsync_PassesScanRoots_ToReloadService()
    {
        var log = new Log();
        var reload = new RecordingReloadService(log);
        var pipeline = Build(log, reload, new RecordingGate(log));

        await pipeline.RunAsync(["/ws/a", "/ws/b"], CancellationToken.None);

        Assert.Equal(["/ws/a", "/ws/b"], reload.LastRoots);
    }

    // ── recording fakes ──────────────────────────────────────────────────────

    private sealed class Log
    {
        public readonly List<string> Entries = [];

        public void Add(string entry)
        {
            lock (Entries)
            {
                Entries.Add(entry);
            }
        }
    }

    private sealed class RecordingSchemaBootstrapper : ISchemaBootstrapper
    {
        private readonly Log _log;

        public RecordingSchemaBootstrapper(Log log)
        {
            _log = log;
        }

        public Task LoadAsync(CancellationToken ct)
        {
            _log.Add("schema");
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingBaselineBootstrapper : IBaselineBootstrapper
    {
        private readonly Log _log;

        public RecordingBaselineBootstrapper(Log log)
        {
            _log = log;
        }

        public Task LoadAsync(CancellationToken ct)
        {
            _log.Add("baseline");
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingReloadService : IModProjectReloadService
    {
        private readonly Log _log;

        public RecordingReloadService(Log log)
        {
            _log = log;
        }

        public IReadOnlyList<string>? LastRoots { get; private set; }
        public IReadOnlyList<string>? LastAssetRoots => null;
        public WorkspaceConfiguration? LastWorkspaceConfig => null;
        public IReadOnlyList<string>? LastWorkspaceRoots => null;

        public Task LoadAsync(IEnumerable<string> workspaceRoots, CancellationToken ct)
        {
            LastRoots = workspaceRoots.ToList();
            _log.Add("load");
            return Task.CompletedTask;
        }

        public Task ReloadAsync(CancellationToken ct)
        {
            return Task.CompletedTask;
        }

        public Task ReloadLocalisationAsync(CancellationToken ct)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingReloadService : IModProjectReloadService
    {
        public IReadOnlyList<string>? LastAssetRoots => null;
        public WorkspaceConfiguration? LastWorkspaceConfig => null;
        public IReadOnlyList<string>? LastWorkspaceRoots => null;

        public Task LoadAsync(IEnumerable<string> workspaceRoots, CancellationToken ct)
        {
            throw new InvalidOperationException("boom");
        }

        public Task ReloadAsync(CancellationToken ct)
        {
            return Task.CompletedTask;
        }

        public Task ReloadLocalisationAsync(CancellationToken ct)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingMigrationOffer : IPgprojMigrationOffer
    {
        private readonly Log _log;

        public RecordingMigrationOffer(Log log)
        {
            _log = log;
        }

        public Task OfferPendingAsync(CancellationToken ct)
        {
            _log.Add("migration.offer");
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingGate : IStartupGate
    {
        private readonly Log _log;

        public RecordingGate(Log log)
        {
            _log = log;
        }

        public bool IsOpen { get; private set; }

        public event Action? Opened;

        public Task RunOrBufferAsync(Func<CancellationToken, Task> action, CancellationToken ct)
        {
            return action(ct);
        }

        public Task OpenAsync()
        {
            IsOpen = true;
            Opened?.Invoke();
            _log.Add("gate.open");
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingProgress : IStartupProgress
    {
        private readonly Log _log;

        public RecordingProgress(Log log)
        {
            _log = log;
        }

        public void Report(string stage, int percent)
        {
        }

        public void Complete()
        {
            _log.Add("progress.complete");
        }
    }

    private sealed class RecordingNotifier : IStartupNotifier
    {
        private readonly Log _log;

        public RecordingNotifier(Log log)
        {
            _log = log;
        }

        public void NotifyScanComplete()
        {
            _log.Add("notify");
        }
    }

    private sealed class RecordingRepublisher : IDiagnosticsRepublisher
    {
        private readonly Log _log;
        private readonly string _name;

        public RecordingRepublisher(Log log, string name)
        {
            _log = log;
            _name = name;
        }

        public Task RepublishAllAsync(CancellationToken ct)
        {
            _log.Add($"republish.{_name}");
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingRepublisher : IDiagnosticsRepublisher
    {
        public Task RepublishAllAsync(CancellationToken ct)
        {
            throw new InvalidOperationException("republish blew up");
        }
    }

    private sealed class BlockingSchemaBootstrapper : ISchemaBootstrapper
    {
        private readonly Task _unblockSignal;

        public BlockingSchemaBootstrapper(Task unblockSignal)
        {
            _unblockSignal = unblockSignal;
        }

        public Task LoadAsync(CancellationToken ct)
        {
            return _unblockSignal;
        }
    }

    private sealed class SignallingBaselineBootstrapper : IBaselineBootstrapper
    {
        private readonly Log _log;
        private readonly TaskCompletionSource _signal;

        public SignallingBaselineBootstrapper(Log log, TaskCompletionSource signal)
        {
            _log = log;
            _signal = signal;
        }

        public Task LoadAsync(CancellationToken ct)
        {
            _log.Add("baseline");
            _signal.TrySetResult();
            return Task.CompletedTask;
        }
    }
}
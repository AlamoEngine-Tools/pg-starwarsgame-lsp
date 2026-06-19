// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Server.Project;
using PG.StarWarsGame.LSP.Server.Startup;

namespace PG.StarWarsGame.LSP.Server.Tests.Startup;

public sealed class StartupPipelineTest
{
    private static StartupPipeline Build(Log log, IModProjectReloadService reloadService, IStartupGate gate)
    {
        return new StartupPipeline(
            new RecordingSchemaBootstrapper(log),
            new RecordingBaselineBootstrapper(log),
            reloadService,
            gate,
            new RecordingProgress(log),
            new RecordingNotifier(log),
            NullLogger<StartupPipeline>.Instance);
    }

    [Fact]
    public async Task RunAsync_ExecutesStagesInOrder()
    {
        var log = new Log();
        var gate = new RecordingGate(log);
        var pipeline = Build(log, new RecordingReloadService(log), gate);

        await pipeline.RunAsync(["/ws"], CancellationToken.None);

        // Schema and baseline run in parallel — their relative order is non-deterministic.
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
            "Startup pipeline deadlocked — schema and baseline appear to be running sequentially");
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
        Assert.True(gate.Opened);
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
    }

    private sealed class RecordingGate : IStartupGate
    {
        private readonly Log _log;

        public RecordingGate(Log log)
        {
            _log = log;
        }

        public bool Opened { get; private set; }
        public bool IsOpen => Opened;

        public Task RunOrBufferAsync(Func<CancellationToken, Task> action, CancellationToken ct)
        {
            return action(ct);
        }

        public Task OpenAsync()
        {
            Opened = true;
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
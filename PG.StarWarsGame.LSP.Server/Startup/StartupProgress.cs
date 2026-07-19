// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server.WorkDone;
using PG.StarWarsGame.LSP.Core.Configuration;

namespace PG.StarWarsGame.LSP.Server.Startup;

/// <summary>
///     Reports linear startup progress over a single client <c>window/workDoneProgress</c> token.
///     Best-effort: the token is created lazily and asynchronously (some clients delay or never
///     grant it), so <see cref="Report" /> simply logs until the observer is available. The progress
///     token's async creation is the one contained piece of asynchrony here - it is never on the
///     deterministic indexing path.
/// </summary>
public sealed class StartupProgress : IStartupProgress
{
    private readonly ILogger<StartupProgress> _logger;
    private readonly ServerOptions _options;
    private readonly IServerWorkDoneManager? _workDone;

    private Task<IWorkDoneObserver>? _createTask;
    private bool _started;

    public StartupProgress(ILogger<StartupProgress> logger, ServerOptions? options = null,
        IServerWorkDoneManager? workDone = null)
    {
        _logger = logger;
        _options = options ?? ServerOptions.Default;
        _workDone = workDone;
    }

    public void Report(string stage, int percent)
    {
        _logger.LogInformation("[startup] {Stage} ({Percent}%)", stage, percent);
        EnsureStarted();
        var observer = Observer();
        observer?.OnNext(new WorkDoneProgressReport { Message = stage, Percentage = percent });
    }

    public void Complete()
    {
        var observer = Observer();
        observer?.OnNext(new WorkDoneProgressReport { Message = "Ready", Percentage = 100 });
        observer?.Dispose();
    }

    private void EnsureStarted()
    {
        if (_started) return;
        _started = true;
        if (_workDone is not { IsSupported: true }) return;

        // window/workDoneProgress/create awaits a client response that some clients delay or never
        // send. Cap it so a missing token never blocks startup; reports before it resolves are
        // logged only.
        _createTask = CreateObserverAsync();
    }

    private async Task<IWorkDoneObserver> CreateObserverAsync()
    {
        var createTask = _workDone!.Create(
            new WorkDoneProgressBegin
            {
                Title = "Star Wars LSP - starting",
                Message = "Initialising…",
                Cancellable = false,
                Percentage = 0
            },
            null!,
            null!,
            CancellationToken.None);

        var winner = await Task.WhenAny(createTask, Task.Delay(_options.ProgressReporterTimeout));
        if (winner == createTask && createTask.IsCompletedSuccessfully)
            return await createTask;

        throw new TimeoutException("Client did not grant a work-done progress token in time.");
    }

    private IWorkDoneObserver? Observer()
    {
        return _createTask is { IsCompletedSuccessfully: true } ? _createTask.Result : null;
    }
}
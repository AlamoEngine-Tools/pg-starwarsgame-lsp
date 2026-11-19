// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.JsonRpc;

namespace PG.StarWarsGame.LSP.Server.Preview;

/// <summary>
///     Shows the preview cache build to the reader by telling the client about it.
/// </summary>
/// <remarks>
///     A notification of our own rather than <c>window/workDoneProgress</c>: the editor draws a
///     work-done progress in its status bar, where a progress cannot be cancelled, and this build
///     has to be. The client shows these as a progress with a Cancel button and answers a press
///     with <c>aet/cancelPreviewCacheBuild</c>.
/// </remarks>
public sealed class NotifiedPreviewBuildProgress(
    Action<PreviewCacheBuildProgress> send,
    ILogger<NotifiedPreviewBuildProgress> logger) : IPreviewBuildProgress
{
    /// <summary>The notification's method.</summary>
    public const string Notification = "aet/previewCacheBuild";

    public const string Running = "running";
    public const string Ended = "ended";

    public IPreviewBuildProgressRun Begin(int total, Action cancel)
    {
        var run = new Run(this);
        run.Report(0, total);
        return run;
    }

    private void Send(PreviewCacheBuildProgress progress)
    {
        try
        {
            send(progress);
        }
        catch (Exception e)
        {
            // A client that has gone away must not stop the build.
            if (logger.IsEnabled(LogLevel.Debug))
                logger.LogDebug(e, "Could not send {Method}", Notification);
        }
    }

    private sealed class Run(NotifiedPreviewBuildProgress owner) : IPreviewBuildProgressRun
    {
        private int _done;
        private int _total;

        public void Report(int done, int total)
        {
            _done = done;
            _total = total;
            owner.Send(new PreviewCacheBuildProgress(Running, done, total));
        }

        public void Dispose()
        {
            owner.Send(new PreviewCacheBuildProgress(Ended, _done, _total));
        }
    }
}

/// <summary>Stops the background build of the preview cache when the reader cancels it.</summary>
public sealed class CancelPreviewCacheBuildHandler(IPreviewCacheBuilder builder)
    : IJsonRpcRequestHandler<CancelPreviewCacheBuildParams, CancelPreviewCacheBuildResult>
{
    public Task<CancelPreviewCacheBuildResult> Handle(
        CancelPreviewCacheBuildParams request, CancellationToken cancellationToken)
    {
        builder.Cancel();
        return Task.FromResult(new CancelPreviewCacheBuildResult());
    }
}

/// <summary>Empties the project's preview cache on the reader's command.</summary>
/// <remarks>
///     A build under way is stopped first, or it would fill the folder again behind the command.
///     The model it has in hand is still written when it finishes - one entry, converted from what
///     is on disk now, so nothing stale. What was removed is converted again when something asks
///     for it, and the next session builds the whole project again: the finished build's marker
///     goes with the entries.
/// </remarks>
public sealed class ClearPreviewCacheHandler(IPreviewCacheBuilder builder, IPreviewModelCache cache)
    : IJsonRpcRequestHandler<ClearPreviewCacheParams, ClearPreviewCacheResult>
{
    public Task<ClearPreviewCacheResult> Handle(ClearPreviewCacheParams request, CancellationToken cancellationToken)
    {
        builder.Cancel();
        return Task.FromResult(new ClearPreviewCacheResult(cache.Clear()));
    }
}

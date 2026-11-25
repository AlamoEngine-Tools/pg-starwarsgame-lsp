// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace PG.StarWarsGame.LSP.E2E.Tests;

/// <summary>
///     The wait behind <see cref="LspServerFixture.WaitForSettledDiagnosticsAsync" />: the diagnostics
///     a document ENDS UP with. Apart from the fixture so it can be tested without a server - it is
///     handed how to listen, not whom to listen to.
/// </summary>
internal static class SettledDiagnostics
{
    /// <summary>
    ///     The last publish for <paramref name="uri" /> once none has arrived for
    ///     <paramref name="quiet" />, or null when none arrived in time.
    /// </summary>
    /// <param name="subscribe">Starts delivering every publish to the handler it is given.</param>
    /// <param name="unsubscribe">Stops it again.</param>
    /// <param name="uri">The document whose publishes count.</param>
    /// <param name="quiet">How long nothing must arrive before the last publish is the settled one.</param>
    /// <param name="timeout">How long to wait in all.</param>
    /// <param name="after">
    ///     A barrier: the moment the server has taken in what the test changed. The wait listens from
    ///     the start, but a publish from before the barrier is not the settled state and is dropped,
    ///     and the quiet window is counted from the barrier at the earliest. Null counts from the
    ///     call, which is right when nothing is changed while waiting.
    /// </param>
    /// <remarks>
    ///     <para>
    ///         The barrier is there because a quiet window alone cannot tell "the server is done"
    ///         from "the server has not started". A publish computed before an edit was processed
    ///         can be followed by a silence longer than any reasonable window while the server works
    ///         through the edit - and the wait then returns the one publish that describes the
    ///         state the test just changed. Measured in the suite's server logs over 196 rename
    ///         roundtrips: that silence exceeded 2 s three times, always on a slow run, while
    ///         counted from the end of the edit's processing the longest silence before the last
    ///         publish was 1.22 s.
    ///     </para>
    ///     <para>
    ///         With a barrier, a wait that sees no publish after it returns null rather than the
    ///         last one from before it. That one may describe a half-applied edit, and a test that
    ///         asserts on it passes or fails on something the author never sees.
    ///     </para>
    /// </remarks>
    public static async Task<PublishDiagnosticsParams?> WaitAsync(
        Action<Action<PublishDiagnosticsParams>> subscribe,
        Action<Action<PublishDiagnosticsParams>> unsubscribe,
        DocumentUri uri,
        TimeSpan quiet,
        TimeSpan timeout,
        Task? after = null)
    {
        var uriStr = uri.ToString();
        var gate = new object();
        PublishDiagnosticsParams? last = null;
        var lastAt = DateTime.UtcNow;
        var passed = after is null;

        void Handler(PublishDiagnosticsParams p)
        {
            if (!string.Equals(p.Uri.ToString(), uriStr, StringComparison.OrdinalIgnoreCase)) return;
            lock (gate)
            {
                last = p;
                lastAt = DateTime.UtcNow;
            }
        }

        subscribe(Handler);
        try
        {
            // The barrier's own moment, taken as it completes rather than when the loop next looks:
            // a publish that follows it within a poll must not be mistaken for one from before it.
            _ = after?.ContinueWith(
                _ =>
                {
                    lock (gate)
                    {
                        passed = true;
                        last = null;
                        lastAt = DateTime.UtcNow;
                    }
                },
                TaskContinuationOptions.ExecuteSynchronously);

            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                await Task.Delay(25);

                // A barrier that failed is the test's failure, not a reason to wait the timeout out.
                if (after is { IsFaulted: true } or { IsCanceled: true })
                    await after;

                lock (gate)
                {
                    if (passed && last is not null && DateTime.UtcNow - lastAt >= quiet)
                        return last;
                }
            }

            lock (gate)
            {
                return passed ? last : null;
            }
        }
        finally
        {
            unsubscribe(Handler);
        }
    }
}
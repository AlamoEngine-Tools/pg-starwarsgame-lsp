// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Server.Notifications;

/// <summary>
///     Sends one notification after a burst of index changes has settled.
/// </summary>
/// <remarks>
///     <para>
///         A workspace-wide rename raises <see cref="IGameIndexService.IndexChanged" /> hundreds of
///         times. Without the collapse below each one costs every open panel a full rebuild - a
///         scene reassembled from its variant chain, or every campaign model re-inspected - for a
///         result the next event immediately invalidates.
///     </para>
///     <para>
///         The rule is last-one-wins: each change takes a ticket, waits, and sends only if no later
///         change has taken one since. A debounce of zero sends inline, which is what the tests use
///         to avoid waiting on a timer.
///     </para>
///     <para>
///         Only the timing lives here. What to send, whether to send at all, and how to survive a
///         client that has gone away are the subclass's, because those differ completely between
///         the two - one posts a bare method name, the other gates on a feature flag and names the
///         campaigns it invalidated.
///     </para>
/// </remarks>
public abstract class DebouncedIndexNotifier
{
    private readonly int _debounceMs;
    private int _pendingVersion;

    protected DebouncedIndexNotifier(IGameIndexService indexService, int debounceMs)
    {
        _debounceMs = debounceMs;
        indexService.IndexChanged += OnIndexChanged;
    }

    /// <summary>
    ///     Sends whatever this notifier sends. Called once per settled burst.
    /// </summary>
    /// <remarks>
    ///     Runs on the thread that completed the index write, or on a pool thread after the delay,
    ///     so an implementation swallows its own failures: a panel refresh is a convenience and must
    ///     never take the indexer down with it.
    /// </remarks>
    protected abstract void Notify();

    private void OnIndexChanged(GameIndex index)
    {
        if (_debounceMs <= 0)
        {
            Notify();
            return;
        }

        var version = Interlocked.Increment(ref _pendingVersion);
        _ = Task.Run(async () =>
        {
            await Task.Delay(_debounceMs);
            if (Volatile.Read(ref _pendingVersion) != version) return;
            Notify();
        });
    }
}

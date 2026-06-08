// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Workspace;

/// <summary>
///     Buffers inbound client notifications that arrive while the startup pipeline is running,
///     then drains them in arrival order once the server is truly ready, after which it becomes
///     a transparent pass-through. Replaces the old flag-gated <c>PreOpenBuffer</c> race.
/// </summary>
public interface IStartupGate
{
    /// <summary>
    ///     <see langword="true" /> once <see cref="OpenAsync" /> has completed and the gate runs
    ///     actions immediately.
    /// </summary>
    bool IsOpen { get; }

    /// <summary>
    ///     Runs <paramref name="action" /> immediately when the gate is open; otherwise buffers it
    ///     for replay during <see cref="OpenAsync" /> and returns without running it.
    /// </summary>
    Task RunOrBufferAsync(Func<CancellationToken, Task> action, CancellationToken ct);

    /// <summary>
    ///     Drains all buffered actions in arrival order, then opens the gate. Actions enqueued while
    ///     draining are replayed after the originally-buffered ones. Idempotent: a second call is a
    ///     no-op and never re-runs already-drained actions.
    /// </summary>
    Task OpenAsync();
}
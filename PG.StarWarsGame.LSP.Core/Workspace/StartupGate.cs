// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Workspace;

public sealed class StartupGate : IStartupGate
{
    private readonly object _lock = new();
    private readonly Queue<Func<CancellationToken, Task>> _pending = new();
    private bool _open;

    public bool IsOpen
    {
        get
        {
            lock (_lock)
            {
                return _open;
            }
        }
    }

    public Task RunOrBufferAsync(Func<CancellationToken, Task> action, CancellationToken ct)
    {
        lock (_lock)
        {
            // While buffering - and while draining (_open is still false) - enqueue rather than run,
            // so live events never overtake the buffered ones mid-drain.
            if (!_open)
            {
                _pending.Enqueue(action);
                return Task.CompletedTask;
            }
        }

        return action(ct);
    }

    public async Task OpenAsync()
    {
        while (true)
        {
            Func<CancellationToken, Task> next;
            lock (_lock)
            {
                if (_pending.Count == 0)
                {
                    // Flip to open under the lock once the queue is empty so no late arrival is missed.
                    _open = true;
                    return;
                }

                next = _pending.Dequeue();
            }

            // Awaited outside the lock; actions enqueued by this call (or by concurrent handlers)
            // land in _pending and are picked up by the next loop iteration.
            await next(CancellationToken.None);
        }
    }
}
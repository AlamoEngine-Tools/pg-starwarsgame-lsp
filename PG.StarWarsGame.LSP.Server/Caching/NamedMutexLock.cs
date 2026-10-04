// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using PG.StarWarsGame.LSP.Core.Util;

namespace PG.StarWarsGame.LSP.Server.Caching;

/// <summary>
///     <see cref="ICrossProcessLock" /> over a named <see cref="Mutex" />, the one primitive that
///     serializes across processes on Windows and Linux alike (on Linux .NET backs it with a file
///     in the shared memory directory).
/// </summary>
/// <remarks>
///     <para>
///         The lock name is hashed: a mutex name is limited in length, a backslash in it is a
///         namespace separator, and the names here are derived from paths. The hash keeps the
///         mapping stable across processes, which is all a lock name needs.
///     </para>
///     <para>
///         A wait is bounded. A server that dies while holding the mutex abandons it, which the
///         next waiter receives as <see cref="AbandonedMutexException" /> - that still means
///         "acquired", so it is treated as such. A wait that runs past the bound is logged and the
///         caller proceeds unlocked: every protected write is atomic on its own, so the worst
///         outcome of proceeding is a duplicated write, where blocking a server start forever is
///         not an option.
///     </para>
/// </remarks>
public sealed class NamedMutexLock : ICrossProcessLock
{
    private static readonly TimeSpan WaitBound = TimeSpan.FromSeconds(30);
    private readonly ILogger<NamedMutexLock> _logger;

    public NamedMutexLock(ILogger<NamedMutexLock> logger)
    {
        _logger = logger;
    }

    public IDisposable Acquire(string name)
    {
        var mutex = new Mutex(false, MutexName(name));
        bool held;
        try
        {
            held = mutex.WaitOne(WaitBound);
        }
        catch (AbandonedMutexException)
        {
            // The previous holder died without releasing. The mutex is ours now and the data it
            // guarded is whatever that process left behind - every write it protects is atomic,
            // so that is a consistent older state, not a torn one.
            held = true;
        }

        if (!held)
            _logger.LogWarning(
                "Cross-process lock '{Name}' not acquired within {Seconds}s; proceeding without it",
                name, WaitBound.TotalSeconds);

        return new Release(mutex, held);
    }

    private static string MutexName(string name)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(name));
        return "Global\\aetswg-" + Convert.ToHexString(hash, 0, 16);
    }

    private sealed class Release(Mutex mutex, bool held) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (held)
                mutex.ReleaseMutex();
            mutex.Dispose();
        }
    }
}

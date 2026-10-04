// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Util;

/// <summary>
///     Serializes a critical section across every server process on this machine. One server runs
///     per open project, and several start at the same moment on the same shared files: the
///     schema mirror and baseline under <c>~/.aetswg</c>, a shared dependency's <c>.aetswg/</c>.
/// </summary>
/// <remarks>
///     <see cref="AtomicFile" /> already keeps a single file whole; this is for the read-compare-
///     write sequences around it (append a missing line, prune old snapshots, write a set of files
///     that belong together), where two processes interleaving would both read "missing" and both
///     act. The name is any string; implementations map it to a machine-wide primitive.
/// </remarks>
public interface ICrossProcessLock
{
    /// <summary>Blocks until the named lock is held; disposing the result releases it.</summary>
    IDisposable Acquire(string name);
}

/// <summary>No locking - for tests and for callers that run alone by construction.</summary>
public sealed class NullCrossProcessLock : ICrossProcessLock
{
    private static readonly IDisposable Nothing = new NoRelease();

    public IDisposable Acquire(string name)
    {
        return Nothing;
    }

    private sealed class NoRelease : IDisposable
    {
        public void Dispose()
        {
        }
    }
}

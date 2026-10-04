// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Server.Caching;

namespace PG.StarWarsGame.LSP.Server.Tests.Caching;

/// <summary>
///     One server per open project means several processes start together and write the same
///     files: the schema mirror and baseline under <c>~/.aetswg</c>, a shared dependency's
///     <c>.aetswg/</c>. A named mutex is the one primitive that serializes across processes on
///     Windows and Linux alike; these tests exercise it across threads, which a named mutex also
///     covers.
/// </summary>
public sealed class NamedMutexLockTest
{
    private static NamedMutexLock Build()
    {
        return new NamedMutexLock(NullLogger<NamedMutexLock>.Instance);
    }

    [Fact]
    public void Acquire_SameName_SecondHolderWaitsForTheFirst()
    {
        var name = "test-" + Guid.NewGuid().ToString("N");
        var firstHeld = new ManualResetEventSlim();
        var releaseFirst = new ManualResetEventSlim();
        var order = new List<string>();
        var sync = new object();

        var first = Task.Run(() =>
        {
            var l = Build();
            using (l.Acquire(name))
            {
                lock (sync) order.Add("first-in");
                firstHeld.Set();
                releaseFirst.Wait(TimeSpan.FromSeconds(10));
                lock (sync) order.Add("first-out");
            }
        });
        firstHeld.Wait(TimeSpan.FromSeconds(10));

        var second = Task.Run(() =>
        {
            var l = Build();
            using (l.Acquire(name))
            {
                lock (sync) order.Add("second-in");
            }
        });

        // The second holder must still be waiting while the first holds the lock.
        Assert.False(second.Wait(TimeSpan.FromMilliseconds(300)));
        releaseFirst.Set();
        Assert.True(Task.WaitAll([first, second], TimeSpan.FromSeconds(10)));

        Assert.Equal(["first-in", "first-out", "second-in"], order);
    }

    [Fact]
    public void Acquire_DifferentNames_DoNotBlockEachOther()
    {
        var l = Build();
        using var a = l.Acquire("test-a-" + Guid.NewGuid().ToString("N"));
        var acquiredB = Task.Run(() =>
        {
            using var b = l.Acquire("test-b-" + Guid.NewGuid().ToString("N"));
            return true;
        });

        Assert.True(acquiredB.Wait(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void Acquire_NameWithPathCharacters_IsAccepted()
    {
        // Lock names are derived from paths; a backslash in a mutex name is a namespace separator
        // and other characters are refused by the OS, so the name is sanitized before use.
        var l = Build();
        using var held = l.Acquire(@"C:\Users\someone\.aetswg\schema/_index.json");
    }

    [Fact]
    public void Acquire_IsReentrantForTheSameThread()
    {
        var l = Build();
        var name = "test-" + Guid.NewGuid().ToString("N");
        using var outer = l.Acquire(name);
        using var inner = l.Acquire(name);
    }
}

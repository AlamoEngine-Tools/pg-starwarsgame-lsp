// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime;
using Microsoft.Extensions.Logging;
using PG.StarWarsGame.LSP.Core.Util;

namespace PG.StarWarsGame.LSP.Server.Startup;

/// <summary>
///     <see cref="IHeapTrimmer" /> over the runtime's collector: one forced, blocking, compacting
///     gen2 collection with the large object heap compacted as well. Logs the heap before and
///     after, which is how the next measurement reads the effect from a user's log.
/// </summary>
public sealed class GcHeapTrimmer(ILogger<GcHeapTrimmer> logger) : IHeapTrimmer
{
    public void TrimAfterBulkWork(string reason)
    {
        var before = GC.GetGCMemoryInfo();

        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
        GC.WaitForPendingFinalizers();

        var after = GC.GetGCMemoryInfo();
        logger.LogInformation(
            "Heap trimmed after {Reason}: {BeforeMb} MB ({BeforeFragmentedMb} MB fragmented) -> {AfterMb} MB ({AfterFragmentedMb} MB fragmented)",
            reason,
            before.HeapSizeBytes >> 20, before.FragmentedBytes >> 20,
            after.HeapSizeBytes >> 20, after.FragmentedBytes >> 20);
    }
}

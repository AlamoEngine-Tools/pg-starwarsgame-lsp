// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Util;

/// <summary>
///     Gives the managed heap back after a known bulk phase. MEASURED 2026-10-04 on a two-layer
///     workspace: a cold start ended with 534 MB of heap, 253 MB of it fragmentation left behind
///     by the parallel parse of 1,882 files, against 280 MB and 49 MB for the warm start of the
///     same workspace. The live data differed by about 50 MB; the rest was holes the collector had
///     no reason to compact on its own, and a server idles on them for the whole session.
/// </summary>
public interface IHeapTrimmer
{
    /// <summary>
    ///     A full, compacting collection including the large object heap. Called ONCE after a bulk
    ///     phase ends - a workspace load or reload - never on a request path.
    /// </summary>
    void TrimAfterBulkWork(string reason);
}

/// <summary>No trimming - for tests and for hosts that manage the heap themselves.</summary>
public sealed class NullHeapTrimmer : IHeapTrimmer
{
    public void TrimAfterBulkWork(string reason)
    {
    }
}
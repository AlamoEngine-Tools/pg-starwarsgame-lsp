// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Caching;

/// <summary>
///     What one in-memory cache holds right now, for <c>aet/getServerStatus</c>. Counts only -
///     the status goes into public bug reports, so a name here is the CACHE's name, never a
///     document's or a project's.
/// </summary>
/// <param name="Name">A short fixed identifier such as <c>xml-parse</c>, stable across releases.</param>
/// <param name="Entries">Live entries.</param>
/// <param name="ApproximateBytes">
///     An estimate of what the entries hold, where one is cheap to compute (byte arrays, text
///     lengths); null where the cache holds object graphs no cheap estimate covers.
/// </param>
/// <param name="Hits">Lookup hits since start, for caches that count them; otherwise null.</param>
/// <param name="Misses">Lookup misses since start, for caches that count them; otherwise null.</param>
/// <param name="Evictions">Evictions since start, for bounded caches; otherwise null.</param>
public sealed record CacheStatistics(
    string Name,
    int Entries,
    long? ApproximateBytes = null,
    long? Hits = null,
    long? Misses = null,
    long? Evictions = null);

/// <summary>
///     A cache that can describe itself. Registered as a multi-binding; the status handler lists
///     every registration. MEASURED 2026-10-04: a cold start held about 300 MB more than a warm
///     start of the same workspace and nothing could say where - this is how the next such
///     question gets answered from a bug report instead of a debugger.
/// </summary>
public interface ICacheStatisticsSource
{
    CacheStatistics Snapshot();
}

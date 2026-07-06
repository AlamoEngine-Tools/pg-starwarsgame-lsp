// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using PG.StarWarsGame.LSP.Core.Caching;
using PG.StarWarsGame.LSP.Core.Util;

namespace PG.StarWarsGame.LSP.Core.Tests.Caching;

public sealed class ParsedDocumentCacheTest
{
    private sealed record Artifact(string Source);

    private static Artifact Parse(string text)
    {
        return new Artifact(text);
    }

    // ── hit / miss semantics ─────────────────────────────────────────────────

    [Fact]
    public void GetOrParse_SameUriAndHash_ReturnsCachedInstanceWithoutReparsing()
    {
        var cache = new ParsedDocumentCache<Artifact>(4);
        var parses = 0;
        Artifact CountingParse(string text)
        {
            parses++;
            return Parse(text);
        }

        var first = cache.GetOrParse("file:///a.xml", "<A/>", ContentHasher.Hash("<A/>"), CountingParse);
        var second = cache.GetOrParse("file:///a.xml", "<A/>", ContentHasher.Hash("<A/>"), CountingParse);

        Assert.Same(first, second);
        Assert.Equal(1, parses);
    }

    [Fact]
    public void GetOrParse_ChangedHash_ReparsesAndReplacesEntry()
    {
        var cache = new ParsedDocumentCache<Artifact>(4);

        var v1 = cache.GetOrParse("file:///a.xml", "<A/>", ContentHasher.Hash("<A/>"), Parse);
        var v2 = cache.GetOrParse("file:///a.xml", "<A2/>", ContentHasher.Hash("<A2/>"), Parse);
        var v2Again = cache.GetOrParse("file:///a.xml", "<A2/>", ContentHasher.Hash("<A2/>"), Parse);

        Assert.NotSame(v1, v2);
        Assert.Same(v2, v2Again);
    }

    [Fact]
    public void GetOrParse_DifferentUris_AreIndependentEntries()
    {
        var cache = new ParsedDocumentCache<Artifact>(4);

        var a = cache.GetOrParse("file:///a.xml", "<X/>", ContentHasher.Hash("<X/>"), Parse);
        var b = cache.GetOrParse("file:///b.xml", "<X/>", ContentHasher.Hash("<X/>"), Parse);

        Assert.NotSame(a, b);
        Assert.Same(a, cache.GetOrParse("file:///a.xml", "<X/>", ContentHasher.Hash("<X/>"), Parse));
        Assert.Same(b, cache.GetOrParse("file:///b.xml", "<X/>", ContentHasher.Hash("<X/>"), Parse));
    }

    // ── LRU eviction ─────────────────────────────────────────────────────────

    [Fact]
    public void GetOrParse_BeyondCapacity_EvictsLeastRecentlyUsed()
    {
        var cache = new ParsedDocumentCache<Artifact>(2);

        var a = cache.GetOrParse("file:///a.xml", "<A/>", 1, Parse);
        _ = cache.GetOrParse("file:///b.xml", "<B/>", 2, Parse);
        _ = cache.GetOrParse("file:///c.xml", "<C/>", 3, Parse); // evicts a (least recently used)

        var aAgain = cache.GetOrParse("file:///a.xml", "<A/>", 1, Parse);

        Assert.NotSame(a, aAgain); // was evicted, re-parsed
    }

    [Fact]
    public void GetOrParse_AccessRefreshesRecency()
    {
        var cache = new ParsedDocumentCache<Artifact>(2);

        var a = cache.GetOrParse("file:///a.xml", "<A/>", 1, Parse);
        _ = cache.GetOrParse("file:///b.xml", "<B/>", 2, Parse);
        _ = cache.GetOrParse("file:///a.xml", "<A/>", 1, Parse); // touch a → b becomes LRU
        _ = cache.GetOrParse("file:///c.xml", "<C/>", 3, Parse); // evicts b, not a

        Assert.Same(a, cache.GetOrParse("file:///a.xml", "<A/>", 1, Parse));
    }

    // ── disabled cache ───────────────────────────────────────────────────────

    [Fact]
    public void GetOrParse_ZeroCapacity_AlwaysParsesAndStoresNothing()
    {
        var cache = new ParsedDocumentCache<Artifact>(0);
        var parses = 0;
        Artifact CountingParse(string text)
        {
            parses++;
            return Parse(text);
        }

        var first = cache.GetOrParse("file:///a.xml", "<A/>", 1, CountingParse);
        var second = cache.GetOrParse("file:///a.xml", "<A/>", 1, CountingParse);

        Assert.NotSame(first, second);
        Assert.Equal(2, parses);
    }

    // ── statistics ───────────────────────────────────────────────────────────

    [Fact]
    public void Statistics_TrackHitsMissesEvictions()
    {
        var cache = new ParsedDocumentCache<Artifact>(1);

        _ = cache.GetOrParse("file:///a.xml", "<A/>", 1, Parse); // miss
        _ = cache.GetOrParse("file:///a.xml", "<A/>", 1, Parse); // hit
        _ = cache.GetOrParse("file:///b.xml", "<B/>", 2, Parse); // miss + evicts a

        var (hits, misses, evictions) = cache.Statistics;
        Assert.Equal(1, hits);
        Assert.Equal(2, misses);
        Assert.Equal(1, evictions);
    }

    // ── observability ────────────────────────────────────────────────────────

    [Fact]
    public void GetOrParse_Every256Operations_LogsStatisticsAtDebug()
    {
        var logger = new CapturingLogger();
        var cache = new ParsedDocumentCache<Artifact>(4, "test", logger);

        for (var i = 0; i < 256; i++)
            _ = cache.GetOrParse("file:///a.xml", "<A/>", 1, Parse);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Debug, entry.Level);
        Assert.Contains("test", entry.Message);
        // The log fires on the 256th call before that call's own lookup, so it reflects the 255
        // completed operations: 1 initial miss + 254 hits.
        Assert.Contains("254 hits", entry.Message);
        Assert.Contains("1 misses", entry.Message);
    }

    [Fact]
    public void GetOrParse_FewerThan256Operations_LogsNothing()
    {
        var logger = new CapturingLogger();
        var cache = new ParsedDocumentCache<Artifact>(4, "test", logger);

        for (var i = 0; i < 255; i++)
            _ = cache.GetOrParse("file:///a.xml", "<A/>", 1, Parse);

        Assert.Empty(logger.Entries);
    }

    private sealed class CapturingLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            lock (Entries)
            {
                Entries.Add((logLevel, formatter(state, exception)));
            }
        }
    }

    // ── concurrency ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetOrParse_ConcurrentCallersForSameContent_ConvergeOnOneInstance()
    {
        var cache = new ParsedDocumentCache<Artifact>(4);
        const string text = "<Shared/>";
        var hash = ContentHasher.Hash(text);

        var results = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(() =>
            cache.GetOrParse("file:///a.xml", text, hash, Parse))));

        // Duplicate parses under contention are tolerated, but every caller must receive the same
        // instance once one is stored — consumers may rely on artifact identity.
        Assert.All(results, r => Assert.Same(results[0], r));
        Assert.Same(results[0], cache.GetOrParse("file:///a.xml", text, hash, Parse));
    }
}

// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;

namespace PG.StarWarsGame.LSP.Core.Caching;

/// <summary>
///     Bounded LRU cache for per-document parse artifacts, keyed by canonical URI and validated by
///     content hash. Invalidation is automatic and total: a changed text carries a different hash,
///     misses, and replaces the entry — nothing is wired into edit/watcher/reload paths. Parsing
///     runs outside the lock; a rare duplicate parse under contention is tolerated (that is
///     today's baseline cost on every call), but all callers converge on the first stored
///     instance, so consumers may rely on artifact identity for identical content.
/// </summary>
/// <remarks>
///     Capacity 0 disables caching entirely (every call parses, nothing is stored). The bound
///     exists so the cache cannot re-pin the workspace corpus that the open-document-only host
///     deliberately released: open documents stay resident through recency, closed ones age out.
/// </remarks>
public sealed class ParsedDocumentCache<TArtifact> where TArtifact : class
{
    // Statistics are logged at debug every this many GetOrParse calls — frequent enough to be
    // visible in a live session's log, cheap enough to never matter (one masked counter check).
    private const int LogInterval = 256;

    private readonly int _capacity;
    private readonly Dictionary<string, LinkedListNode<Entry>> _entries;
    private readonly object _gate = new();
    private readonly ILogger? _logger;
    private readonly LinkedList<Entry> _lru = [];
    private readonly string _name;
    private long _evictions;
    private long _hits;
    private long _misses;
    private long _operations;

    public ParsedDocumentCache(int capacity, string? name = null, ILogger? logger = null)
    {
        _capacity = capacity;
        _name = name ?? typeof(TArtifact).Name;
        _logger = logger;
        _entries = new Dictionary<string, LinkedListNode<Entry>>(StringComparer.Ordinal);
    }

    public (long Hits, long Misses, long Evictions) Statistics
    {
        get
        {
            lock (_gate)
            {
                return (_hits, _misses, _evictions);
            }
        }
    }

    public TArtifact GetOrParse(string canonicalUri, string text, long contentHash,
        Func<string, TArtifact> parse)
    {
        if (_capacity <= 0)
            return parse(text);

        if (_logger is not null && (Interlocked.Increment(ref _operations) & (LogInterval - 1)) == 0)
        {
            var (hits, misses, evictions) = Statistics;
            _logger.LogDebug("{Name} parse cache: {Hits} hits, {Misses} misses, {Evictions} evictions",
                _name, hits, misses, evictions);
        }

        lock (_gate)
        {
            if (_entries.TryGetValue(canonicalUri, out var node) && node.Value.ContentHash == contentHash)
            {
                Touch(node);
                _hits++;
                return node.Value.Artifact;
            }
        }

        var artifact = parse(text); // potentially slow — never under the lock

        lock (_gate)
        {
            if (_entries.TryGetValue(canonicalUri, out var node))
            {
                if (node.Value.ContentHash == contentHash)
                {
                    // A concurrent caller stored the same content while we parsed — adopt its
                    // artifact so identical content always yields one instance.
                    Touch(node);
                    _hits++;
                    return node.Value.Artifact;
                }

                _lru.Remove(node);
                _entries.Remove(canonicalUri);
            }

            var newNode = _lru.AddFirst(new Entry(canonicalUri, contentHash, artifact));
            _entries[canonicalUri] = newNode;
            _misses++;

            if (_entries.Count > _capacity)
            {
                var oldest = _lru.Last!;
                _entries.Remove(oldest.Value.CanonicalUri);
                _lru.RemoveLast();
                _evictions++;
            }

            return artifact;
        }
    }

    private void Touch(LinkedListNode<Entry> node)
    {
        if (!ReferenceEquals(_lru.First, node))
        {
            _lru.Remove(node);
            _lru.AddFirst(node);
        }
    }

    private sealed record Entry(string CanonicalUri, long ContentHash, TArtifact Artifact);
}

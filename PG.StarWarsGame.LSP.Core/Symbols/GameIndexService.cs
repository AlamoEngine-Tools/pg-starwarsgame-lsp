// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using Microsoft.Extensions.Logging;

namespace PG.StarWarsGame.LSP.Core.Symbols;

public sealed class GameIndexService : IGameIndexService
{
    private readonly ILogger<GameIndexService> _logger;
    private readonly IEnumerable<IGameDocumentParser> _parsers;
    private GameIndex _current = GameIndex.Empty;
    private int _hasPendingEvent; // 0 = false, 1 = true; int for Interlocked
    private int _suppressionDepth;

    public GameIndexService(IEnumerable<IGameDocumentParser> parsers, ILogger<GameIndexService> logger)
    {
        _parsers = parsers;
        _logger = logger;
    }

    public GameIndex Current => Volatile.Read(ref _current);

    public event Action<GameIndex>? IndexChanged;

    public IDisposable BeginBulkUpdate()
    {
        Interlocked.Increment(ref _suppressionDepth);
        return new BulkUpdateScope(this);
    }

    public async Task UpdateDocumentAsync(string uri, string text, int version, CancellationToken ct)
    {
        uri = NormalizeUri(uri);
        var parser = _parsers.FirstOrDefault(p => p.CanParse(Path.GetExtension(uri)));
        if (parser is null) return;

        // Parse outside the CAS loop — potentially slow, must not hold a lock.
        var newDoc = await parser.ParseAsync(uri, text, version, ct);

        GameIndex snapshot, updated;
        do
        {
            snapshot = Volatile.Read(ref _current);

            // Drop stale parses: another task may have committed a newer version already.
            var existing = snapshot.Documents.GetValueOrDefault(uri);
            if (existing is not null && existing.Version >= newDoc.Version)
            {
                _logger.LogDebug("Dropping stale parse for {Uri} v{Incoming} (committed v{Current})", uri,
                    newDoc.Version, existing.Version);
                return;
            }

            updated = ApplyDocumentIndex(snapshot, newDoc);
        } while (Interlocked.CompareExchange(ref _current, updated, snapshot) != snapshot);

        _logger.LogDebug("Indexed {Uri} v{Version} ({Symbols} symbols, {Refs} refs)",
            uri, newDoc.Version, newDoc.Symbols.Length, newDoc.References.Length);
        RaiseIndexChanged(Volatile.Read(ref _current));
    }

    public void RemoveDocument(string uri)
    {
        uri = NormalizeUri(uri);
        GameIndex snapshot, updated;
        do
        {
            snapshot = Volatile.Read(ref _current);
            if (!snapshot.Documents.ContainsKey(uri)) return;
            updated = StripDocumentFromIndex(snapshot, uri);
        } while (Interlocked.CompareExchange(ref _current, updated, snapshot) != snapshot);

        _logger.LogDebug("Removed document {Uri} from index", uri);
        RaiseIndexChanged(Volatile.Read(ref _current));
    }

    public void ApplyBaseline(BaselineIndex baseline)
    {
        GameIndex snapshot, updated;
        do
        {
            snapshot = Volatile.Read(ref _current);
            updated = snapshot with { Baseline = baseline };
        } while (Interlocked.CompareExchange(ref _current, updated, snapshot) != snapshot);

        _logger.LogInformation("Applied baseline: {Count} symbols, built {BuiltAt}", baseline.Symbols.Count,
            baseline.BuiltAt);
        RaiseIndexChanged(Volatile.Read(ref _current));
    }

    private void EndBulkUpdate()
    {
        if (Interlocked.Decrement(ref _suppressionDepth) > 0)
            return;
        if (Interlocked.Exchange(ref _hasPendingEvent, 0) != 0)
            RaiseIndexChanged(Volatile.Read(ref _current));
    }

    private void RaiseIndexChanged(GameIndex index)
    {
        if (Volatile.Read(ref _suppressionDepth) > 0)
        {
            Interlocked.Exchange(ref _hasPendingEvent, 1);
            return;
        }

        IndexChanged?.Invoke(index);
    }

    private static GameIndex ApplyDocumentIndex(GameIndex index, DocumentIndex doc)
    {
        // Strip the previous version of this document before applying the new one.
        var base_ = index.Documents.ContainsKey(doc.DocumentUri)
            ? StripDocumentFromIndex(index, doc.DocumentUri)
            : index;

        var defs = base_.WorkspaceDefinitions;
        foreach (var sym in doc.Symbols)
            defs = defs.TryGetValue(sym.Id, out var arr)
                ? defs.SetItem(sym.Id, arr.Add(sym))
                : defs.Add(sym.Id, ImmutableArray.Create(sym));

        var refs = base_.WorkspaceReferences;
        foreach (var reference in doc.References)
            refs = refs.TryGetValue(reference.TargetId, out var arr)
                ? refs.SetItem(reference.TargetId, arr.Add(reference))
                : refs.Add(reference.TargetId, ImmutableArray.Create(reference));

        return base_ with
        {
            Documents = base_.Documents.SetItem(doc.DocumentUri, doc),
            WorkspaceDefinitions = defs,
            WorkspaceReferences = refs
        };
    }

    private static GameIndex StripDocumentFromIndex(GameIndex index, string uri)
    {
        if (!index.Documents.TryGetValue(uri, out var existing))
            return index;

        var defs = index.WorkspaceDefinitions;
        foreach (var sym in existing.Symbols)
        {
            if (!defs.TryGetValue(sym.Id, out var arr)) continue;
            var trimmed = arr.Remove(sym);
            defs = trimmed.IsEmpty ? defs.Remove(sym.Id) : defs.SetItem(sym.Id, trimmed);
        }

        var refs = index.WorkspaceReferences;
        foreach (var reference in existing.References)
        {
            if (!refs.TryGetValue(reference.TargetId, out var arr)) continue;
            var trimmed = arr.Remove(reference);
            refs = trimmed.IsEmpty ? refs.Remove(reference.TargetId) : refs.SetItem(reference.TargetId, trimmed);
        }

        return index with
        {
            Documents = index.Documents.Remove(uri),
            WorkspaceDefinitions = defs,
            WorkspaceReferences = refs
        };
    }

    private static string NormalizeUri(string uri)
    {
        if (uri.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            return uri;
        var forward = uri.Replace('\\', '/');
        var prefix = forward.StartsWith('/') ? "file://" : "file:///";
        return prefix + forward;
    }

    private sealed class BulkUpdateScope : IDisposable
    {
        private readonly GameIndexService _owner;
        private int _disposed;

        public BulkUpdateScope(GameIndexService owner)
        {
            _owner = owner;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                _owner.EndBulkUpdate();
        }
    }
}
// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using PG.StarWarsGame.LSP.Core.Localisation;
using PG.StarWarsGame.LSP.Core.Util;

namespace PG.StarWarsGame.LSP.Core.Symbols;

public sealed class GameIndexService : IGameIndexService
{
    private readonly IFileHelper _fileHelper;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _inflightCts = new();
    private readonly ILogger<GameIndexService> _logger;
    private readonly IEnumerable<IGameDocumentParser> _parsers;
    private GameIndex _current = GameIndex.Empty;
    private int _hasPendingEvent; // 0 = false, 1 = true; int for Interlocked
    private int _suppressionDepth;

    public GameIndexService(IFileHelper fileHelper, IEnumerable<IGameDocumentParser> parsers,
        ILogger<GameIndexService> logger)
    {
        _fileHelper = fileHelper;
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

        // Cancel any in-flight parse for this URI and register the new one.
        // AddOrUpdate is atomic: the prior CTS is cancelled before the new one is stored.
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _inflightCts.AddOrUpdate(
            uri,
            _ => cts,
            (_, prior) =>
            {
                prior.Cancel();
                return cts;
            });

        // Parse outside the CAS loop — potentially slow, must not hold a lock.
        DocumentIndex newDoc;
        try
        {
            newDoc = await parser.ParseAsync(uri, text, version, cts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Cancelled internally by a newer edit for the same URI — silently discard.
            return;
        }
        finally
        {
            // Remove our slot only if it hasn't been replaced by a newer edit.
            _inflightCts.TryRemove(new KeyValuePair<string, CancellationTokenSource>(uri, cts));
            cts.Dispose();
        }

        GameIndex snapshot, updated;
        do
        {
            snapshot = Volatile.Read(ref _current);

            // Drop stale parses: a strictly newer version has already been committed.
            // Using strict > (not >=) so that re-opening the same file at the same version
            // (e.g. DidOpen after a workspace scan at v0, then again at v1) still fires
            // IndexChanged and re-publishes diagnostics.
            var existing = snapshot.Documents.GetValueOrDefault(uri);
            if (existing is not null && existing.Version > newDoc.Version)
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

    public void ApplyLocalisation(ILocalisationIndex index)
    {
        GameIndex snapshot, updated;
        do
        {
            snapshot = Volatile.Read(ref _current);
            updated = snapshot with { Localisation = index };
        } while (Interlocked.CompareExchange(ref _current, updated, snapshot) != snapshot);

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

        var groups = base_.WorkspaceGroupMemberships;
        if (!doc.GroupMemberships.IsDefault)
            foreach (var dgm in doc.GroupMemberships)
            {
                var key = dgm.Membership.GroupKey;
                groups = groups.TryGetValue(key, out var arr)
                    ? groups.SetItem(key, arr.Add(dgm.Membership))
                    : groups.Add(key, ImmutableArray.Create(dgm.Membership));
            }

        return base_ with
        {
            Documents = base_.Documents.SetItem(doc.DocumentUri, doc),
            WorkspaceDefinitions = defs,
            WorkspaceReferences = refs,
            WorkspaceGroupMemberships = groups
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

        var groups = index.WorkspaceGroupMemberships;
        if (!existing.GroupMemberships.IsDefault)
            foreach (var dgm in existing.GroupMemberships)
            {
                var key = dgm.Membership.GroupKey;
                if (!groups.TryGetValue(key, out var arr)) continue;
                var trimmed = arr.Remove(dgm.Membership);
                groups = trimmed.IsEmpty ? groups.Remove(key) : groups.SetItem(key, trimmed);
            }

        return index with
        {
            Documents = index.Documents.Remove(uri),
            WorkspaceDefinitions = defs,
            WorkspaceReferences = refs,
            WorkspaceGroupMemberships = groups
        };
    }

    private string NormalizeUri(string uri)
    {
        return _fileHelper.NormalizeUri(uri);
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
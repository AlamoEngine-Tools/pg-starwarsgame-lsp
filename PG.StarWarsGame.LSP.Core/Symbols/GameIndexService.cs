using System.Collections.Immutable;

namespace PG.StarWarsGame.LSP.Core.Symbols;

public sealed class GameIndexService : IGameIndexService
{
    private readonly IEnumerable<IGameDocumentParser> _parsers;
    private GameIndex _current = GameIndex.Empty;

    public GameIndexService(IEnumerable<IGameDocumentParser> parsers)
        => _parsers = parsers;

    public GameIndex Current => Volatile.Read(ref _current);

    public event Action<GameIndex>? IndexChanged;

    public async Task UpdateDocumentAsync(string uri, string text, int version, CancellationToken ct)
    {
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
                return;

            updated = ApplyDocumentIndex(snapshot, newDoc);
        }
        while (Interlocked.CompareExchange(ref _current, updated, snapshot) != snapshot);

        IndexChanged?.Invoke(Volatile.Read(ref _current));
    }

    public void RemoveDocument(string uri)
    {
        GameIndex snapshot, updated;
        do
        {
            snapshot = Volatile.Read(ref _current);
            if (!snapshot.Documents.ContainsKey(uri)) return;
            updated = StripDocumentFromIndex(snapshot, uri);
        }
        while (Interlocked.CompareExchange(ref _current, updated, snapshot) != snapshot);

        IndexChanged?.Invoke(Volatile.Read(ref _current));
    }

    public void ApplyBaseline(BaselineIndex baseline)
    {
        GameIndex snapshot, updated;
        do
        {
            snapshot = Volatile.Read(ref _current);
            updated  = snapshot with { Baseline = baseline };
        }
        while (Interlocked.CompareExchange(ref _current, updated, snapshot) != snapshot);

        IndexChanged?.Invoke(Volatile.Read(ref _current));
    }

    private static GameIndex ApplyDocumentIndex(GameIndex index, DocumentIndex doc)
    {
        // Strip the previous version of this document before applying the new one.
        var base_ = index.Documents.ContainsKey(doc.DocumentUri)
            ? StripDocumentFromIndex(index, doc.DocumentUri)
            : index;

        var defs = base_.WorkspaceDefinitions;
        foreach (var sym in doc.Symbols)
        {
            defs = defs.TryGetValue(sym.Id, out var arr)
                ? defs.SetItem(sym.Id, arr.Add(sym))
                : defs.Add(sym.Id, ImmutableArray.Create(sym));
        }

        var refs = base_.WorkspaceReferences;
        foreach (var reference in doc.References)
        {
            refs = refs.TryGetValue(reference.TargetId, out var arr)
                ? refs.SetItem(reference.TargetId, arr.Add(reference))
                : refs.Add(reference.TargetId, ImmutableArray.Create(reference));
        }

        return base_ with
        {
            Documents            = base_.Documents.SetItem(doc.DocumentUri, doc),
            WorkspaceDefinitions = defs,
            WorkspaceReferences  = refs
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
            Documents            = index.Documents.Remove(uri),
            WorkspaceDefinitions = defs,
            WorkspaceReferences  = refs
        };
    }
}

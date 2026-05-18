using System.Collections.Concurrent;

namespace PG.StarWarsGame.LSP.Core.Workspace;

public sealed class GameWorkspaceHost : IGameWorkspaceHost
{
    private readonly ConcurrentDictionary<string, TrackedDocument> _docs = new();

    public void AddOrUpdate(string uri, string text, int version)
    {
        _docs[uri] = new TrackedDocument(uri, text, version);
    }

    public void Remove(string uri)
    {
        _docs.TryRemove(uri, out _);
    }

    public bool TryGet(string uri, out TrackedDocument doc)
    {
        return _docs.TryGetValue(uri, out doc!);
    }

    public IEnumerable<TrackedDocument> All => _docs.Values;
}
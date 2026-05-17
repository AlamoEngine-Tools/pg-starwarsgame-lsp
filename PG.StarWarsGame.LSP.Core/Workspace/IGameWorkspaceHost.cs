namespace PG.StarWarsGame.LSP.Core.Workspace;

public interface IGameWorkspaceHost
{
    void AddOrUpdate(string uri, string text, int version);
    void Remove(string uri);
    bool TryGet(string uri, out TrackedDocument doc);
    IEnumerable<TrackedDocument> All { get; }
}

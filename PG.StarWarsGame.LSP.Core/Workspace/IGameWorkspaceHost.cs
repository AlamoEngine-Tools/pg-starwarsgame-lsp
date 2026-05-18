namespace PG.StarWarsGame.LSP.Core.Workspace;

public interface IGameWorkspaceHost
{
    IEnumerable<TrackedDocument> All { get; }
    void AddOrUpdate(string uri, string text, int version);
    void Remove(string uri);
    bool TryGet(string uri, out TrackedDocument doc);
}
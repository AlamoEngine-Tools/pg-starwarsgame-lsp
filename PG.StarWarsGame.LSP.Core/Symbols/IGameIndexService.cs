namespace PG.StarWarsGame.LSP.Core.Symbols;

public interface IGameIndexService
{
    GameIndex Current { get; }

    Task UpdateDocumentAsync(string uri, string text, int version, CancellationToken ct);
    void RemoveDocument(string uri);
    void ApplyBaseline(BaselineIndex baseline);

    // Fires on the thread that completes a CAS write. Subscribers must not do heavy work
    // inline; queue a background diagnostics publish instead.
    event Action<GameIndex>? IndexChanged;
}

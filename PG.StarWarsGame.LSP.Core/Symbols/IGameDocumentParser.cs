namespace PG.StarWarsGame.LSP.Core.Symbols;

public interface IGameDocumentParser
{
    bool CanParse(string fileExtension);
    ValueTask<DocumentIndex> ParseAsync(
        string            documentUri,
        string            text,
        int               version,
        CancellationToken ct);
}

namespace PG.StarWarsGame.LSP.Core.Symbols;

public sealed record GameReference(
    string          TargetId,
    GameSymbolKind? ExpectedKind,
    string?         ExpectedTypeName,
    string          DocumentUri,
    int             Line,
    int             Column,
    int             Length
);

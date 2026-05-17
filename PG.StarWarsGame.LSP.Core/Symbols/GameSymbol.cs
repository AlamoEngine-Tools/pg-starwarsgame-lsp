namespace PG.StarWarsGame.LSP.Core.Symbols;

public sealed record GameSymbol(
    string         Id,
    GameSymbolKind Kind,
    string?        TypeName,
    SymbolOrigin   Origin,
    string?        Description
);

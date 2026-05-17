namespace PG.StarWarsGame.LSP.Core.Symbols;

public abstract record SymbolOrigin;

public sealed record FileOrigin(
    string Uri,
    int    Line,
    int?   Column
) : SymbolOrigin;

public sealed record MegArchiveOrigin(
    string ArchivePath,
    string InternalPath,
    int?   Line,
    int?   Column
) : SymbolOrigin;

public sealed record UnknownOrigin(
    string Hint
) : SymbolOrigin;

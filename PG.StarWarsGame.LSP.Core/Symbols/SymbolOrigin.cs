using MessagePack;

namespace PG.StarWarsGame.LSP.Core.Symbols;

[Union(0, typeof(FileOrigin))]
[Union(1, typeof(MegArchiveOrigin))]
[Union(2, typeof(UnknownOrigin))]
[MessagePackObject]
public abstract record SymbolOrigin;

[MessagePackObject]
public sealed record FileOrigin(
    [property: Key(0)] string Uri,
    [property: Key(1)] int    Line,
    [property: Key(2)] int?   Column
) : SymbolOrigin;

[MessagePackObject]
public sealed record MegArchiveOrigin(
    [property: Key(0)] string ArchivePath,
    [property: Key(1)] string InternalPath,
    [property: Key(2)] int?   Line,
    [property: Key(3)] int?   Column
) : SymbolOrigin;

[MessagePackObject]
public sealed record UnknownOrigin(
    [property: Key(0)] string Hint
) : SymbolOrigin;

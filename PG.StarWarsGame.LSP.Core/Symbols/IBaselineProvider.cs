namespace PG.StarWarsGame.LSP.Core.Symbols;

public interface IBaselineProvider
{
    Task<SerializedBaseline?> LoadAsync(CancellationToken ct = default);
}

public record SerializedBaseline
{
    public required string GameVariant { get; init; }
    public required string GameVersion { get; init; }
    public required DateTimeOffset BuildDate { get; init; }
    public required IReadOnlyList<SerializedSymbol> Symbols { get; init; }
}

public record SerializedSymbol
{
    public required string Name { get; init; }
    public required string TypeName { get; init; }
    public required string FilePath { get; init; }
    public int Line { get; init; }
}
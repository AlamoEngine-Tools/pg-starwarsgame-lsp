namespace PG.StarWarsGame.LSP.Core.Completion;

public record ValueProposal
{
    public required string Label { get; init; }
    public string? Detail { get; init; }

    /// <summary>Text inserted on accept; falls back to <see cref="Label" /> when null.</summary>
    public string? InsertText { get; init; }
}
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Assets.Baseline;

/// <summary>Used when BaselineSourceType.None is configured — always returns null.</summary>
public sealed class NullBaselineProvider : IBaselineProvider
{
    public Task<SerializedBaseline?> LoadAsync(CancellationToken ct = default)
    {
        return Task.FromResult<SerializedBaseline?>(null);
    }
}
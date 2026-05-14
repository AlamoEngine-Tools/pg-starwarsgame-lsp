using PG.StarWarsGame.LSP.Assets.Baseline;

namespace PG.StarWarsGame.LSP.Assets.Tests;

public sealed class NullBaselineProviderTests
{
    [Fact]
    public async Task LoadAsync_AlwaysReturnsNull()
    {
        var result = await new NullBaselineProvider().LoadAsync();
        Assert.Null(result);
    }

    [Fact]
    public async Task LoadAsync_WithCancelledToken_StillReturnsNull()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await new NullBaselineProvider().LoadAsync(cts.Token);
        Assert.Null(result);
    }
}
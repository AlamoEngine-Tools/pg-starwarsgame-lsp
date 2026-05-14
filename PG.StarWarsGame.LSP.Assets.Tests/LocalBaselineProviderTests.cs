using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Assets.Baseline;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Assets.Tests;

public sealed class LocalBaselineProviderTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    public LocalBaselineProviderTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempDir, true);
        }
        catch
        {
        }
    }

    private static LocalBaselineProvider Make(FakeConfig config)
    {
        return new LocalBaselineProvider(config, NullLogger<LocalBaselineProvider>.Instance);
    }

    private static SerializedBaseline MakeBaseline()
    {
        return new SerializedBaseline
        {
            GameVariant = "EaW",
            GameVersion = "1.0",
            BuildDate = DateTimeOffset.UtcNow,
            Symbols = [new SerializedSymbol { Name = "A", TypeName = "T", FilePath = "f.xml", Line = 1 }]
        };
    }

    [Fact]
    public async Task Load_ValidJsonFile_ReturnsBaseline()
    {
        var path = Path.Combine(_tempDir, "baseline.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(MakeBaseline()));

        var result = await Make(new FakeConfig { LocalPath = path }).LoadAsync();

        Assert.NotNull(result);
        Assert.Equal("EaW", result!.GameVariant);
    }

    [Fact]
    public async Task Load_NullLocalPath_ReturnsNull()
    {
        Assert.Null(await Make(new FakeConfig { LocalPath = null }).LoadAsync());
    }

    [Fact]
    public async Task Load_WhitespacePath_ReturnsNull()
    {
        Assert.Null(await Make(new FakeConfig { LocalPath = "   " }).LoadAsync());
    }

    [Fact]
    public async Task Load_FileMissing_ReturnsNull()
    {
        Assert.Null(await Make(new FakeConfig
            { LocalPath = Path.Combine(_tempDir, "no-such-file.json") }).LoadAsync());
    }

    [Fact]
    public async Task Load_SymbolsDeserializedCorrectly()
    {
        var path = Path.Combine(_tempDir, "baseline.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(MakeBaseline()));

        var result = await Make(new FakeConfig { LocalPath = path }).LoadAsync();

        var sym = Assert.Single(result!.Symbols);
        Assert.Equal("A", sym.Name);
        Assert.Equal("T", sym.TypeName);
        Assert.Equal("f.xml", sym.FilePath);
        Assert.Equal(1, sym.Line);
    }

    private sealed class FakeConfig : ILspConfigurationProvider
    {
        public string? LocalPath { get; set; }

        public LspConfiguration Current => new()
        {
            BaselineSource = new BaselineSourceConfig
            {
                Type = LocalPath is null ? BaselineSourceType.None : BaselineSourceType.Local,
                LocalPath = LocalPath
            }
        };

        public void LoadFrom(object? _)
        {
        }
    }
}
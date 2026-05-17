using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Assets.Tests;

public sealed class BaselinePopulatorTests
{
    private static SerializedBaseline MakeBaseline(params SerializedSymbol[] symbols)
    {
        return new SerializedBaseline
        {
            GameVariant = "EaW",
            GameVersion = "1.0",
            BuildDate = DateTimeOffset.UtcNow,
            Symbols = symbols
        };
    }

    [Fact]
    public void Populate_ValidSymbolIndex_AddsAllSymbols()
    {
        var index = new SymbolIndex();
        var populator = new BaselinePopulator(NullLogger<BaselinePopulator>.Instance);

        populator.PopulateFromBaseline(index, MakeBaseline(
            new SerializedSymbol { Name = "Alpha", TypeName = "Unit", FilePath = "a.xml", Line = 1 },
            new SerializedSymbol { Name = "Beta", TypeName = "Faction", FilePath = "b.xml", Line = 5 },
            new SerializedSymbol { Name = "Gamma", TypeName = "Unit", FilePath = "c.xml", Line = 9 }
        ));

        Assert.Equal(3, index.All.Count);
    }

    [Fact]
    public void Populate_SymbolPropertiesMappedCorrectly()
    {
        var index = new SymbolIndex();
        var populator = new BaselinePopulator(NullLogger<BaselinePopulator>.Instance);

        populator.PopulateFromBaseline(index, MakeBaseline(
            new SerializedSymbol { Name = "Darth_Vader", TypeName = "Infantry", FilePath = "units.xml", Line = 42 }
        ));

        var sym = Assert.Single(index.All);
        Assert.Equal("Darth_Vader", sym.Name);
        Assert.Equal("Infantry", sym.TypeName);
        Assert.Equal("units.xml", sym.Location.FilePath);
        Assert.Equal(42, sym.Location.Line);
    }

    [Fact]
    public void Populate_EmptyBaseline_IndexUnchanged()
    {
        var index = new SymbolIndex();
        var populator = new BaselinePopulator(NullLogger<BaselinePopulator>.Instance);

        populator.PopulateFromBaseline(index, MakeBaseline());

        Assert.Empty(index.All);
    }

    [Fact]
    public void Populate_NonSymbolIndex_ThrowsArgumentException()
    {
        var fakeIndex = new FakeSymbolIndex();
        var populator = new BaselinePopulator(NullLogger<BaselinePopulator>.Instance);

        var ex = Assert.Throws<ArgumentException>(() => populator.PopulateFromBaseline(fakeIndex, MakeBaseline()));

        Assert.Equal("index", ex.ParamName);
    }

    private sealed class FakeSymbolIndex : ISymbolIndex
    {
        public IReadOnlyList<IndexedSymbol> Lookup(string name)
        {
            return [];
        }

        public IReadOnlyList<IndexedSymbol> LookupByType(string typeName)
        {
            return [];
        }

        public IReadOnlyList<IndexedSymbol> All => [];
    }
}
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Assets.Tests;

public sealed class SymbolIndexTests
{
    private static GameSymbol Symbol(string name, string type = "Unit", string file = "", int line = 0)
    {
        return new GameSymbol { Name = name, TypeName = type, Location = new SymbolLocation(file, line) };
    }

    [Fact]
    public void FreshIndex_IsEmpty()
    {
        var index = new SymbolIndex();
        Assert.Empty(index.All);
    }

    [Fact]
    public void Lookup_ByName_ReturnsMatch()
    {
        var index = new SymbolIndex();
        var sym = Symbol("Darth_Vader");
        index.Add(sym);

        var result = index.Lookup("Darth_Vader");
        Assert.Single(result);
        Assert.Equal("Darth_Vader", result[0].Name);
    }

    [Fact]
    public void Lookup_CaseInsensitive()
    {
        var index = new SymbolIndex();
        index.Add(Symbol("Darth_Vader"));

        Assert.Single(index.Lookup("darth_vader"));
        Assert.Single(index.Lookup("DARTH_VADER"));
    }

    [Fact]
    public void Lookup_Unknown_ReturnsEmpty()
    {
        var index = new SymbolIndex();
        Assert.Empty(index.Lookup("Unknown"));
    }

    [Fact]
    public void Lookup_MultipleWithSameName_ReturnsAll()
    {
        var index = new SymbolIndex();
        index.Add(Symbol("Shared", "TypeA"));
        index.Add(Symbol("Shared", "TypeB"));

        Assert.Equal(2, index.Lookup("Shared").Count);
    }

    [Fact]
    public void LookupByType_ReturnsMatch()
    {
        var index = new SymbolIndex();
        index.Add(Symbol("A", "SpaceUnit"));
        index.Add(Symbol("B", "Infantry"));

        Assert.Single(index.LookupByType("SpaceUnit"));
    }

    [Fact]
    public void LookupByType_CaseInsensitive()
    {
        var index = new SymbolIndex();
        index.Add(Symbol("A", "SpaceUnit"));

        Assert.Single(index.LookupByType("spaceunit"));
        Assert.Single(index.LookupByType("SPACEUNIT"));
    }

    [Fact]
    public void LookupByType_Unknown_ReturnsEmpty()
    {
        var index = new SymbolIndex();
        Assert.Empty(index.LookupByType("Nonexistent"));
    }

    [Fact]
    public void All_ContainsAllAddedSymbols()
    {
        var index = new SymbolIndex();
        index.Add(Symbol("A"));
        index.Add(Symbol("B"));
        index.Add(Symbol("C"));

        Assert.Equal(3, index.All.Count);
    }
}
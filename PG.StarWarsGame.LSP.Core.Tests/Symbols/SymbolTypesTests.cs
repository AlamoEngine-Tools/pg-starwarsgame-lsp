using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Core.Tests.Symbols;

public sealed class SymbolTypesTests
{
    // ── SymbolOrigin ────────────────────────────────────────────────────────

    [Fact]
    public void FileOrigin_Stores_Uri_Line_And_Optional_Column()
    {
        var origin = new FileOrigin("file:///foo.xml", 10, 5);
        Assert.Equal("file:///foo.xml", origin.Uri);
        Assert.Equal(10, origin.Line);
        Assert.Equal(5, origin.Column);
    }

    [Fact]
    public void FileOrigin_Column_Can_Be_Null()
    {
        var origin = new FileOrigin("file:///foo.xml", 1, null);
        Assert.Null(origin.Column);
    }

    [Fact]
    public void MegArchiveOrigin_Stores_Paths_Line_And_Column()
    {
        var origin = new MegArchiveOrigin("DATA.MEG", "XML/UNITS.XML", 7, null);
        Assert.Equal("DATA.MEG", origin.ArchivePath);
        Assert.Equal("XML/UNITS.XML", origin.InternalPath);
        Assert.Equal(7, origin.Line);
        Assert.Null(origin.Column);
    }

    [Fact]
    public void UnknownOrigin_Stores_Hint()
    {
        var origin = new UnknownOrigin("CRC collision survivor");
        Assert.Equal("CRC collision survivor", origin.Hint);
    }

    [Fact]
    public void SymbolOrigin_Subtypes_Are_Distinct()
    {
        SymbolOrigin a = new FileOrigin("file:///a.xml", 1, null);
        SymbolOrigin b = new MegArchiveOrigin("D.MEG", "A.XML", null, null);
        SymbolOrigin c = new UnknownOrigin("hint");

        Assert.IsType<FileOrigin>(a);
        Assert.IsType<MegArchiveOrigin>(b);
        Assert.IsType<UnknownOrigin>(c);
    }

    // ── GameSymbol ──────────────────────────────────────────────────────────

    [Fact]
    public void GameSymbol_Stores_All_Required_Fields()
    {
        var origin = new FileOrigin("file:///foo.xml", 1, null);
        var symbol = new GameSymbol("UNIT_REBEL", GameSymbolKind.XmlObject, "Unit", origin, null);

        Assert.Equal("UNIT_REBEL", symbol.Id);
        Assert.Equal(GameSymbolKind.XmlObject, symbol.Kind);
        Assert.Equal("Unit", symbol.TypeName);
        Assert.Same(origin, symbol.Origin);
        Assert.Null(symbol.Description);
    }

    [Fact]
    public void GameSymbol_TypeName_And_Description_Can_Be_Null()
    {
        var symbol = new GameSymbol("MY_LUA_FN", GameSymbolKind.LuaGlobal, null,
            new FileOrigin("file:///script.lua", 5, null), null);

        Assert.Null(symbol.TypeName);
    }

    [Fact]
    public void GameSymbol_Equality_Is_Value_Based()
    {
        var origin = new FileOrigin("file:///foo.xml", 1, null);
        var a = new GameSymbol("ID", GameSymbolKind.XmlObject, "Unit", origin, null);
        var b = new GameSymbol("ID", GameSymbolKind.XmlObject, "Unit", origin, null);

        Assert.Equal(a, b);
    }

    // ── GameReference ───────────────────────────────────────────────────────

    [Fact]
    public void GameReference_Stores_All_Fields()
    {
        var ref1 = new GameReference("UNIT_REBEL", GameSymbolKind.XmlObject, "Unit",
            "file:///foo.xml", 3, 10, 11);

        Assert.Equal("UNIT_REBEL", ref1.TargetId);
        Assert.Equal(GameSymbolKind.XmlObject, ref1.ExpectedKind);
        Assert.Equal("Unit", ref1.ExpectedTypeName);
        Assert.Equal("file:///foo.xml", ref1.DocumentUri);
        Assert.Equal(3, ref1.Line);
        Assert.Equal(10, ref1.Column);
        Assert.Equal(11, ref1.Length);
    }

    [Fact]
    public void GameReference_ExpectedKind_And_TypeName_Can_Be_Null()
    {
        var ref1 = new GameReference("TARGET", null, null, "file:///foo.xml", 0, 0, 5);
        Assert.Null(ref1.ExpectedKind);
        Assert.Null(ref1.ExpectedTypeName);
    }

    // ── DocumentIndex ───────────────────────────────────────────────────────

    [Fact]
    public void DocumentIndex_Stores_Uri_Version_Symbols_References()
    {
        var symbol = new GameSymbol("A", GameSymbolKind.XmlObject, "Unit",
            new FileOrigin("file:///f.xml", 1, null), null);
        var reference = new GameReference("B", null, null, "file:///f.xml", 2, 0, 1);

        var doc = new DocumentIndex(
            "file:///f.xml", 3,
            ImmutableArray.Create(symbol),
            ImmutableArray.Create(reference));

        Assert.Equal("file:///f.xml", doc.DocumentUri);
        Assert.Equal(3, doc.Version);
        Assert.Single(doc.Symbols);
        Assert.Single(doc.References);
    }

    [Fact]
    public void DocumentIndex_Allows_Empty_Symbols_And_References()
    {
        var doc = new DocumentIndex("file:///empty.xml", 1,
            ImmutableArray<GameSymbol>.Empty,
            ImmutableArray<GameReference>.Empty);

        Assert.Empty(doc.Symbols);
        Assert.Empty(doc.References);
    }
}

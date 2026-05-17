using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Assets.Serialization;
using PG.StarWarsGame.LSP.Core.Symbols;

// ReSharper disable SuggestVarOrType_Elsewhere

namespace PG.StarWarsGame.LSP.Assets.Tests.Serialization;

public sealed class BaselineSerializerTests
{
    private static readonly DateTimeOffset TestDate =
        new(2026, 1, 15, 10, 30, 0, TimeSpan.Zero);

    private static GameSymbol Symbol(string id, SymbolOrigin origin, string? typeName = "Unit") =>
        new(id, GameSymbolKind.XmlObject, typeName, origin, null);

    // ── Empty baseline ───────────────────────────────────────────────────────

    [Fact]
    public void RoundTrip_EmptyBaseline()
    {
        var data  = BaselineSerializer.Serialize(BaselineIndex.Empty);
        var result = BaselineSerializer.Deserialize(data);

        Assert.Empty(result.Symbols);
        Assert.Equal(string.Empty, result.SourceManifestHash);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(
            DateTimeOffset.MinValue.ToUnixTimeMilliseconds()), result.BuiltAt);
    }

    // ── SymbolOrigin union variants ──────────────────────────────────────────

    [Fact]
    public void RoundTrip_FileOrigin()
    {
        var sym  = Symbol("UNIT_A", new FileOrigin("file:///data/unit.xml", 10, 5));
        var data = BaselineSerializer.Serialize(Baseline(sym));
        var result = BaselineSerializer.Deserialize(data).Symbols["UNIT_A"];

        var origin = Assert.IsType<FileOrigin>(result.Origin);
        Assert.Equal("file:///data/unit.xml", origin.Uri);
        Assert.Equal(10, origin.Line);
        Assert.Equal(5, origin.Column);
    }

    [Fact]
    public void RoundTrip_MegArchiveOrigin()
    {
        var sym  = Symbol("UNIT_B", new MegArchiveOrigin("DATA.MEG", "DATA/UNITS/B.XML", 3, null));
        var data = BaselineSerializer.Serialize(Baseline(sym));
        var result = BaselineSerializer.Deserialize(data).Symbols["UNIT_B"];

        var origin = Assert.IsType<MegArchiveOrigin>(result.Origin);
        Assert.Equal("DATA.MEG", origin.ArchivePath);
        Assert.Equal("DATA/UNITS/B.XML", origin.InternalPath);
        Assert.Equal(3, origin.Line);
        Assert.Null(origin.Column);
    }

    [Fact]
    public void RoundTrip_UnknownOrigin()
    {
        var sym  = Symbol("UNIT_C", new UnknownOrigin("CRC collision survivor"));
        var data = BaselineSerializer.Serialize(Baseline(sym));
        var result = BaselineSerializer.Deserialize(data).Symbols["UNIT_C"];

        var origin = Assert.IsType<UnknownOrigin>(result.Origin);
        Assert.Equal("CRC collision survivor", origin.Hint);
    }

    // ── Symbol fields ────────────────────────────────────────────────────────

    [Fact]
    public void RoundTrip_AllSymbolFields()
    {
        var sym = new GameSymbol("UNIT_D", GameSymbolKind.XmlObject, "Infantry",
            new FileOrigin("file:///f.xml", 1, null), "A description");
        var result = BaselineSerializer.Deserialize(BaselineSerializer.Serialize(Baseline(sym)))
            .Symbols["UNIT_D"];

        Assert.Equal("UNIT_D",             result.Id);
        Assert.Equal(GameSymbolKind.XmlObject, result.Kind);
        Assert.Equal("Infantry",           result.TypeName);
        Assert.Equal("A description",      result.Description);
    }

    [Fact]
    public void RoundTrip_NullableFields_WhenNull()
    {
        var sym    = Symbol("UNIT_E", new FileOrigin("file:///f.xml", 1, null), typeName: null);
        var result = BaselineSerializer.Deserialize(BaselineSerializer.Serialize(Baseline(sym)))
            .Symbols["UNIT_E"];

        Assert.Null(result.TypeName);
        Assert.Null(result.Description);
        Assert.Null(((FileOrigin)result.Origin).Column);
    }

    // ── Metadata ─────────────────────────────────────────────────────────────

    [Fact]
    public void RoundTrip_MetadataFields()
    {
        var baseline = new BaselineIndex(
            ImmutableDictionary<string, GameSymbol>.Empty,
            TestDate, "abc123hash",
            ImmutableDictionary<string, ImmutableArray<string>>.Empty,
            ImmutableDictionary<string, ImmutableArray<string>>.Empty);

        var result = BaselineSerializer.Deserialize(BaselineSerializer.Serialize(baseline));

        Assert.Equal(TestDate, result.BuiltAt);
        Assert.Equal("abc123hash", result.SourceManifestHash);
    }

    [Fact]
    public void RoundTrip_MultipleSymbols_DictionaryKeyedById()
    {
        var a = Symbol("ALPHA", new FileOrigin("file:///a.xml", 1, null));
        var b = Symbol("BETA",  new FileOrigin("file:///b.xml", 2, null));
        var result = BaselineSerializer.Deserialize(BaselineSerializer.Serialize(Baseline(a, b)));

        Assert.Equal(2, result.Symbols.Count);
        Assert.True(result.Symbols.ContainsKey("ALPHA"));
        Assert.True(result.Symbols.ContainsKey("BETA"));
    }

    // ── DynamicEnumValues ────────────────────────────────────────────────────

    [Fact]
    public void RoundTrip_DynamicEnumValues()
    {
        var baseline = new BaselineIndex(
            ImmutableDictionary<string, GameSymbol>.Empty,
            TestDate, "hash",
            ImmutableDictionary<string, ImmutableArray<string>>.Empty
                .Add("DamageType", ["EXPLOSIVE", "ENERGY", "GRENADE"])
                .Add("ArmorType",  ["ARMOR_INFANTRY", "ARMOR_VEHICLE"]),
            ImmutableDictionary<string, ImmutableArray<string>>.Empty);

        var result = BaselineSerializer.Deserialize(BaselineSerializer.Serialize(baseline));

        Assert.Equal(2, result.DynamicEnumValues.Count);
        Assert.Collection(result.DynamicEnumValues["DamageType"],
            v => Assert.Equal("EXPLOSIVE", v),
            v => Assert.Equal("ENERGY",    v),
            v => Assert.Equal("GRENADE",   v));
        Assert.Collection(result.DynamicEnumValues["ArmorType"],
            v => Assert.Equal("ARMOR_INFANTRY", v),
            v => Assert.Equal("ARMOR_VEHICLE",  v));
    }

    [Fact]
    public void RoundTrip_EmptyDynamicEnumValues()
    {
        var result = BaselineSerializer.Deserialize(BaselineSerializer.Serialize(BaselineIndex.Empty));
        Assert.Empty(result.DynamicEnumValues);
    }

    // ── HardcodedEnumValues ──────────────────────────────────────────────────

    [Fact]
    public void RoundTrip_HardcodedEnumValues()
    {
        var baseline = new BaselineIndex(
            ImmutableDictionary<string, GameSymbol>.Empty,
            TestDate, "hash",
            ImmutableDictionary<string, ImmutableArray<string>>.Empty,
            ImmutableDictionary<string, ImmutableArray<string>>.Empty
                .Add("DamageType", ["EXPLOSIVE", "ENERGY"])
                .Add("ArmorType",  ["ARMOR_INFANTRY"]));

        var result = BaselineSerializer.Deserialize(BaselineSerializer.Serialize(baseline));

        Assert.Equal(2, result.HardcodedEnumValues.Count);
        Assert.Collection(result.HardcodedEnumValues["DamageType"],
            v => Assert.Equal("EXPLOSIVE", v),
            v => Assert.Equal("ENERGY",    v));
        Assert.Collection(result.HardcodedEnumValues["ArmorType"],
            v => Assert.Equal("ARMOR_INFANTRY", v));
    }

    [Fact]
    public void RoundTrip_EmptyHardcodedEnumValues()
    {
        var result = BaselineSerializer.Deserialize(BaselineSerializer.Serialize(BaselineIndex.Empty));
        Assert.Empty(result.HardcodedEnumValues);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static BaselineIndex Baseline(params GameSymbol[] symbols) =>
        new(symbols.ToImmutableDictionary(s => s.Id), TestDate, "hash",
            ImmutableDictionary<string, ImmutableArray<string>>.Empty,
            ImmutableDictionary<string, ImmutableArray<string>>.Empty);
}

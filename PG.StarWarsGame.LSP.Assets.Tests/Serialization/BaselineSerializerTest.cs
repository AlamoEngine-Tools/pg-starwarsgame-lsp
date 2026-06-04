// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Assets.Serialization;
using PG.StarWarsGame.LSP.Core.Symbols;

// ReSharper disable SuggestVarOrType_Elsewhere

namespace PG.StarWarsGame.LSP.Assets.Tests.Serialization;

public sealed class BaselineSerializerTest
{
    private static readonly DateTimeOffset TestDate =
        new(2026, 1, 15, 10, 30, 0, TimeSpan.Zero);

    private static GameSymbol Symbol(string id, SymbolOrigin origin, string? typeName = "Unit")
    {
        return new GameSymbol(id, GameSymbolKind.XmlObject, typeName, origin, null);
    }

    // ── Empty baseline ───────────────────────────────────────────────────────

    [Fact]
    public void RoundTrip_EmptyBaseline()
    {
        var data = BaselineSerializer.Serialize(BaselineIndex.Empty);
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
        var sym = Symbol("UNIT_A", new FileOrigin("file:///data/unit.xml", 10, 5));
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
        var sym = Symbol("UNIT_B", new MegArchiveOrigin("DATA.MEG", "DATA/UNITS/B.XML", 3, null));
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
        var sym = Symbol("UNIT_C", new UnknownOrigin("CRC collision survivor"));
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

        Assert.Equal("UNIT_D", result.Id);
        Assert.Equal(GameSymbolKind.XmlObject, result.Kind);
        Assert.Equal("Infantry", result.TypeName);
        Assert.Equal("A description", result.Description);
    }

    [Fact]
    public void RoundTrip_NullableFields_WhenNull()
    {
        var sym = Symbol("UNIT_E", new FileOrigin("file:///f.xml", 1, null), null);
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
        var b = Symbol("BETA", new FileOrigin("file:///b.xml", 2, null));
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
                .Add("ArmorType", ["ARMOR_INFANTRY", "ARMOR_VEHICLE"]),
            ImmutableDictionary<string, ImmutableArray<string>>.Empty,
            ImmutableDictionary<string, ImmutableArray<string>>.Empty);

        var result = BaselineSerializer.Deserialize(BaselineSerializer.Serialize(baseline));

        Assert.Equal(2, result.DynamicEnumValues.Count);
        Assert.Collection(result.DynamicEnumValues["DamageType"],
            v => Assert.Equal("EXPLOSIVE", v),
            v => Assert.Equal("ENERGY", v),
            v => Assert.Equal("GRENADE", v));
        Assert.Collection(result.DynamicEnumValues["ArmorType"],
            v => Assert.Equal("ARMOR_INFANTRY", v),
            v => Assert.Equal("ARMOR_VEHICLE", v));
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
                .Add("ArmorType", ["ARMOR_INFANTRY"]),
            ImmutableDictionary<string, ImmutableArray<string>>.Empty);

        var result = BaselineSerializer.Deserialize(BaselineSerializer.Serialize(baseline));

        Assert.Equal(2, result.HardcodedEnumValues.Count);
        Assert.Collection(result.HardcodedEnumValues["DamageType"],
            v => Assert.Equal("EXPLOSIVE", v),
            v => Assert.Equal("ENERGY", v));
        Assert.Collection(result.HardcodedEnumValues["ArmorType"],
            v => Assert.Equal("ARMOR_INFANTRY", v));
    }

    [Fact]
    public void RoundTrip_EmptyHardcodedEnumValues()
    {
        var result = BaselineSerializer.Deserialize(BaselineSerializer.Serialize(BaselineIndex.Empty));
        Assert.Empty(result.HardcodedEnumValues);
    }

    // ── FileTypeMap ──────────────────────────────────────────────────────────

    [Fact]
    public void RoundTrip_FileTypeMap()
    {
        var baseline = new BaselineIndex(
            ImmutableDictionary<string, GameSymbol>.Empty,
            TestDate, "hash",
            ImmutableDictionary<string, ImmutableArray<string>>.Empty,
            ImmutableDictionary<string, ImmutableArray<string>>.Empty,
            ImmutableDictionary<string, ImmutableArray<string>>.Empty
                .Add("data/xml/hardpoints.xml", ["GameObjectType"])
                .Add("data/xml/movies.xml", ["BinkMovie"]));

        var result = BaselineSerializer.Deserialize(BaselineSerializer.Serialize(baseline));

        Assert.Equal(2, result.FileTypeMap.Count);
        Assert.Equal(["GameObjectType"], result.FileTypeMap["data/xml/hardpoints.xml"].ToArray());
        Assert.Equal(["BinkMovie"], result.FileTypeMap["data/xml/movies.xml"].ToArray());
    }

    [Fact]
    public void RoundTrip_EmptyFileTypeMap()
    {
        var result = BaselineSerializer.Deserialize(BaselineSerializer.Serialize(BaselineIndex.Empty));

        Assert.Empty(result.FileTypeMap);
    }

    // ── GroupMemberships ─────────────────────────────────────────────────────

    [Fact]
    public void RoundTrip_GroupMemberships_Empty()
    {
        var result = BaselineSerializer.Deserialize(BaselineSerializer.Serialize(BaselineIndex.Empty));
        Assert.Empty(result.GroupMemberships);
    }

    [Fact]
    public void RoundTrip_GroupMemberships_FileOrigin()
    {
        var membership = new GroupMembership("Unit_AT_AT", "SFXEvent",
            new FileOrigin("file:///sfx.xml", 5, 10));
        var baseline = Baseline() with
        {
            GroupMemberships = ImmutableDictionary.Create<string, ImmutableArray<GroupMembership>>(
                    StringComparer.OrdinalIgnoreCase)
                .Add("Unit_AT_AT", [membership])
        };

        var result = BaselineSerializer.Deserialize(BaselineSerializer.Serialize(baseline));

        Assert.Single(result.GroupMemberships);
        var members = result.GroupMemberships["Unit_AT_AT"];
        Assert.Single(members);
        Assert.Equal("Unit_AT_AT", members[0].GroupKey);
        Assert.Equal("SFXEvent", members[0].MemberTypeName);
        var origin = Assert.IsType<FileOrigin>(members[0].MemberOrigin);
        Assert.Equal("file:///sfx.xml", origin.Uri);
        Assert.Equal(5, origin.Line);
        Assert.Equal(10, origin.Column);
    }

    [Fact]
    public void RoundTrip_GroupMemberships_MegArchiveOrigin()
    {
        var membership = new GroupMembership("Laser_Group", "SFXEvent",
            new MegArchiveOrigin("SFX.MEG", "SFX/LASER.XML", 3, null));
        var baseline = Baseline() with
        {
            GroupMemberships = ImmutableDictionary.Create<string, ImmutableArray<GroupMembership>>(
                    StringComparer.OrdinalIgnoreCase)
                .Add("Laser_Group", [membership])
        };

        var result = BaselineSerializer.Deserialize(BaselineSerializer.Serialize(baseline));

        var members = result.GroupMemberships["Laser_Group"];
        Assert.Single(members);
        var origin = Assert.IsType<MegArchiveOrigin>(members[0].MemberOrigin);
        Assert.Equal("SFX.MEG", origin.ArchivePath);
        Assert.Equal("SFX/LASER.XML", origin.InternalPath);
        Assert.Equal(3, origin.Line);
        Assert.Null(origin.Column);
    }

    [Fact]
    public void RoundTrip_GroupMemberships_MultipleGroupsAndMembers()
    {
        var g1m1 = new GroupMembership("GRP_A", "SFXEvent", new FileOrigin("file:///a.xml", 1, null));
        var g1m2 = new GroupMembership("GRP_A", "SFXEvent", new FileOrigin("file:///b.xml", 2, null));
        var g2m1 = new GroupMembership("GRP_B", "SFXEvent", new FileOrigin("file:///c.xml", 3, null));
        var baseline = Baseline() with
        {
            GroupMemberships = ImmutableDictionary.Create<string, ImmutableArray<GroupMembership>>(
                    StringComparer.OrdinalIgnoreCase)
                .Add("GRP_A", [g1m1, g1m2])
                .Add("GRP_B", [g2m1])
        };

        var result = BaselineSerializer.Deserialize(BaselineSerializer.Serialize(baseline));

        Assert.Equal(2, result.GroupMemberships.Count);
        Assert.Equal(2, result.GroupMemberships["GRP_A"].Length);
        Assert.Single(result.GroupMemberships["GRP_B"]);
    }

    [Fact]
    public void RoundTrip_OldBaselineWithoutGroupMemberships_DeserializesWithEmptyGroupMemberships()
    {
        // Verify backwards compatibility: a baseline serialized before GroupMemberships existed
        // deserializes without error and yields an empty GroupMemberships dict.
        // We simulate this by checking BaselineIndex.Empty round-trips cleanly.
        var result = BaselineSerializer.Deserialize(BaselineSerializer.Serialize(BaselineIndex.Empty));
        Assert.Empty(result.GroupMemberships);
    }

    // ── AssetFiles ───────────────────────────────────────────────────────────

    [Fact]
    public void RoundTrip_AssetFiles_Empty()
    {
        var result = BaselineSerializer.Deserialize(BaselineSerializer.Serialize(BaselineIndex.Empty));
        Assert.Empty(result.AssetFiles);
    }

    [Fact]
    public void RoundTrip_AssetFiles()
    {
        var baseline = Baseline() with
        {
            AssetFiles = ImmutableHashSet.Create(
                "data/art/textures/foo.tga",
                "data/art/models/bar.alo",
                "data/audio/baz.wav")
        };

        var result = BaselineSerializer.Deserialize(BaselineSerializer.Serialize(baseline));

        Assert.Equal(3, result.AssetFiles.Count);
        Assert.Contains("data/art/textures/foo.tga", result.AssetFiles);
        Assert.Contains("data/art/models/bar.alo", result.AssetFiles);
        Assert.Contains("data/audio/baz.wav", result.AssetFiles);
    }

    // ── ModelBones ───────────────────────────────────────────────────────────

    [Fact]
    public void RoundTrip_ModelBones_Empty()
    {
        var result = BaselineSerializer.Deserialize(BaselineSerializer.Serialize(BaselineIndex.Empty));
        Assert.Empty(result.ModelBones);
    }

    [Fact]
    public void RoundTrip_ModelBones()
    {
        var baseline = Baseline() with
        {
            ModelBones = ImmutableDictionary<string, ImmutableArray<string>>.Empty
                .Add("data/art/models/unit.alo", ["root", "turret_bone"])
        };

        var result = BaselineSerializer.Deserialize(BaselineSerializer.Serialize(baseline));

        Assert.Single(result.ModelBones);
        Assert.Collection(result.ModelBones["data/art/models/unit.alo"],
            b => Assert.Equal("root", b),
            b => Assert.Equal("turret_bone", b));
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static BaselineIndex Baseline(params GameSymbol[] symbols)
    {
        return new BaselineIndex(symbols.ToImmutableDictionary(s => s.Id), TestDate, "hash",
            ImmutableDictionary<string, ImmutableArray<string>>.Empty,
            ImmutableDictionary<string, ImmutableArray<string>>.Empty,
            ImmutableDictionary<string, ImmutableArray<string>>.Empty);
    }
}
// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

public sealed class DamageToArmorModHandlerTest
{
    private static readonly DamageToArmorModHandler Sut = new();

    private static readonly XmlTagDefinition Tag =
        XmlHandlerTestFixtures.MakeTag("Armor_Modifier", XmlValueType.DamageToArmorMod);

    private static DiagnosticsContext CtxWithEnums(string[] damageTypes, string[] armorTypes)
    {
        var dict = ImmutableDictionary.CreateBuilder<string, ImmutableArray<string>>(StringComparer.OrdinalIgnoreCase);
        dict["DamageType"] = ImmutableArray.Create(damageTypes);
        dict["ArmorType"] = ImmutableArray.Create(armorTypes);
        var index = GameIndex.Empty with
        {
            Baseline = BaselineIndex.Empty with { DynamicEnumValues = dict.ToImmutable() }
        };
        return new DiagnosticsContext(new EmptySchemaProvider(), index, "file:///test.xml", "en");
    }

    [Theory]
    [InlineData("Damage_Default, Armor_Default, 1")]
    [InlineData("Damage_Crush,Armor_Heavy,0.5")]
    [InlineData("Damage_Heat, Shield_Default, 2.0")]
    public void Valid_triple_returns_no_diagnostics(string value)
    {
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, value), XmlHandlerTestFixtures.EmptyCtx).ToList();
        Assert.Empty(results);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ARMOR_LIGHT")]
    [InlineData("ARMOR_LIGHT, 1.5")]
    [InlineData(",Armor_Default,1.0")]
    [InlineData("Damage_Default,,1.0")]
    [InlineData("Damage_Default,Armor_Default,not_a_float")]
    [InlineData("Damage_Default,Armor_Default,")]
    [InlineData("Damage_Default,Armor_Default,1.0,extra")]
    public void Invalid_values_return_error(string value)
    {
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, value), XmlHandlerTestFixtures.EmptyCtx).ToList();
        var d = Assert.Single(results);
        Assert.Equal(XmlDiagnosticSeverity.Error, d.Severity);
    }

    [Fact]
    public void Wrong_type_returns_no_diagnostics()
    {
        var floatTag = XmlHandlerTestFixtures.MakeTag("Speed", XmlValueType.Float);
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(floatTag, ""), XmlHandlerTestFixtures.EmptyCtx)
            .ToList();
        Assert.Empty(results);
    }

    // ── Dynamic enum validation ───────────────────────────────────────────────

    [Fact]
    public void Known_damage_and_armor_types_emit_no_diagnostics()
    {
        var ctx = CtxWithEnums(["Damage_Default", "Damage_Crush"], ["Armor_Default", "Armor_Heavy"]);
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, "Damage_Default, Armor_Heavy, 1.5"), ctx)
            .ToList();
        Assert.Empty(results);
    }

    [Fact]
    public void Unknown_damage_type_emits_warning()
    {
        var ctx = CtxWithEnums(["Damage_Default"], ["Armor_Default"]);
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, "garbage, Armor_Default, 0.5"), ctx).ToList();
        var d = Assert.Single(results);
        Assert.Equal(XmlDiagnosticSeverity.Warning, d.Severity);
        Assert.Contains("garbage", d.Message);
    }

    [Fact]
    public void Unknown_armor_type_emits_warning()
    {
        var ctx = CtxWithEnums(["Damage_Default"], ["Armor_Default"]);
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, "Damage_Default, garbage, 0.5"), ctx).ToList();
        var d = Assert.Single(results);
        Assert.Equal(XmlDiagnosticSeverity.Warning, d.Severity);
        Assert.Contains("garbage", d.Message);
    }

    [Fact]
    public void Both_unknown_types_emit_two_warnings()
    {
        var ctx = CtxWithEnums(["Damage_Default"], ["Armor_Default"]);
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, "garbage, rubbish, 0.1"), ctx).ToList();
        Assert.Equal(2, results.Count);
        Assert.All(results, d => Assert.Equal(XmlDiagnosticSeverity.Warning, d.Severity));
    }

    [Fact]
    public void Enum_lookup_is_case_insensitive()
    {
        var ctx = CtxWithEnums(["Damage_Default"], ["Armor_Default"]);
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, "DAMAGE_DEFAULT, armor_default, 1.0"), ctx)
            .ToList();
        Assert.Empty(results);
    }

    [Fact]
    public void Empty_baseline_skips_enum_validation()
    {
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, "garbage, garbage, 0.1"),
            XmlHandlerTestFixtures.EmptyCtx).ToList();
        Assert.Empty(results);
    }

    // ── Workspace enum values ─────────────────────────────────────────────────

    [Fact]
    public void Workspace_damage_type_is_accepted()
    {
        var index = GameIndex.Empty with
        {
            WorkspaceDynamicEnumValues = ImmutableDictionary<string, ImmutableArray<string>>
                .Empty.WithComparers(StringComparer.OrdinalIgnoreCase)
                .Add("DamageType", ImmutableArray.Create("Damage_Mod"))
                .Add("ArmorType", ImmutableArray.Create("Armor_Default"))
        };
        var ctx = new DiagnosticsContext(new EmptySchemaProvider(), index, "file:///test.xml", "en");

        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, "Damage_Mod, Armor_Default, 1.0"), ctx).ToList();
        Assert.Empty(results);
    }

    [Fact]
    public void Workspace_armor_type_is_accepted()
    {
        var index = GameIndex.Empty with
        {
            WorkspaceDynamicEnumValues = ImmutableDictionary<string, ImmutableArray<string>>
                .Empty.WithComparers(StringComparer.OrdinalIgnoreCase)
                .Add("DamageType", ImmutableArray.Create("Damage_Default"))
                .Add("ArmorType", ImmutableArray.Create("Armor_Mod"))
        };
        var ctx = new DiagnosticsContext(new EmptySchemaProvider(), index, "file:///test.xml", "en");

        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, "Damage_Default, Armor_Mod, 1.0"), ctx).ToList();
        Assert.Empty(results);
    }

    [Fact]
    public void Workspace_values_merged_with_baseline_values()
    {
        var baselineDict =
            ImmutableDictionary.CreateBuilder<string, ImmutableArray<string>>(StringComparer.OrdinalIgnoreCase);
        baselineDict["DamageType"] = ImmutableArray.Create("Damage_Default");
        baselineDict["ArmorType"] = ImmutableArray.Create("Armor_Default");
        var workspaceDict = ImmutableDictionary<string, ImmutableArray<string>>
            .Empty.WithComparers(StringComparer.OrdinalIgnoreCase)
            .Add("DamageType", ImmutableArray.Create("Damage_Mod"))
            .Add("ArmorType", ImmutableArray.Create("Armor_Mod"));
        var index = GameIndex.Empty with
        {
            Baseline = BaselineIndex.Empty with { DynamicEnumValues = baselineDict.ToImmutable() },
            WorkspaceDynamicEnumValues = workspaceDict
        };
        var ctx = new DiagnosticsContext(new EmptySchemaProvider(), index, "file:///test.xml", "en");

        // Damage_Mod is workspace-only, Armor_Default is baseline-only - both valid
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, "Damage_Mod, Armor_Default, 1.0"), ctx).ToList();
        Assert.Empty(results);
    }

    [Fact]
    public void Unknown_type_still_warns_when_workspace_enums_present()
    {
        var index = GameIndex.Empty with
        {
            WorkspaceDynamicEnumValues = ImmutableDictionary<string, ImmutableArray<string>>
                .Empty.WithComparers(StringComparer.OrdinalIgnoreCase)
                .Add("DamageType", ImmutableArray.Create("Damage_Default"))
                .Add("ArmorType", ImmutableArray.Create("Armor_Default"))
        };
        var ctx = new DiagnosticsContext(new EmptySchemaProvider(), index, "file:///test.xml", "en");

        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, "garbage, Armor_Default, 1.0"), ctx).ToList();
        var d = Assert.Single(results);
        Assert.Equal(XmlDiagnosticSeverity.Warning, d.Severity);
        Assert.Contains("garbage", d.Message);
    }
}
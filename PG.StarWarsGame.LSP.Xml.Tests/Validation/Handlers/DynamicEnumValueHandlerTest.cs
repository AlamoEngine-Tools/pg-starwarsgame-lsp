// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

public sealed class DynamicEnumValueHandlerTest
{
    private static readonly DynamicEnumValueHandler Sut = new();

    private static readonly XmlTagDefinition Tag =
        XmlHandlerTestFixtures.MakeTag("MovementClass", XmlValueType.DynamicEnumValue);

    private static readonly XmlTagDefinition FlagTag =
        XmlHandlerTestFixtures.MakeTag("CategoryMask", XmlValueType.DynamicEnumValue, TagSemanticType.FlagList);

    [Theory]
    [InlineData("Infantry")]
    [InlineData("Build Pad")]
    [InlineData("Galactic_Automatic")]
    public void Valid_single_enum_values_return_no_diagnostics(string value)
    {
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, value), XmlHandlerTestFixtures.EmptyCtx).ToList();
        Assert.Empty(results);
    }

    [Theory]
    [InlineData("Infantry")]
    [InlineData("Infantry | Vehicle | Air")]
    [InlineData("Build Pad")]
    public void Valid_flag_list_values_return_no_diagnostics(string value)
    {
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(FlagTag, value), XmlHandlerTestFixtures.EmptyCtx)
            .ToList();
        Assert.Empty(results);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Infantry|")]
    [InlineData("|Infantry")]
    [InlineData("Infantry | Vehicle | Air")]
    public void Pipe_on_single_enum_tag_returns_error(string value)
    {
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, value), XmlHandlerTestFixtures.EmptyCtx).ToList();
        Assert.Single(results);
        Assert.Equal(XmlDiagnosticSeverity.Error, results[0].Severity);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Infantry|")]
    [InlineData("|Infantry")]
    public void Invalid_flag_list_values_return_error(string value)
    {
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(FlagTag, value), XmlHandlerTestFixtures.EmptyCtx)
            .ToList();
        Assert.Single(results);
        Assert.Equal(XmlDiagnosticSeverity.Error, results[0].Severity);
    }

    [Fact]
    public void Known_value_passes_for_schema_fixed_enum()
    {
        var enumDef = SchemaFixed("ShipClass", false, "Frigate", "Capital");
        var tag = XmlHandlerTestFixtures.MakeTag("Ship_Class", XmlValueType.DynamicEnumValue, enumDef: enumDef);
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(tag, "Frigate"), XmlHandlerTestFixtures.EmptyCtx)
            .ToList();
        Assert.Empty(results);
    }

    [Fact]
    public void Unknown_value_fails_for_schema_fixed_enum()
    {
        var enumDef = SchemaFixed("ShipClass", false, "Frigate", "Capital");
        var tag = XmlHandlerTestFixtures.MakeTag("Ship_Class", XmlValueType.DynamicEnumValue, enumDef: enumDef);
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(tag, "INVALID_VALUE"), XmlHandlerTestFixtures.EmptyCtx)
            .ToList();
        var d = Assert.Single(results);
        Assert.Equal(XmlDiagnosticSeverity.Error, d.Severity);
    }

    [Fact]
    public void Value_lookup_is_case_insensitive()
    {
        var enumDef = SchemaFixed("ShipClass", false, "Frigate");
        var tag = XmlHandlerTestFixtures.MakeTag("Ship_Class", XmlValueType.DynamicEnumValue, enumDef: enumDef);
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(tag, "FRIGATE"), XmlHandlerTestFixtures.EmptyCtx)
            .ToList();
        Assert.Empty(results);
    }

    [Fact]
    public void Dynamic_xml_enum_empty_baseline_skips_value_check()
    {
        var enumDef = new EnumDefinition { Name = "MyEnum", Kind = EnumKind.DynamicXml, Values = [] };
        var tag = XmlHandlerTestFixtures.MakeTag("Tag", XmlValueType.DynamicEnumValue, enumDef: enumDef);
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(tag, "AnyValue123"), XmlHandlerTestFixtures.EmptyCtx)
            .ToList();
        Assert.Empty(results);
    }

    [Fact]
    public void Non_dynamic_enum_tag_returns_no_diagnostics()
    {
        var floatTag = XmlHandlerTestFixtures.MakeTag("Speed", XmlValueType.Float);
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(floatTag, "abc"), XmlHandlerTestFixtures.EmptyCtx)
            .ToList();
        Assert.Empty(results);
    }

    // ── DynamicXml enum - baseline validation ─────────────────────────────────

    [Fact]
    public void DynamicXml_unknown_value_with_populated_baseline_emits_warning()
    {
        var enumDef = DynXml("DamageType");
        var tag = XmlHandlerTestFixtures.MakeTag("Damage_Type", XmlValueType.DynamicEnumValue, enumDef: enumDef);
        var ctx = CtxWithEnumValues("DamageType", "EXPLOSIVE", "ENERGY");

        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(tag, "MADE_UP"), ctx).ToList();

        var d = Assert.Single(results);
        Assert.Equal(XmlDiagnosticSeverity.Warning, d.Severity);
        Assert.Contains("MADE_UP", d.Message);
    }

    [Fact]
    public void DynamicXml_known_value_with_populated_baseline_emits_no_diagnostic()
    {
        var enumDef = DynXml("DamageType");
        var tag = XmlHandlerTestFixtures.MakeTag("Damage_Type", XmlValueType.DynamicEnumValue, enumDef: enumDef);
        var ctx = CtxWithEnumValues("DamageType", "EXPLOSIVE", "ENERGY");

        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(tag, "EXPLOSIVE"), ctx).ToList();

        Assert.Empty(results);
    }

    [Fact]
    public void DynamicXml_value_lookup_is_case_insensitive()
    {
        var enumDef = DynXml("DamageType");
        var tag = XmlHandlerTestFixtures.MakeTag("Damage_Type", XmlValueType.DynamicEnumValue, enumDef: enumDef);
        var ctx = CtxWithEnumValues("DamageType", "Damage_Default");

        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(tag, "DAMAGE_DEFAULT"), ctx).ToList();

        Assert.Empty(results);
    }

    [Fact]
    public void DynamicXml_enum_not_in_baseline_dict_skips_validation()
    {
        var enumDef = DynXml("MovementClass");
        var tag = XmlHandlerTestFixtures.MakeTag("Movement_Class", XmlValueType.DynamicEnumValue, enumDef: enumDef);
        // Baseline only has DamageType, not MovementClass
        var ctx = CtxWithEnumValues("DamageType", "EXPLOSIVE");

        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(tag, "MADE_UP"), ctx).ToList();

        Assert.Empty(results);
    }

    [Fact]
    public void DynamicXml_flag_list_with_one_unknown_segment_emits_warning()
    {
        var enumDef = DynXml("GameObjectCategoryType");
        var tag = XmlHandlerTestFixtures.MakeTag("Category", XmlValueType.DynamicEnumValue,
            TagSemanticType.FlagList, enumDef);
        var ctx = CtxWithEnumValues("GameObjectCategoryType", "Fighter", "Bomber");

        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(tag, "Fighter | UNKNOWN_CAT"), ctx).ToList();

        var d = Assert.Single(results);
        Assert.Equal(XmlDiagnosticSeverity.Warning, d.Severity);
        Assert.Contains("UNKNOWN_CAT", d.Message);
    }

    [Fact]
    public void DynamicXml_flag_list_all_known_emits_no_diagnostic()
    {
        var enumDef = DynXml("GameObjectCategoryType");
        var tag = XmlHandlerTestFixtures.MakeTag("Category", XmlValueType.DynamicEnumValue,
            TagSemanticType.FlagList, enumDef);
        var ctx = CtxWithEnumValues("GameObjectCategoryType", "Fighter", "Bomber");

        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(tag, "Fighter | Bomber"), ctx).ToList();

        Assert.Empty(results);
    }

    // ── Workspace dynamic enum values ────────────────────────────────────────

    [Fact]
    public void DynamicXml_workspace_only_value_is_accepted()
    {
        var enumDef = DynXml("SurfaceFXTriggerType");
        var tag = XmlHandlerTestFixtures.MakeTag("SurfaceFX_Name", XmlValueType.DynamicEnumValue, enumDef: enumDef);

        // Baseline knows GENERIC_TRACK; workspace adds MY_CUSTOM_TRACK.
        var index = GameIndex.Empty with
        {
            Baseline = BaselineIndex.Empty with
            {
                DynamicEnumValues = ImmutableDictionary.CreateRange(
                    StringComparer.OrdinalIgnoreCase,
                    [KeyValuePair.Create("SurfaceFXTriggerType", ImmutableArray.Create("GENERIC_TRACK"))])
            },
            WorkspaceDynamicEnumValues = ImmutableDictionary.CreateRange(
                StringComparer.OrdinalIgnoreCase,
                [KeyValuePair.Create("SurfaceFXTriggerType", ImmutableArray.Create("MY_CUSTOM_TRACK"))])
        };
        var ctx = new DiagnosticsContext(new EmptySchemaProvider(), index, "file:///test.xml", "en");

        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(tag, "MY_CUSTOM_TRACK"), ctx).ToList();
        Assert.Empty(results);
    }

    [Fact]
    public void DynamicXml_baseline_value_still_accepted_when_workspace_also_present()
    {
        var enumDef = DynXml("SurfaceFXTriggerType");
        var tag = XmlHandlerTestFixtures.MakeTag("SurfaceFX_Name", XmlValueType.DynamicEnumValue, enumDef: enumDef);

        var index = GameIndex.Empty with
        {
            Baseline = BaselineIndex.Empty with
            {
                DynamicEnumValues = ImmutableDictionary.CreateRange(
                    StringComparer.OrdinalIgnoreCase,
                    [KeyValuePair.Create("SurfaceFXTriggerType", ImmutableArray.Create("GENERIC_TRACK"))])
            },
            WorkspaceDynamicEnumValues = ImmutableDictionary.CreateRange(
                StringComparer.OrdinalIgnoreCase,
                [KeyValuePair.Create("SurfaceFXTriggerType", ImmutableArray.Create("MY_CUSTOM_TRACK"))])
        };
        var ctx = new DiagnosticsContext(new EmptySchemaProvider(), index, "file:///test.xml", "en");

        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(tag, "GENERIC_TRACK"), ctx).ToList();
        Assert.Empty(results);
    }

    [Fact]
    public void DynamicXml_value_absent_from_both_baseline_and_workspace_warns()
    {
        var enumDef = DynXml("SurfaceFXTriggerType");
        var tag = XmlHandlerTestFixtures.MakeTag("SurfaceFX_Name", XmlValueType.DynamicEnumValue, enumDef: enumDef);

        var index = GameIndex.Empty with
        {
            Baseline = BaselineIndex.Empty with
            {
                DynamicEnumValues = ImmutableDictionary.CreateRange(
                    StringComparer.OrdinalIgnoreCase,
                    [KeyValuePair.Create("SurfaceFXTriggerType", ImmutableArray.Create("GENERIC_TRACK"))])
            },
            WorkspaceDynamicEnumValues = ImmutableDictionary.CreateRange(
                StringComparer.OrdinalIgnoreCase,
                [KeyValuePair.Create("SurfaceFXTriggerType", ImmutableArray.Create("MY_CUSTOM_TRACK"))])
        };
        var ctx = new DiagnosticsContext(new EmptySchemaProvider(), index, "file:///test.xml", "en");

        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(tag, "TOTALLY_UNKNOWN"), ctx).ToList();
        var d = Assert.Single(results);
        Assert.Equal(XmlDiagnosticSeverity.Warning, d.Severity);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static EnumDefinition DynXml(string name)
    {
        return new EnumDefinition { Name = name, Kind = EnumKind.DynamicXml, Values = [] };
    }

    private static DiagnosticsContext CtxWithEnumValues(string enumName, params string[] values)
    {
        var index = GameIndex.Empty with
        {
            Baseline = BaselineIndex.Empty with
            {
                DynamicEnumValues = ImmutableDictionary.CreateRange(
                    StringComparer.OrdinalIgnoreCase,
                    [KeyValuePair.Create(enumName, ImmutableArray.Create(values))])
            }
        };
        return new DiagnosticsContext(new EmptySchemaProvider(), index, "file:///test.xml", "en");
    }

    private static EnumDefinition SchemaFixed(string name, bool isBitfield, params string[] values)
    {
        return new EnumDefinition
        {
            Name = name, Kind = EnumKind.SchemaFixed, IsBitfield = isBitfield,
            Values = values.Select(v => new EnumValueDefinition { Name = v }).ToList()
        };
    }
}
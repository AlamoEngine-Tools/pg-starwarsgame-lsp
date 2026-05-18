// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Xml.Validation.Validators;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validators;

file static class TagOf
{
    public static XmlTagDefinition Make(string name, XmlValueType type,
        TagSemanticType semanticType = TagSemanticType.Default)
    {
        return new XmlTagDefinition { Tag = name, ValueType = type, SemanticType = semanticType };
    }
}

file sealed class StubSchemaProvider : ISchemaProvider
{
    private readonly Dictionary<string, EnumDefinition> _enums;

    public StubSchemaProvider(IEnumerable<EnumDefinition>? enums = null)
    {
        _enums = (enums ?? []).ToDictionary(e => e.Name, StringComparer.OrdinalIgnoreCase);
    }

    public EnumDefinition? GetEnum(string enumName) => _enums.GetValueOrDefault(enumName);
    public IReadOnlyList<EnumDefinition> AllEnums => [.. _enums.Values];
    public IReadOnlyList<XmlTagDefinition> AllTags => [];
    public IReadOnlyList<GameObjectTypeDefinition> AllObjectTypes => [];
    public XmlTagDefinition? GetTag(string _) => null;
    public IReadOnlyList<XmlTagDefinition> GetAllTagDefinitions(string _) => [];
    public GameObjectTypeDefinition? GetObjectType(string _) => null;
    public IReadOnlyList<XmlTagDefinition> GetTagsForType(string _) => [];
    public event EventHandler? SchemaRefreshed { add { } remove { } }
}

public sealed class DynamicEnumValueValidatorTest
{
    private static readonly DynamicEnumValueValidator Sut = new(new StubSchemaProvider());
    private static readonly XmlTagDefinition Tag = TagOf.Make("MovementClass", XmlValueType.DynamicEnumValue);

    private static readonly XmlTagDefinition FlagTag = TagOf.Make("CategoryMask", XmlValueType.DynamicEnumValue,
        TagSemanticType.FlagList);

    [Theory]
    [InlineData("Infantry")]
    [InlineData("Build Pad")]
    [InlineData("Galactic_Automatic")]
    [InlineData("1x1")]
    public void Valid_single_enum_identifiers_pass(string value)
    {
        Assert.True(Sut.Validate(value, Tag).IsValid);
    }

    [Theory]
    [InlineData("Infantry")]
    [InlineData("Infantry | Vehicle | Air")]
    [InlineData("Build Pad")]
    public void Valid_flag_list_identifiers_pass(string value)
    {
        Assert.True(Sut.Validate(value, FlagTag).IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Infantry|")]
    [InlineData("|Infantry")]
    [InlineData("Infantry | Vehicle | Air")]
    public void Pipe_on_single_enum_tag_fails(string value)
    {
        Assert.False(Sut.Validate(value, Tag).IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Infantry|")]
    [InlineData("|Infantry")]
    public void Invalid_flag_list_identifiers_fail(string value)
    {
        Assert.False(Sut.Validate(value, FlagTag).IsValid);
    }

    private static EnumDefinition SchemaFixed(string name, bool isBitfield, params string[] values) =>
        new()
        {
            Name = name, Kind = EnumKind.SchemaFixed, IsBitfield = isBitfield,
            Values = values.Select(v => new EnumValueDefinition { Name = v }).ToList()
        };

    private static EnumDefinition DynamicXml(string name) =>
        new() { Name = name, Kind = EnumKind.DynamicXml, Values = [] };

    private static XmlTagDefinition EnumTag(string enumName, TagSemanticType sem = TagSemanticType.Default) =>
        new() { Tag = "Tag", ValueType = XmlValueType.DynamicEnumValue, EnumName = enumName, SemanticType = sem };

    [Fact]
    public void Known_value_passes_for_schema_fixed_enum()
    {
        var sut = new DynamicEnumValueValidator(
            new StubSchemaProvider([SchemaFixed("ShipClass", false, "Frigate", "Capital")]));
        Assert.True(sut.Validate("Frigate", EnumTag("ShipClass")).IsValid);
    }

    [Fact]
    public void Unknown_value_fails_for_schema_fixed_enum()
    {
        var sut = new DynamicEnumValueValidator(
            new StubSchemaProvider([SchemaFixed("ShipClass", false, "Frigate", "Capital")]));
        Assert.False(sut.Validate("INVALID_VALUE", EnumTag("ShipClass")).IsValid);
    }

    [Fact]
    public void Value_lookup_is_case_insensitive()
    {
        var sut = new DynamicEnumValueValidator(
            new StubSchemaProvider([SchemaFixed("ShipClass", false, "Frigate")]));
        Assert.True(sut.Validate("FRIGATE", EnumTag("ShipClass")).IsValid);
    }

    [Fact]
    public void FlagList_each_segment_validated_against_enum()
    {
        var sut = new DynamicEnumValueValidator(
            new StubSchemaProvider([SchemaFixed("Cat", true, "Infantry", "Vehicle", "Air")]));
        Assert.True(sut.Validate("Infantry | Vehicle", EnumTag("Cat", TagSemanticType.FlagList)).IsValid);
    }

    [Fact]
    public void FlagList_unknown_segment_fails()
    {
        var sut = new DynamicEnumValueValidator(
            new StubSchemaProvider([SchemaFixed("Cat", true, "Infantry", "Vehicle")]));
        Assert.False(sut.Validate("Infantry | BOGUS", EnumTag("Cat", TagSemanticType.FlagList)).IsValid);
    }

    [Fact]
    public void Comma_separated_values_validated_agnostically()
    {
        var sut = new DynamicEnumValueValidator(
            new StubSchemaProvider([SchemaFixed("Cat", true, "Infantry", "Vehicle")]));
        Assert.True(sut.Validate("Infantry, Vehicle", EnumTag("Cat", TagSemanticType.FlagList)).IsValid);
    }

    [Fact]
    public void Dynamic_xml_enum_skips_value_check()
    {
        var sut = new DynamicEnumValueValidator(
            new StubSchemaProvider([DynamicXml("GameObjectCategoryType")]));
        Assert.True(sut.Validate("AnyValue123", EnumTag("GameObjectCategoryType")).IsValid);
    }

    [Fact]
    public void Unknown_enum_name_falls_back_to_format_only()
    {
        var sut = new DynamicEnumValueValidator(new StubSchemaProvider());
        Assert.True(sut.Validate("AnyValidIdent", EnumTag("UnknownEnum")).IsValid);
    }

    [Fact]
    public void No_enum_name_on_tag_falls_back_to_format_only()
    {
        var sut = new DynamicEnumValueValidator(
            new StubSchemaProvider([SchemaFixed("ShipClass", false, "Frigate")]));
        var tag = new XmlTagDefinition { Tag = "Tag", ValueType = XmlValueType.DynamicEnumValue };
        Assert.True(sut.Validate("AnyValidIdent", tag).IsValid);
    }
}

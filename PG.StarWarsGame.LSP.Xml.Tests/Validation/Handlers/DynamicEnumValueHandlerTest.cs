// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
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
    public void Dynamic_xml_enum_skips_value_check()
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

    private static EnumDefinition SchemaFixed(string name, bool isBitfield, params string[] values)
    {
        return new EnumDefinition
        {
            Name = name, Kind = EnumKind.SchemaFixed, IsBitfield = isBitfield,
            Values = values.Select(v => new EnumValueDefinition { Name = v }).ToList()
        };
    }
}
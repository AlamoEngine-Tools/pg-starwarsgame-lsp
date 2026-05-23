// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

public sealed class StoryParamEnumHandlerTest
{
    private static readonly StoryParamEnumHandler Sut = new();

    private static EnumDefinition MakeEnum(params string[] values)
    {
        return new EnumDefinition
        {
            Name = "TestEnum",
            Kind = EnumKind.SchemaFixed,
            Values = [.. values.Select(v => new EnumValueDefinition { Name = v })]
        };
    }

    private static StoryParamFact MakeFact(EnumDefinition? enumDef, string value)
    {
        return new StoryParamFact("file:///test.xml", 1, 0, 0, "MY_EVENT", false, 0,
            new ParamDefinition { Position = 0, ValueType = XmlValueType.DynamicEnumValue, Enum = enumDef },
            value);
    }

    [Fact]
    public void Valid_enum_value_emits_no_diagnostics()
    {
        var fact = MakeFact(MakeEnum("RED", "GREEN", "BLUE"), "RED");
        Assert.Empty(Sut.Handle(fact, XmlHandlerTestFixtures.EmptyCtx));
    }

    [Fact]
    public void Invalid_enum_value_emits_warning()
    {
        var fact = MakeFact(MakeEnum("RED", "GREEN"), "YELLOW");
        var results = Sut.Handle(fact, XmlHandlerTestFixtures.EmptyCtx).ToList();
        var d = Assert.Single(results);
        Assert.Equal(XmlDiagnosticSeverity.Warning, d.Severity);
        Assert.Contains("YELLOW", d.Message);
    }

    [Fact]
    public void Valid_space_separated_enum_list_emits_no_diagnostics()
    {
        var fact = MakeFact(MakeEnum("A", "B", "C"), "A B");
        Assert.Empty(Sut.Handle(fact, XmlHandlerTestFixtures.EmptyCtx));
    }

    [Fact]
    public void Invalid_token_in_enum_list_emits_warning()
    {
        var fact = MakeFact(MakeEnum("A", "B"), "A INVALID");
        var results = Sut.Handle(fact, XmlHandlerTestFixtures.EmptyCtx).ToList();
        Assert.Single(results);
        Assert.Contains("INVALID", results[0].Message);
    }

    [Fact]
    public void Enum_matching_is_case_insensitive()
    {
        var fact = MakeFact(MakeEnum("RED"), "red");
        Assert.Empty(Sut.Handle(fact, XmlHandlerTestFixtures.EmptyCtx));
    }

    [Fact]
    public void Null_enum_def_emits_no_diagnostics()
    {
        var fact = MakeFact(null, "anything");
        Assert.Empty(Sut.Handle(fact, XmlHandlerTestFixtures.EmptyCtx));
    }

    [Fact]
    public void Non_enum_value_type_emits_no_diagnostics()
    {
        var fact = new StoryParamFact("file:///test.xml", 1, 0, 0, "MY_EVENT", false, 0,
            new ParamDefinition { Position = 0, ValueType = XmlValueType.Int }, "YELLOW");
        Assert.Empty(Sut.Handle(fact, XmlHandlerTestFixtures.EmptyCtx));
    }
}
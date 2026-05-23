// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

public sealed class StoryParamValueHandlerTest
{
    private static readonly StoryParamValueHandler Sut = new();

    private static StoryParamFact MakeFact(XmlValueType type, string value)
    {
        return new StoryParamFact("file:///test.xml", 1, 0, 0, "MY_EVENT", false, 0,
            new ParamDefinition { Position = 0, ValueType = type }, value);
    }

    [Theory]
    [InlineData("42")]
    [InlineData("-5")]
    public void Valid_int_emits_no_diagnostics(string value)
    {
        Assert.Empty(Sut.Handle(MakeFact(XmlValueType.Int, value), XmlHandlerTestFixtures.EmptyCtx));
    }

    [Fact]
    public void Invalid_int_emits_warning()
    {
        var results = Sut.Handle(MakeFact(XmlValueType.Int, "not_an_int"), XmlHandlerTestFixtures.EmptyCtx).ToList();
        Assert.Single(results);
        Assert.Equal(XmlDiagnosticSeverity.Warning, results[0].Severity);
        Assert.Contains("not_an_int", results[0].Message);
    }

    [Theory]
    [InlineData("3.14")]
    [InlineData("1.0")]
    public void Valid_float_emits_no_diagnostics(string value)
    {
        Assert.Empty(Sut.Handle(MakeFact(XmlValueType.Float, value), XmlHandlerTestFixtures.EmptyCtx));
    }

    [Fact]
    public void Invalid_float_emits_warning()
    {
        Assert.Single(Sut.Handle(MakeFact(XmlValueType.Float, "not_a_float"), XmlHandlerTestFixtures.EmptyCtx));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("false")]
    [InlineData("TRUE")]
    [InlineData("FALSE")]
    public void Valid_boolean_emits_no_diagnostics(string value)
    {
        Assert.Empty(Sut.Handle(MakeFact(XmlValueType.Boolean, value), XmlHandlerTestFixtures.EmptyCtx));
    }

    [Fact]
    public void Invalid_boolean_emits_warning()
    {
        Assert.Single(Sut.Handle(MakeFact(XmlValueType.Boolean, "yes"), XmlHandlerTestFixtures.EmptyCtx));
    }

    [Fact]
    public void Valid_float_vector3_emits_no_diagnostics()
    {
        Assert.Empty(Sut.Handle(MakeFact(XmlValueType.FloatVector3, "1.0 2.0 3.0"), XmlHandlerTestFixtures.EmptyCtx));
    }

    [Fact]
    public void Invalid_float_vector3_wrong_count_emits_warning()
    {
        Assert.Single(Sut.Handle(MakeFact(XmlValueType.FloatVector3, "1.0 2.0"), XmlHandlerTestFixtures.EmptyCtx));
    }

    [Fact]
    public void Empty_value_emits_no_diagnostics()
    {
        Assert.Empty(Sut.Handle(MakeFact(XmlValueType.Int, ""), XmlHandlerTestFixtures.EmptyCtx));
    }

    [Fact]
    public void Null_def_emits_no_diagnostics()
    {
        var fact = new StoryParamFact("file:///test.xml", 1, 0, 0, "MY_EVENT", false, 0, null, "42");
        Assert.Empty(Sut.Handle(fact, XmlHandlerTestFixtures.EmptyCtx));
    }

    [Fact]
    public void NameReference_type_does_not_emit_format_warning()
    {
        Assert.Empty(Sut.Handle(MakeFact(XmlValueType.NameReference, "SomeName"), XmlHandlerTestFixtures.EmptyCtx));
    }
}
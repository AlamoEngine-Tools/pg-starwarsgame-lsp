// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

public sealed class DisallowedOrOperatorHandlerTest
{
    private static readonly DisallowedOrOperatorHandler Sut = new();

    [Theory]
    [InlineData(XmlValueType.GameObjectTypeReferenceList)]
    [InlineData(XmlValueType.TypeReferenceList)]
    [InlineData(XmlValueType.NameReferenceList)]
    [InlineData(XmlValueType.PerFactionObjectList)]
    public void Pipe_in_and_only_list_returns_error_with_comma_suggested_fix(XmlValueType type)
    {
        var tag = XmlHandlerTestFixtures.MakeTag("Tactical_Build_Prerequisites", type);
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(tag, "StructA | StructB"),
            XmlHandlerTestFixtures.EmptyCtx).ToList();

        var d = Assert.Single(results);
        Assert.Equal(XmlDiagnosticSeverity.Error, d.Severity);
        Assert.Equal("StructA , StructB", d.SuggestedFix);
    }

    [Theory]
    [InlineData("StructA, StructB")]
    [InlineData("StructA StructB")]
    [InlineData("StructA")]
    public void Values_without_pipe_return_no_diagnostics(string value)
    {
        var tag = XmlHandlerTestFixtures.MakeTag("Tactical_Build_Prerequisites", XmlValueType.GameObjectTypeReferenceList);
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(tag, value), XmlHandlerTestFixtures.EmptyCtx).ToList();
        Assert.Empty(results);
    }

    [Fact]
    public void Prerequisite_expression_tags_are_exempt()
    {
        var tag = XmlHandlerTestFixtures.MakeTag(
            "Required_Special_Structures", XmlValueType.GameObjectTypeReferenceList, TagSemanticType.PrerequisiteExpression);
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(tag, "StructA | StructB"),
            XmlHandlerTestFixtures.EmptyCtx).ToList();
        Assert.Empty(results);
    }

    [Fact]
    public void Non_list_value_types_return_no_diagnostics()
    {
        var tag = XmlHandlerTestFixtures.MakeTag("Max_Speed", XmlValueType.Float);
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(tag, "1 | 2"), XmlHandlerTestFixtures.EmptyCtx).ToList();
        Assert.Empty(results);
    }
}

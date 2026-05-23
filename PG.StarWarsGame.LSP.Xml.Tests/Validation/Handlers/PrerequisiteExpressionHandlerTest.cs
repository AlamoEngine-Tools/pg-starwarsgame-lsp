// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

public sealed class PrerequisiteExpressionHandlerTest
{
    private static readonly PrerequisiteExpressionHandler Sut = new();

    private static readonly XmlTagDefinition Tag = XmlHandlerTestFixtures.MakeTag(
        "Required_Special_Structures",
        XmlValueType.GameObjectTypeReferenceList,
        TagSemanticType.PrerequisiteExpression);

    [Theory]
    [InlineData("U_Ground_Barracks")]
    [InlineData("StructA | StructB")]
    [InlineData("StructA, StructB")]
    [InlineData("StructA StructB")]
    [InlineData("StructA | StructB, StructC | StructD")]
    [InlineData("A | B, C")]
    public void Valid_expressions_return_no_diagnostics(string value)
    {
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, value), XmlHandlerTestFixtures.EmptyCtx).ToList();
        Assert.Empty(results);
    }

    [Theory]
    [InlineData("")]
    [InlineData("|StructA")]
    [InlineData("StructA|")]
    [InlineData("StructA | | StructB")]
    [InlineData("StructA,,StructB")]
    public void Invalid_expressions_return_error(string value)
    {
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, value), XmlHandlerTestFixtures.EmptyCtx).ToList();
        var d = Assert.Single(results);
        Assert.Equal(XmlDiagnosticSeverity.Error, d.Severity);
    }

    [Fact]
    public void Non_prerequisite_semantic_tag_returns_no_diagnostics()
    {
        var nonSemTag = XmlHandlerTestFixtures.MakeTag(
            "Object_List",
            XmlValueType.GameObjectTypeReferenceList);
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(nonSemTag, ""), XmlHandlerTestFixtures.EmptyCtx)
            .ToList();
        Assert.Empty(results);
    }
}
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

public sealed class PrerequisiteExpressionValidatorTest
{
    private static readonly PrerequisiteExpressionValidator Sut = new();

    private static readonly XmlTagDefinition Tag =
        TagOf.Make("Required_Special_Structures", XmlValueType.GameObjectTypeReferenceList,
            TagSemanticType.PrerequisiteExpression);

    [Theory]
    [InlineData("U_Ground_Barracks")]
    [InlineData("StructA | StructB")]
    [InlineData("StructA, StructB")]
    [InlineData("StructA StructB")]
    [InlineData("StructA | StructB, StructC | StructD")]
    [InlineData("StructA | StructB StructC | StructD")]
    [InlineData("A | B, C")]
    public void Valid_expressions_pass(string value)
    {
        Assert.True(Sut.Validate(value, Tag).IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("|StructA")]
    [InlineData("StructA|")]
    [InlineData("StructA | | StructB")]
    [InlineData("StructA,,StructB")]
    public void Invalid_expressions_fail(string value)
    {
        Assert.False(Sut.Validate(value, Tag).IsValid);
    }
}
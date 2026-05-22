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

public sealed class FloatVector4ValidatorTest
{
    private static readonly FloatVector4Validator Sut = new();
    private static readonly XmlTagDefinition Tag = TagOf.Make("Shield_Normal_Color", XmlValueType.FloatVector4);

    [Theory]
    [InlineData("0.0, 1.0, 1.0, 0.0")]
    [InlineData("0.2 0.7 0.8 1.0")]
    [InlineData("0.2, 0.7, 0.8, 1.0")]
    [InlineData("1.0f, 0.5f, 0.0f, 1.0f")]
    public void Valid_float4_values_pass(string value)
    {
        Assert.True(Sut.Validate(value, Tag).IsValid);
    }

    [Theory]
    [InlineData("1 2 3")]
    [InlineData("a b c d")]
    [InlineData("")]
    [InlineData("1 2 3 4 5")]
    public void Invalid_float4_values_fail(string value)
    {
        Assert.False(Sut.Validate(value, Tag).IsValid);
    }
}
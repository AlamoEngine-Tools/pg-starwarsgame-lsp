// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Xml.Validation.Validators;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validators;

file static class TagOf
{
    public static XmlTagDefinition Make(string name, XmlValueType type,
        TagSemanticType semanticType = TagSemanticType.Default)
        => new() { Tag = name, ValueType = type, SemanticType = semanticType };
}

public sealed class FloatVector2ValidatorTest
{
    private static readonly FloatVector2Validator Sut = new();
    private static readonly XmlTagDefinition Tag = TagOf.Make("UV_Offset", XmlValueType.FloatVector2);

    [Theory]
    [InlineData("0.0 0.0")]
    [InlineData("1.0, 2.5")]
    [InlineData("-1.0 1.0")]
    [InlineData("0.5f 0.5f")]
    public void Valid_float2_values_pass(string value)
        => Assert.True(Sut.Validate(value, Tag).IsValid);

    [Theory]
    [InlineData("1 2 3")]
    [InlineData("1")]
    [InlineData("a b")]
    [InlineData("")]
    public void Invalid_float2_values_fail(string value)
        => Assert.False(Sut.Validate(value, Tag).IsValid);
}

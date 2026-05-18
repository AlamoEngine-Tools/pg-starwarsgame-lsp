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

public sealed class FloatVector3ListValidatorTest
{
    private static readonly FloatVector3ListValidator Sut = new();
    private static readonly XmlTagDefinition Tag = TagOf.Make("Path_Points", XmlValueType.FloatVector3List);

    [Theory]
    [InlineData("0.0 0.0 0.0")]
    [InlineData("1.0 2.0 3.0 4.0 5.0 6.0")]
    [InlineData("-1.0 0 1.0 2.0 -2.0 0.5")]
    public void Valid_float3_lists_pass(string value)
        => Assert.True(Sut.Validate(value, Tag).IsValid);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1.0 2.0")]
    [InlineData("1.0 2.0 3.0 4.0")]
    [InlineData("a b c")]
    public void Invalid_float3_lists_fail(string value)
        => Assert.False(Sut.Validate(value, Tag).IsValid);
}

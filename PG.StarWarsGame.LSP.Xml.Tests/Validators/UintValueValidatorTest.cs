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

public sealed class UintValueValidatorTest
{
    private static readonly UintValueValidator Sut = new();
    private static readonly XmlTagDefinition Tag = TagOf.Make("Uint_Value", XmlValueType.UInt);

    [Theory]
    [InlineData("2147483647" /*int.MaxValue*/)]
    [InlineData("0")]
    [InlineData("1")]
    public void Valid_uint_values_pass(string value)
    {
        Assert.True(Sut.Validate(value, Tag).IsValid);
    }

    [Theory]
    [InlineData("2147483648" /*int.MaxValue + 1*/)]
    [InlineData("-1 0")]
    [InlineData("abc")]
    [InlineData("1 2")]
    [InlineData("")]
    public void Invalid_uint_values_fail(string value)
    {
        Assert.False(Sut.Validate(value, Tag).IsValid);
    }
}
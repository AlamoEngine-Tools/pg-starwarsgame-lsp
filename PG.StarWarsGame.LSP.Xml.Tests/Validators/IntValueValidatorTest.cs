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

public sealed class IntValueValidatorTest
{
    private static readonly IntValueValidator Sut = new();
    private static readonly XmlTagDefinition Tag = TagOf.Make("Int_Value", XmlValueType.Int);

    [Theory]
    [InlineData("2147483647" /*int.MaxValue*/)]
    [InlineData("-2147483648" /*int.MinValue*/)]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("-1")]
    public void Valid_int_values_pass(string value)
    {
        Assert.True(Sut.Validate(value, Tag).IsValid);
    }

    [Theory]
    [InlineData("-1 0")]
    [InlineData("abc")]
    [InlineData("1 2")]
    [InlineData("")]
    [InlineData("2147483648" /*int.MaxValue + 1*/)]
    [InlineData("-2147483649" /*int.MinValue - 1*/)]
    public void Invalid_int_values_fail(string value)
    {
        Assert.False(Sut.Validate(value, Tag).IsValid);
    }
}

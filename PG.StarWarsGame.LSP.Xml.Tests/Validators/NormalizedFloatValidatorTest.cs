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

public sealed class NormalizedFloatValidatorTest
{
    private static readonly NormalizedFloatValidator Sut = new();

    private static readonly XmlTagDefinition Tag =
        TagOf.Make("Squadron_Refill_Threshold", XmlValueType.NormalizedFloat);

    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("0.5")]
    [InlineData("0.75f")]
    [InlineData("1.0")]
    [InlineData("0.0F")]
    [InlineData("0.0f")]
    public void Valid_normalized_float_values_pass(string value)
    {
        Assert.True(Sut.Validate(value, Tag).IsValid);
    }

    [Theory]
    [InlineData("1.1")]
    [InlineData("-0.1")]
    [InlineData("2")]
    [InlineData("-1")]
    public void Out_of_range_values_fail(string value)
    {
        Assert.False(Sut.Validate(value, Tag).IsValid);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("")]
    public void Non_numeric_values_fail(string value)
    {
        Assert.False(Sut.Validate(value, Tag).IsValid);
    }
}
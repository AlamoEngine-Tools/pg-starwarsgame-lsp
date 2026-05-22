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

public sealed class SfxPercentageValidatorTest
{
    private static readonly SfxPercentageValidator Sut = new();
    private static readonly XmlTagDefinition Tag = TagOf.Make("Volume_Percent", XmlValueType.SfxPercentage);

    [Theory]
    [InlineData("0")]
    [InlineData("50")]
    [InlineData("100")]
    public void Valid_sfx_percentages_pass(string value)
    {
        Assert.True(Sut.Validate(value, Tag).IsValid);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("101")]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("50.5")]
    public void Invalid_sfx_percentages_fail(string value)
    {
        Assert.False(Sut.Validate(value, Tag).IsValid);
    }
}
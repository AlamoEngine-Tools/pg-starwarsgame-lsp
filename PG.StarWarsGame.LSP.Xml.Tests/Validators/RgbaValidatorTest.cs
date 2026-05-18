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

public sealed class RgbaValidatorTest
{
    private static readonly RgbaValidator Sut = new();
    private static readonly XmlTagDefinition Tag = TagOf.Make("GUI_Cycle_Color", XmlValueType.RGBA);

    [Theory]
    [InlineData("255 64 64 255")]
    [InlineData("239, 9, 9, 255")]
    [InlineData("0 0 0")]
    [InlineData("0,255,0,255")]
    [InlineData("128,0,0,128")]
    [InlineData("255 255 255 180")]
    public void Valid_rgba_values_pass(string value)
    {
        Assert.True(Sut.Validate(value, Tag).IsValid);
    }

    [Theory]
    [InlineData("256 0 0 255")]
    [InlineData("-1 0 0 255")]
    [InlineData("abc")]
    [InlineData("1 2")]
    [InlineData("")]
    [InlineData("1 2 3 4 5")]
    public void Invalid_rgba_values_fail(string value)
    {
        Assert.False(Sut.Validate(value, Tag).IsValid);
    }
}

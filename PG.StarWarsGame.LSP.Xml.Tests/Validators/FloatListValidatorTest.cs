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

public sealed class FloatListValidatorTest
{
    private static readonly FloatListValidator Sut = new();
    private static readonly XmlTagDefinition Tag = TagOf.Make("Key_Frames", XmlValueType.FloatList);

    [Theory]
    [InlineData("0.0")]
    [InlineData("1.0 2.5 3.14")]
    [InlineData("-1.0 0 1.0")]
    [InlineData("0.5f 1.0f")]
    public void Valid_float_lists_pass(string value)
    {
        Assert.True(Sut.Validate(value, Tag).IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1.0 abc 3.0")]
    public void Invalid_float_lists_fail(string value)
    {
        Assert.False(Sut.Validate(value, Tag).IsValid);
    }
}
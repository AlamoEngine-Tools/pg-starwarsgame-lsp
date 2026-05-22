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

public sealed class IntListValidatorTest
{
    private static readonly IntListValidator Sut = new();
    private static readonly XmlTagDefinition Tag = TagOf.Make("Frame_List", XmlValueType.IntList);

    [Theory]
    [InlineData("0")]
    [InlineData("1 2 3")]
    [InlineData("10 20 30 40")]
    [InlineData("-1 0 1")]
    public void Valid_int_lists_pass(string value)
    {
        Assert.True(Sut.Validate(value, Tag).IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1 abc 3")]
    [InlineData("1.5 2.5")]
    public void Invalid_int_lists_fail(string value)
    {
        Assert.False(Sut.Validate(value, Tag).IsValid);
    }
}
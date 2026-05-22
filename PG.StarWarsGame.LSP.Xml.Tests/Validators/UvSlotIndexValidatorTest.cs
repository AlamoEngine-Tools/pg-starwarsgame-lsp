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

public sealed class UvSlotIndexValidatorTest
{
    private static readonly UvSlotIndexValidator Sut = new();
    private static readonly XmlTagDefinition Tag = TagOf.Make("UV_Slot", XmlValueType.UvSlotIndex);

    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("2")]
    [InlineData("3")]
    public void Valid_uv_slot_indices_pass(string value)
    {
        Assert.True(Sut.Validate(value, Tag).IsValid);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("4")]
    [InlineData("abc")]
    [InlineData("")]
    public void Invalid_uv_slot_indices_fail(string value)
    {
        Assert.False(Sut.Validate(value, Tag).IsValid);
    }
}
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

public sealed class SfxCountValidatorTest
{
    private static readonly SfxCountValidator Sut = new();
    private static readonly XmlTagDefinition Tag = TagOf.Make("Max_Instances", XmlValueType.SfxCount);

    [Theory]
    [InlineData("-1")]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("100")]
    public void Valid_sfx_counts_pass(string value)
        => Assert.True(Sut.Validate(value, Tag).IsValid);

    [Theory]
    [InlineData("-2")]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("1.5")]
    public void Invalid_sfx_counts_fail(string value)
        => Assert.False(Sut.Validate(value, Tag).IsValid);
}

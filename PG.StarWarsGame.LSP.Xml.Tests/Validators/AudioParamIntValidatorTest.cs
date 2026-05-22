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

public sealed class AudioParamIntValidatorTest
{
    private static readonly AudioParamIntValidator Sut = new();
    private static readonly XmlTagDefinition Tag = TagOf.Make("Min_Volume", XmlValueType.AudioParamInt);

    [Theory]
    [InlineData("0")]
    [InlineData("50")]
    [InlineData("100")]
    [InlineData("127")]
    public void Valid_audio_param_ints_pass(string value)
    {
        Assert.True(Sut.Validate(value, Tag).IsValid);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("128")]
    public void Invalid_audio_param_ints_fail(string value)
    {
        Assert.False(Sut.Validate(value, Tag).IsValid);
    }
}
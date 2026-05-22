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

public sealed class ShaderVersionHexValidatorTest
{
    private static readonly ShaderVersionHexValidator Sut = new();
    private static readonly XmlTagDefinition Tag = TagOf.Make("PixelShaderVersionHEX", XmlValueType.ShaderVersionHex);

    [Theory]
    [InlineData("0x0200")]
    [InlineData("0x0000")]
    [InlineData("0x0100")]
    [InlineData("0xDEAD")]
    [InlineData("0Xabcd")]
    public void Valid_hex_literals_pass(string value)
    {
        Assert.True(Sut.Validate(value, Tag).IsValid);
    }

    [Theory]
    [InlineData("0200")]
    [InlineData("0xGGGG")]
    [InlineData("0x")]
    [InlineData("")]
    [InlineData("0x 200")]
    public void Invalid_hex_literals_fail(string value)
    {
        Assert.False(Sut.Validate(value, Tag).IsValid);
    }
}
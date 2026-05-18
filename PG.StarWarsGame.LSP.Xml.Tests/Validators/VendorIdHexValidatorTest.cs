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

public sealed class VendorIdHexValidatorTest
{
    private static readonly VendorIdHexValidator Sut = new();
    private static readonly XmlTagDefinition Tag = TagOf.Make("VendorIDHEX", XmlValueType.VendorIdHex);

    [Theory]
    [InlineData("0x8086")]
    [InlineData("0x10DE")]
    [InlineData("0x0000")]
    public void Valid_vendor_hex_passes(string value)
    {
        Assert.True(Sut.Validate(value, Tag).IsValid);
    }

    [Theory]
    [InlineData("8086")]
    [InlineData("0x")]
    [InlineData("")]
    public void Invalid_vendor_hex_fails(string value)
    {
        Assert.False(Sut.Validate(value, Tag).IsValid);
    }
}

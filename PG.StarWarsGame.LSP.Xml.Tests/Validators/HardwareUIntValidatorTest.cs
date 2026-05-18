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

public sealed class HardwareUIntValidatorTest
{
    private static readonly HardwareUIntValidator Sut = new();
    private static readonly XmlTagDefinition Tag = TagOf.Make("Vendor_ID", XmlValueType.HardwareUInt);

    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("65535")]
    [InlineData("4294967295" /*uint.MaxValue*/)]
    public void Valid_hardware_uints_pass(string value)
        => Assert.True(Sut.Validate(value, Tag).IsValid);

    [Theory]
    [InlineData("-1")]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("4294967296" /*uint.MaxValue + 1*/)]
    public void Invalid_hardware_uints_fail(string value)
        => Assert.False(Sut.Validate(value, Tag).IsValid);
}

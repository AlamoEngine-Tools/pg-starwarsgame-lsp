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

public sealed class ShipClassTypeValidatorTest
{
    private static readonly ShipClassTypeValidator Sut = new();
    private static readonly XmlTagDefinition Tag = TagOf.Make("Ship_Class", XmlValueType.ShipClassType);

    [Theory]
    [InlineData("CAPITAL_SHIP")]
    [InlineData("Fighter")]
    [InlineData("Star Destroyer")]
    public void Valid_ship_class_types_pass(string value)
    {
        Assert.True(Sut.Validate(value, Tag).IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_value_fails(string value)
    {
        Assert.False(Sut.Validate(value, Tag).IsValid);
    }
}
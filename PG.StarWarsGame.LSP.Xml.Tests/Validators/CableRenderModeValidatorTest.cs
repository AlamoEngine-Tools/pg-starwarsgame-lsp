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

public sealed class CableRenderModeValidatorTest
{
    private static readonly CableRenderModeValidator Sut = new();
    private static readonly XmlTagDefinition Tag = TagOf.Make("Cable_Render_Mode", XmlValueType.CableRenderMode);

    [Theory]
    [InlineData("FLAT")]
    [InlineData("Round")]
    [InlineData("Cylinder Mapped")]
    public void Valid_cable_render_modes_pass(string value)
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
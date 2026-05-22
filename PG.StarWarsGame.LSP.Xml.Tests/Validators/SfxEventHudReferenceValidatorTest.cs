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

public sealed class SfxEventHudReferenceValidatorTest
{
    private static readonly SfxEventHudReferenceValidator Sut = new();
    private static readonly XmlTagDefinition Tag = TagOf.Make("HUD_Sound", XmlValueType.SfxEventHudReference);

    [Theory]
    [InlineData("SFX_HUD_CLICK")]
    [InlineData("hud_button_press")]
    [InlineData("UI Select Sound")]
    public void Valid_sfx_hud_references_pass(string value)
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
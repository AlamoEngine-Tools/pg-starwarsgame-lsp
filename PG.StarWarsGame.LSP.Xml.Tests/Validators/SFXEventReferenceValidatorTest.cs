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

public sealed class SFXEventReferenceValidatorTest
{
    private static readonly SFXEventReferenceValidator Sut = new();
    private static readonly XmlTagDefinition Tag = TagOf.Make("Fire_Sound", XmlValueType.SFXEventReference);

    [Theory]
    [InlineData("SFX_LASER_FIRE")]
    [InlineData("sfx_blaster")]
    [InlineData("Explosion Large")]
    public void Valid_sfx_event_references_pass(string value)
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
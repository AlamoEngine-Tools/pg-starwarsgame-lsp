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

public sealed class FactionReferenceValidatorTest
{
    private static readonly FactionReferenceValidator Sut = new();
    private static readonly XmlTagDefinition Tag = TagOf.Make("Faction", XmlValueType.FactionReference);

    [Theory]
    [InlineData("EMPIRE")]
    [InlineData("Rebel Alliance")]
    [InlineData("Pirates")]
    public void Valid_faction_references_pass(string value)
        => Assert.True(Sut.Validate(value, Tag).IsValid);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_value_fails(string value)
        => Assert.False(Sut.Validate(value, Tag).IsValid);
}

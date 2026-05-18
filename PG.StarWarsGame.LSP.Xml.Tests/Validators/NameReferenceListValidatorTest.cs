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

public sealed class NameReferenceListValidatorTest
{
    private static readonly NameReferenceListValidator Sut = new();
    private static readonly XmlTagDefinition Tag = TagOf.Make("Units", XmlValueType.NameReferenceList);

    [Theory]
    [InlineData("UNIT_STORMTROOPER")]
    [InlineData("UNIT_VADER UNIT_STORMTROOPER")]
    [InlineData("Unit_A Unit_B Unit_C")]
    public void Valid_name_reference_lists_pass(string value)
        => Assert.True(Sut.Validate(value, Tag).IsValid);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_value_fails(string value)
        => Assert.False(Sut.Validate(value, Tag).IsValid);
}

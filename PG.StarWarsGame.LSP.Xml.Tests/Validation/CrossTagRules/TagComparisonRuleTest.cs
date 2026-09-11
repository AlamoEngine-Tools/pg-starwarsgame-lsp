// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Abstractions.TestingHelpers;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;
using PG.StarWarsGame.LSP.Xml.Validation;
using PG.StarWarsGame.LSP.Xml.Validation.CrossTagRules;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.CrossTagRules;

/// <summary>
///     The two relations the engine states between one numeric tag and another.
/// </summary>
/// <remarks>
///     The boundary is the point of each: <c>Damage_Radius</c> must be strictly less than
///     <c>Chase_Radius</c>, while <c>Min_Respawn_Time</c> may equal <c>Max_Respawn_Time</c> - that
///     pins the respawn to an exact delay and is a legitimate setting.
/// </remarks>
public sealed class TagComparisonRuleTest
{
    private const string Uri = "file:///abilities/Abilities.xml";

    [Theory]
    [InlineData("100", "500", true)]
    [InlineData("499.9", "500", true)]
    [InlineData("500", "500", false)]
    [InlineData("600", "500", false)]
    public void Damage_radius_must_be_strictly_inside_the_chase_radius(
        string damage, string chase, bool accepted)
    {
        var facts = Run(new DamageRadiusWithinChaseRadiusRule(),
            $"<Damage_Radius>{damage}</Damage_Radius><Chase_Radius>{chase}</Chase_Radius>");

        if (accepted)
        {
            Assert.Empty(facts);
            return;
        }

        var fact = Assert.Single(facts);
        Assert.Equal("Damage_Radius", fact.LeftTag);
        Assert.Equal("Chase_Radius", fact.RightTag);
    }

    // Equality is meaningful here, unlike the radius pair.
    [Theory]
    [InlineData("10", "20", true)]
    [InlineData("20", "20", true)]
    [InlineData("21", "20", false)]
    public void A_respawn_window_may_be_a_single_instant_but_not_reversed(
        string min, string max, bool accepted)
    {
        var facts = Run(new RespawnTimeOrderRule(),
            $"<Min_Respawn_Time>{min}</Min_Respawn_Time><Max_Respawn_Time>{max}</Max_Respawn_Time>");

        Assert.Equal(accepted ? 0 : 1, facts.Count);
    }

    // One tag alone says nothing about the other. Damage_Radius also lives on ShieldFlareAbility,
    // which has no Chase_Radius, so this is what keeps the rule off it.
    [Theory]
    [InlineData("<Damage_Radius>900</Damage_Radius>")]
    [InlineData("<Chase_Radius>100</Chase_Radius>")]
    [InlineData("")]
    public void A_missing_side_is_silent(string body)
    {
        Assert.Empty(Run(new DamageRadiusWithinChaseRadiusRule(), body));
    }

    // A non-numeric value is the type check's business; two complaints for one typo helps nobody.
    [Theory]
    [InlineData("abc", "500")]
    [InlineData("100", "")]
    public void A_value_that_is_not_a_number_is_left_alone(string damage, string chase)
    {
        Assert.Empty(Run(new DamageRadiusWithinChaseRadiusRule(),
            $"<Damage_Radius>{damage}</Damage_Radius><Chase_Radius>{chase}</Chase_Radius>"));
    }

    // The engine keeps the LAST occurrence of a repeated tag, so the comparison must too.
    [Fact]
    public void A_repeated_tag_is_judged_on_its_last_value()
    {
        Assert.Empty(Run(new DamageRadiusWithinChaseRadiusRule(),
            "<Damage_Radius>900</Damage_Radius><Damage_Radius>100</Damage_Radius>"
            + "<Chase_Radius>500</Chase_Radius>"));
    }

    private static IReadOnlyList<TagComparisonFact> Run(IXmlCrossTagRule rule, string body)
    {
        var producer = new XmlDocumentFactProducer(
            new FileHelper(new MockFileSystem()),
            new EmptySchemaProvider(),
            new EmptyFileTypeRegistry(),
            new XmlStructuralValidator(),
            [rule]);

        return producer.Produce($"<Root><Obj>{body}</Obj></Root>", Uri)
            .OfType<TagComparisonFact>()
            .ToList();
    }
}

file sealed class EmptyFileTypeRegistry : IFileTypeRegistry
{
    public IReadOnlyDictionary<string, ImmutableArray<string>> All =>
        new Dictionary<string, ImmutableArray<string>>();

    public ImmutableArray<string> GetTypesForFile(string _)
    {
        return ImmutableArray<string>.Empty;
    }

    public void RegisterFile(string fileUri, ImmutableArray<string> typeNames)
    {
    }

    public void UnregisterFile(string fileUri)
    {
    }
}

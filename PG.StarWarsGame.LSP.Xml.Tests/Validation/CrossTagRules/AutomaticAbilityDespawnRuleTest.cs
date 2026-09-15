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
///     An automatic ability that also despawns its owner.
/// </summary>
/// <remarks>
///     <para>
///         <c>Error: (%s) Automatic special abilities must have Causes_Despawn=false.</c>
///         (<c>01503518</c>) from <c>SpecialAbilityClass::Validate_Data</c>, which then sets
///         <c>CausesDespawn = false</c> itself.
///     </para>
///     <para>
///         Which styles count as automatic is MEASURED from that function's switch rather than
///         inferred from the names - six cases fall into the complaining arm and six into the arm
///         that does nothing. The rule lives on the base class, so it applies to every ability type
///         and is scoped by the two tags being present rather than by an element name.
///     </para>
///     <para>
///         Measured on the shipped corpus: 62 objects carry both tags, 44 of them with an automatic
///         style, and not one sets <c>Causes_Despawn</c>. The branch is exercised by vanilla and the
///         rule is silent on it.
///     </para>
/// </remarks>
public sealed class AutomaticAbilityDespawnRuleTest
{
    private const string Uri = "file:///abilities/Abilities.xml";

    /// <summary>The six cases in the complaining arm of the switch.</summary>
    public static TheoryData<string> AutomaticStyles =>
    [
        "Global_Automatic", "Combat_Automatic", "Galactic_Automatic",
        "Space_Automatic", "Ground_Automatic", "Skirmish_Automatic"
    ];

    /// <summary>The six cases that break out of the switch without a word.</summary>
    public static TheoryData<string> ManualStyles =>
    [
        "Ground_Activated", "Hero_Detected", "Combat_Imminent",
        "Special_Attack", "Take_Damage", "User_Input"
    ];

    [Theory]
    [MemberData(nameof(AutomaticStyles))]
    public void An_automatic_style_that_despawns_is_reported(string style)
    {
        var fact = Assert.Single(Reported(Run(Ability("Spawn_Ability", style, "Yes"))));

        Assert.Equal(style, fact.ActivationStyle);
    }

    [Theory]
    [MemberData(nameof(ManualStyles))]
    public void A_manual_style_that_despawns_is_silent(string style)
    {
        Assert.Empty(Reported(Run(Ability("Spawn_Ability", style, "Yes"))));
    }

    [Theory]
    [MemberData(nameof(AutomaticStyles))]
    public void An_automatic_style_that_does_not_despawn_is_silent(string style)
    {
        Assert.Empty(Reported(Run(Ability("Spawn_Ability", style, "No"))));
    }

    /// <summary>An absent boolean parses as false, so the ability is fine.</summary>
    [Fact]
    public void An_absent_despawn_flag_is_silent()
    {
        Assert.Empty(Reported(Run(
            "<Spawn_Ability Name='A'><Activation_Style>Ground_Automatic</Activation_Style></Spawn_Ability>")));
    }

    /// <summary>The engine uppercases before hashing, so the style matches by meaning.</summary>
    [Theory]
    [InlineData("GROUND_AUTOMATIC")]
    [InlineData("ground_automatic")]
    [InlineData("  Ground_Automatic  ")]
    public void The_style_is_matched_case_insensitively(string style)
    {
        Assert.Single(Reported(Run(Ability("Spawn_Ability", style, "Yes"))));
    }

    /// <summary>
    ///     The rule is stated on the base class, so it is every ability type's rule - it must not be
    ///     scoped to an element name.
    /// </summary>
    [Theory]
    [InlineData("Spawn_Ability")]
    [InlineData("Stun_Ability")]
    [InlineData("Force_Lightning_Ability")]
    public void It_applies_to_any_ability_element(string element)
    {
        Assert.Single(Reported(Run(Ability(element, "Skirmish_Automatic", "Yes"))));
    }

    /// <summary>
    ///     A style the enum does not know is the enum handler's to report. Guessing that an
    ///     unrecognised value is automatic would put a second, contradictory diagnostic on one typo.
    /// </summary>
    [Fact]
    public void An_unknown_style_is_left_alone()
    {
        Assert.Empty(Reported(Run(Ability("Spawn_Ability", "Sometimes_Automatic", "Yes"))));
    }

    private static string Ability(string element, string style, string despawn)
    {
        return $"<{element} Name='A'><Activation_Style>{style}</Activation_Style>"
               + $"<Causes_Despawn>{despawn}</Causes_Despawn></{element}>";
    }

    private static IReadOnlyList<AutomaticDespawnFact> Reported(IEnumerable<XmlFact> facts)
    {
        return facts.OfType<AutomaticDespawnFact>().ToList();
    }

    private static IReadOnlyList<XmlFact> Run(string body)
    {
        var producer = new XmlDocumentFactProducer(
            new FileHelper(new MockFileSystem()),
            new EmptySchemaProvider(),
            new EmptyFileTypeRegistry(),
            new XmlStructuralValidator(),
            [new AutomaticAbilityDespawnRule()]);

        return producer.Produce($"<Root>{body}</Root>", Uri);
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
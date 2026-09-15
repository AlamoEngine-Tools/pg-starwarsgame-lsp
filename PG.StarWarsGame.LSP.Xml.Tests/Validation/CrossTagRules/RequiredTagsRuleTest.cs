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
///     The two remaining required-tag asserts the engine states, on the shared rule base.
/// </summary>
/// <remarks>
///     <para>
///         <c>You must specify a Bomb_Type. Defaulting to "Demolition_Bomb".</c> (<c>01552660</c>)
///         and <c>You must specify an Attack_Animation!</c> (<c>0155122c</c>). Each owner was
///         attributed by the xref to the string rather than by where our schema carries the tag,
///         and the two answers differ - which is the whole point of these tests.
///     </para>
///     <para>
///         Measured on the shipped corpus: 19 instances of the three attack abilities across
///         <c>eaw/</c> and <c>foc/</c>, every one of which sets <c>Attack_Animation</c>. No vanilla
///         object declares a <c>Demolition_Ability</c> at all, so that half is silent on shipped
///         data and real for mods.
///     </para>
/// </remarks>
public sealed class RequiredTagsRuleTest
{
    private const string Uri = "file:///abilities/Abilities.xml";

    /// <summary>
    ///     The three classes whose <c>Validate_Data</c> references the Attack_Animation string:
    ///     ArcSweepAttackAbilityClass (00ff5e4b), GenericAttackAbilityClass (0100a95b) and
    ///     EatAttackAbilityClass (0100046b).
    /// </summary>
    public static TheoryData<string, string> AttackAbilities => new()
    {
        { "Arc_Sweep_Attack_Ability", "ArcSweepAttackAbility" },
        { "Generic_Attack_Ability", "GenericAttackAbility" },
        { "Eat_Attack_Ability", "EatAttackAbility" }
    };

    [Theory]
    [MemberData(nameof(AttackAbilities))]
    public void An_attack_ability_without_an_animation_is_reported(string element, string owningType)
    {
        var fact = Assert.Single(Missing(Run($"<{element} Name='A'><Damage_Amount>10</Damage_Amount></{element}>")));

        Assert.Equal("Attack_Animation", fact.TagName);
        Assert.Equal(owningType, fact.OwningType);
    }

    [Theory]
    [MemberData(nameof(AttackAbilities))]
    public void An_attack_ability_with_an_animation_is_silent(string element, string owningType)
    {
        Assert.Empty(Missing(Run(
            $"<{element} Name='A'><Attack_Animation>ATTACK</Attack_Animation></{element}>")));
        Assert.NotEmpty(owningType);
    }

    [Fact]
    public void A_demolition_ability_without_a_bomb_type_is_reported()
    {
        var fact = Assert.Single(Missing(Run(
            "<Demolition_Ability Name='D'><Damage_Percentage>0.5</Damage_Percentage></Demolition_Ability>")));

        Assert.Equal("Bomb_Type", fact.TagName);
        Assert.Equal("DemolitionAbility", fact.OwningType);
    }

    /// <summary>
    ///     The engine repairs rather than refuses, and the author is entitled to know what it
    ///     silently picked.
    /// </summary>
    [Fact]
    public void The_bomb_type_report_names_the_default_the_engine_substitutes()
    {
        var fact = Assert.Single(Missing(Run("<Demolition_Ability Name='D'></Demolition_Ability>")));

        Assert.Contains("Demolition_Bomb", fact.Repair);
    }

    [Fact]
    public void A_demolition_ability_with_a_bomb_type_is_silent()
    {
        Assert.Empty(Missing(Run(
            "<Demolition_Ability Name='D'><Bomb_Type>Demolition_Bomb</Bomb_Type></Demolition_Ability>")));
    }

    /// <summary>
    ///     The scoping case, and the reason the owner was taken from the xref rather than from our
    ///     own schema: <c>Bomb_Type</c> is declared on three ability types, and only
    ///     <c>DemolitionAbilityClass::Validate_Data</c> demands it. Both of these ship in FoC
    ///     (<c>Units_space_rebel_mc30_frigate.xml</c>, <c>Minor_heroes_expansion.xml</c>) and both
    ///     set the tag, so this guards a rule that would be silent on vanilla either way.
    /// </summary>
    [Theory]
    [InlineData("Cluster_Bomb_Ability")]
    [InlineData("Remote_Bomb_Ability")]
    public void Only_the_demolition_ability_requires_a_bomb_type(string element)
    {
        var missing = Missing(Run($"<{element} Name='B'><Damage_Radius>50</Damage_Radius></{element}>"));

        // Specifically about Bomb_Type, not about silence: Remote_Bomb_Ability has a required tag of
        // its own (Toss_Anim), and this fixture legitimately lacks it.
        Assert.DoesNotContain(missing, f => f.TagName == "Bomb_Type");
    }

    // Inherited from the shared base: the engine's complaint is that the value was not SET.
    [Fact]
    public void An_empty_tag_counts_as_unset()
    {
        Assert.Equal("Attack_Animation",
            Assert.Single(Missing(Run(
                    "<Eat_Attack_Ability Name='A'><Attack_Animation>  </Attack_Animation></Eat_Attack_Ability>")))
                .TagName);
    }

    /// <summary>The seven Leech_Shields tags still behave exactly as they did before the rebase.</summary>
    [Fact]
    public void The_leech_shields_rule_still_reports_all_seven()
    {
        Assert.Equal(7, Missing(Run("<Leech_Shields_Ability Name='K'></Leech_Shields_Ability>")).Count);
    }

    /// <summary>
    ///     A WEAPON hardpoint has to declare both fire-cone angles, because the engine's defaults
    ///     cannot satisfy the check it then makes.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <c>HardPointDataClass</c>'s constructor leaves <c>FireConeWidth</c> and
    ///         <c>FireConeHeight</c> at <c>0.0</c>, and <c>Can_Weapon_Point_At</c> asserts both are
    ///         <c>&gt; 0</c> (<c>HardPoint.cpp:0x741</c> and <c>:0x742</c>) before it does anything
    ///         else. A weapon hardpoint that declares neither can therefore point at nothing.
    ///     </para>
    ///     <para>
    ///         Gated on the hardpoint's <c>Type</c>, because the asserts sit behind
    ///         <c>Is_Weapon()</c>: a shield generator or an engine has no business declaring a cone.
    ///     </para>
    ///     <para>
    ///         Measured: 439 <c>HARD_POINT_WEAPON_*</c> definitions across the two trees, ONE of
    ///         which declares neither - <c>HP_KEDALBE_SHIELD_LEECH_00</c>. The game asserting on its
    ///         own data is not a veto here.
    ///     </para>
    /// </remarks>
    [Theory]
    [InlineData("HARD_POINT_WEAPON_LASER")]
    [InlineData("HARD_POINT_WEAPON_ION_CANNON")]
    [InlineData("HARD_POINT_WEAPON_MISSILE")]
    public void A_weapon_hardpoint_without_a_fire_cone_is_reported(string type)
    {
        var missing = Missing(Run(
            $"<HardPoint Name='HP'><Type>{type}</Type></HardPoint>"));

        Assert.Equal(
            ["Fire_Cone_Width", "Fire_Cone_Height"],
            missing.Select(f => f.TagName).ToArray());
    }

    [Fact]
    public void A_weapon_hardpoint_with_both_angles_is_silent()
    {
        Assert.Empty(Missing(Run(
            "<HardPoint Name='HP'><Type>HARD_POINT_WEAPON_LASER</Type>"
            + "<Fire_Cone_Width>60</Fire_Cone_Width>"
            + "<Fire_Cone_Height>30</Fire_Cone_Height></HardPoint>")));
    }

    /// <summary>Only one of the two is just as broken as neither.</summary>
    [Fact]
    public void A_weapon_hardpoint_with_only_one_angle_is_reported_for_the_other()
    {
        var missing = Missing(Run(
            "<HardPoint Name='HP'><Type>HARD_POINT_WEAPON_LASER</Type>"
            + "<Fire_Cone_Width>60</Fire_Cone_Width></HardPoint>"));

        Assert.Equal("Fire_Cone_Height", Assert.Single(missing).TagName);
    }

    /// <summary>
    ///     The gate: the asserts sit behind <c>Is_Weapon()</c>, so a hardpoint that is not one is
    ///     not this rule's business however little it declares.
    /// </summary>
    [Theory]
    [InlineData("HARD_POINT_SHIELD_GENERATOR")]
    [InlineData("HARD_POINT_ENGINE")]
    [InlineData("HARD_POINT_FIGHTER_BAY")]
    [InlineData("HARD_POINT_DUMMY_ART")]
    public void A_non_weapon_hardpoint_needs_no_fire_cone(string type)
    {
        Assert.Empty(Missing(Run($"<HardPoint Name='HP'><Type>{type}</Type></HardPoint>")));
    }

    /// <summary>A hardpoint that names no type at all cannot be judged either way.</summary>
    [Fact]
    public void A_hardpoint_with_no_type_is_left_alone()
    {
        Assert.Empty(Missing(Run("<HardPoint Name='HP'></HardPoint>")));
    }

    private static IReadOnlyList<MissingRequiredTagFact> Missing(IEnumerable<XmlFact> facts)
    {
        return facts.OfType<MissingRequiredTagFact>().ToList();
    }

    private static IReadOnlyList<XmlFact> Run(string body)
    {
        var producer = new XmlDocumentFactProducer(
            new FileHelper(new MockFileSystem()),
            new EmptySchemaProvider(),
            new EmptyFileTypeRegistry(),
            new XmlStructuralValidator(),
            CrossTagRuleSets.RequiredTags());

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
// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

/// <summary>
///     A list whose FIRST entry the engine requires to be a particular name.
/// </summary>
/// <remarks>
///     <para>
///         From the assert seam rather than from any engine message - there is no message.
///         <c>GameConstants.cpp:1170</c> states <c>DamageTypeNames[ 0 ] == "Damage_Default"</c> and
///         <c>:1180</c> states <c>ArmorTypeNames[ 0 ] == "Armor_Default"</c>, both assert-shaped, so
///         the expression is the condition that must hold.
///     </para>
///     <para>
///         Index 0 is the fallback the engine hands out when a lookup misses, so PREPENDING a type
///         silently repoints every default in the game while appending one is safe. Nothing in the
///         shipped build says so - the assert is compiled out of the Gold build.
///     </para>
///     <para>
///         Measured on the corpus: <c>Damage_Types</c> 81 entries in eaw and 101 in foc,
///         <c>Armor_Types</c> 46 and 54, every one of the four starting with the required name. The
///         rule reports zero on vanilla.
///     </para>
/// </remarks>
public sealed class RequiredFirstEntryHandlerTest
{
    private static readonly XmlTagDefinition DamageTypes = new()
    {
        Tag = "Damage_Types",
        ValueType = XmlValueType.NameReferenceList
    };

    private static readonly XmlTagDefinition ArmorTypes = new()
    {
        Tag = "Armor_Types",
        ValueType = XmlValueType.NameReferenceList
    };

    [Fact]
    public void A_damage_list_not_starting_with_the_default_is_reported()
    {
        var d = Assert.Single(Run(DamageTypes, "Damage_Explosive, Damage_Default, Damage_Laser"));

        Assert.Equal(XmlDiagnosticSeverity.Error, d.Severity);
        Assert.Contains("Damage_Default", d.Message);
        Assert.Contains("first", d.Message);
    }

    [Fact]
    public void A_damage_list_starting_with_the_default_is_silent()
    {
        Assert.Empty(Run(DamageTypes, "Damage_Default, Damage_Explosive"));
    }

    [Fact]
    public void The_armor_list_carries_its_own_required_name()
    {
        var d = Assert.Single(Run(ArmorTypes, "Armor_Light, Armor_Default"));

        Assert.Contains("Armor_Default", d.Message);
        Assert.DoesNotContain("Damage_Default", d.Message);
    }

    [Fact]
    public void The_armor_list_starting_with_its_default_is_silent()
    {
        Assert.Empty(Run(ArmorTypes, "Armor_Default, Armor_Light"));
    }

    /// <summary>Whitespace around entries is the author's business, not a violation.</summary>
    [Fact]
    public void Leading_whitespace_does_not_make_it_a_violation()
    {
        Assert.Empty(Run(DamageTypes, "   Damage_Default ,Damage_Explosive"));
    }

    /// <summary>
    ///     The engine uppercases before hashing every name it resolves, so the comparison it makes
    ///     here is not the place to be stricter than it is.
    /// </summary>
    [Fact]
    public void Casing_is_not_a_violation()
    {
        Assert.Empty(Run(DamageTypes, "DAMAGE_DEFAULT, Damage_Explosive"));
    }

    /// <summary>
    ///     A tag with no entry in the table is not this handler's business - it is opted in per
    ///     (owner, tag), because the required name is an engine fact and differs per list.
    /// </summary>
    [Fact]
    public void An_unlisted_tag_is_silent()
    {
        var other = new XmlTagDefinition { Tag = "Projectile_Types", ValueType = XmlValueType.NameReferenceList };

        Assert.Empty(Run(other, "Anything_At_All"));
    }

    [Fact]
    public void An_empty_list_is_left_to_the_list_handler()
    {
        Assert.Empty(Run(DamageTypes, "   "));
    }

    private static IReadOnlyList<XmlDiagnosticResult> Run(XmlTagDefinition tag, string value)
    {
        var fact = new XmlTagValueFact("file:///Data/Xml/GameConstants.xml", 4, 2, value.Length,
            tag, value, "GameConstants");

        return new RequiredFirstEntryHandler().Handle(fact, XmlHandlerTestFixtures.EmptyCtx).ToList();
    }
}

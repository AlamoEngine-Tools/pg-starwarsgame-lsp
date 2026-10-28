// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Core.Tests.Symbols;

/// <summary>
///     The one place a tag value is read off an effective object: the trimmed text, and the number
///     or the flag the game reads from it. The preview, the encyclopedia and the validation handlers
///     all go through these, so a repeated or untrimmed tag reads the same everywhere.
/// </summary>
public sealed class EffectiveObjectTest
{
    private static EffectiveObject With(params (string Tag, string Value)[] tags)
    {
        return new EffectiveObject("Obj", "GameObjectType", true, false, null, ["Obj"],
            [.. tags.Select(t => new EffectiveTag(t.Tag, t.Value, t.Value, VariantProvenance.Own, "Obj", null))]);
    }

    private static EffectiveObject WithTags(params EffectiveTag[] tags)
    {
        return new EffectiveObject("Obj", "GameObjectType", true, false, null, ["Obj", "Base"], [.. tags]);
    }

    private static EffectiveTag Entry(string tag, string value, VariantProvenance provenance,
        string? baseValue = null)
    {
        return new EffectiveTag(tag, value, $"<{tag}>{value}</{tag}>", provenance,
            provenance == VariantProvenance.Inherited ? "Base" : "Obj", null, baseValue);
    }

    // ── ValueOf ──────────────────────────────────────────────────────────────

    [Fact]
    public void ValueOf_returns_the_tag_value_trimmed()
    {
        Assert.Equal("1", With(("Special_Weapon_Index", "  1 ")).ValueOf("Special_Weapon_Index"));
    }

    // Element names are case-insensitive to the engine, so the lookup is too.
    [Fact]
    public void ValueOf_matches_the_tag_name_case_insensitively()
    {
        Assert.Equal("1", With(("special_weapon_index", "1")).ValueOf("Special_Weapon_Index"));
    }

    // A repeated scalar: the engine keeps the last write.
    [Fact]
    public void ValueOf_takes_the_last_occurrence()
    {
        Assert.Equal("2",
            With(("Special_Weapon_Index", "1"), ("Special_Weapon_Index", "2")).ValueOf("Special_Weapon_Index"));
    }

    [Fact]
    public void ValueOf_is_null_for_a_tag_the_object_does_not_carry()
    {
        Assert.Null(With(("Tactical_Health", "100")).ValueOf("Special_Weapon_Index"));
    }

    // An empty element writes no value: to every reader it is the same as the tag not being there.
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ValueOf_is_null_for_a_blank_value(string blank)
    {
        Assert.Null(With(("Space_Model_Name", blank)).ValueOf("Space_Model_Name"));
    }

    [Fact]
    public void ValueOf_is_null_on_an_object_that_was_not_found()
    {
        var missing = new EffectiveObject("Nope", null, false, false, null,
            ImmutableArray<string>.Empty, ImmutableArray<EffectiveTag>.Empty);

        Assert.Null(missing.ValueOf("Special_Weapon_Index"));
    }

    // ── FloatOf ──────────────────────────────────────────────────────────────

    /// <summary>The last occurrence, read as the game reads a float: blanks and the suffix do not count.</summary>
    [Fact]
    public void FloatOf_reads_the_last_occurrence_the_way_the_game_does()
    {
        Assert.Equal(2.5f, With(("Scale_Factor", "1.0"), ("Scale_Factor", " 2.5f ")).FloatOf("Scale_Factor"));
    }

    [Theory]
    [InlineData("2.0ff", 2.0f)]
    [InlineData("1,5", 1f)]
    [InlineData("7abc", 7f)]
    public void FloatOf_takes_the_number_at_the_front_of_the_value(string written, float expected)
    {
        Assert.Equal(expected, With(("Spin_Away_On_Death_Time", written)).FloatOf("Spin_Away_On_Death_Time"));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("")]
    public void FloatOf_is_null_when_no_number_starts_the_value(string written)
    {
        Assert.Null(With(("Scale_Factor", written)).FloatOf("Scale_Factor"));
    }

    [Fact]
    public void FloatOf_is_null_for_an_absent_tag()
    {
        Assert.Null(With(("Tactical_Health", "100")).FloatOf("Scale_Factor"));
    }

    // ── IntOf ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("3", 3)]
    [InlineData(" -1 ", -1)]
    // The engine's integer tags are written as floats often enough to matter - Projectile_Blast_Area_Max_Victims
    // ships as 1.0 in every one of its three rows - and the game reads the whole part.
    [InlineData("1.0", 1)]
    [InlineData("2.9", 2)]
    [InlineData("3f", 3)]
    public void IntOf_reads_the_whole_number_at_the_front_of_the_value(string written, int expected)
    {
        Assert.Equal(expected, With(("Tech_Level", written)).IntOf("Tech_Level"));
    }

    [Fact]
    public void IntOf_takes_the_last_occurrence()
    {
        Assert.Equal(4, With(("Tech_Level", "3"), ("Tech_Level", "4")).IntOf("Tech_Level"));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData(".5")]
    [InlineData("")]
    public void IntOf_is_null_when_no_digit_starts_the_value(string written)
    {
        Assert.Null(With(("Tech_Level", written)).IntOf("Tech_Level"));
    }

    [Fact]
    public void IntOf_is_null_for_an_absent_tag()
    {
        Assert.Null(With(("Tactical_Health", "100")).IntOf("Tech_Level"));
    }

    // ── BoolOf ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Yes")]
    [InlineData("true")]
    [InlineData(" 1 ")]
    public void BoolOf_is_true_for_an_affirmative_spelling(string written)
    {
        Assert.True(With(("Spin_Away_On_Death", written)).BoolOf("Spin_Away_On_Death"));
    }

    [Theory]
    [InlineData("No")]
    [InlineData("false")]
    [InlineData("0")]
    [InlineData("maybe")]
    [InlineData("")]
    public void BoolOf_is_false_for_a_denial_and_for_a_spelling_the_engine_does_not_know(string written)
    {
        Assert.False(With(("Spin_Away_On_Death", written)).BoolOf("Spin_Away_On_Death"));
    }

    [Fact]
    public void BoolOf_is_false_for_an_absent_tag()
    {
        Assert.False(With(("Tactical_Health", "100")).BoolOf("Spin_Away_On_Death"));
    }

    [Fact]
    public void BoolOf_takes_the_last_occurrence()
    {
        Assert.True(With(("Is_Turret", "No"), ("Is_Turret", "Yes")).BoolOf("Is_Turret"));
        Assert.False(With(("Is_Turret", "Yes"), ("Is_Turret", "No")).BoolOf("Is_Turret"));
    }

    // ── FragmentOf ───────────────────────────────────────────────────────────

    /// <summary>The outer XML, for the sub-object lists whose value is only whitespace.</summary>
    [Fact]
    public void FragmentOf_gives_the_last_occurrences_outer_xml()
    {
        var effective = WithTags(
            Entry("Unit_Abilities_Data", "a", VariantProvenance.Own),
            Entry("Unit_Abilities_Data", "b", VariantProvenance.Own));

        Assert.Equal("<Unit_Abilities_Data>b</Unit_Abilities_Data>", effective.FragmentOf("unit_abilities_data"));
    }

    [Fact]
    public void FragmentOf_is_null_for_an_absent_tag()
    {
        Assert.Null(With(("Tactical_Health", "100")).FragmentOf("Unit_Abilities_Data"));
    }

    // ── DisplacingTag ────────────────────────────────────────────────────────

    /// <summary>The object's own entry that replaced a base value, with what it replaced.</summary>
    [Fact]
    public void DisplacingTag_gives_the_entry_that_overrides_the_base()
    {
        var effective = WithTags(
            Entry("Tactical_Health", "100", VariantProvenance.Inherited),
            Entry("Scale_Factor", "2.0", VariantProvenance.Overridden, "1.0"));

        var tag = effective.DisplacingTag("scale_factor");

        Assert.NotNull(tag);
        Assert.Equal(VariantProvenance.Overridden, tag.Provenance);
        Assert.Equal("1.0", tag.BaseValue);
    }

    /// <summary>
    ///     A repeatable additive tag contributes one entry per occurrence, the base's inherited ones
    ///     first, so the displacing entry is not simply the first with the name.
    /// </summary>
    [Fact]
    public void DisplacingTag_skips_the_inherited_occurrences_of_an_additive_tag()
    {
        var effective = WithTags(
            Entry("Death_Clone", "Damage_Normal, Base_Clone", VariantProvenance.Inherited),
            Entry("Death_Clone", "Damage_Fire, Own_Clone", VariantProvenance.Merged, "Damage_Normal, Base_Clone"));

        var tag = effective.DisplacingTag("Death_Clone");

        Assert.NotNull(tag);
        Assert.Equal(VariantProvenance.Merged, tag.Provenance);
        Assert.Equal("Damage_Fire, Own_Clone", tag.Value);
    }

    [Theory]
    [InlineData(VariantProvenance.Inherited)]
    [InlineData(VariantProvenance.Added)]
    [InlineData(VariantProvenance.Own)]
    public void DisplacingTag_is_null_when_the_object_displaced_nothing(VariantProvenance provenance)
    {
        Assert.Null(WithTags(Entry("Scale_Factor", "2.0", provenance)).DisplacingTag("Scale_Factor"));
    }

    [Fact]
    public void DisplacingTag_is_null_for_an_absent_tag()
    {
        Assert.Null(WithTags(Entry("Scale_Factor", "2.0", VariantProvenance.Overridden, "1.0"))
            .DisplacingTag("Tactical_Health"));
    }
}
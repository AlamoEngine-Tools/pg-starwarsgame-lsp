// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

/// <summary>
///     The value the engine substitutes, looked up per owning type and tag.
/// </summary>
/// <remarks>
///     <para>
///         Keyed on <c>(owner, tag)</c> and not on the range rule, because one rule serves many
///         owners with different repairs: <c>fraction-below-one</c> covers both
///         <c>Time_Reduction_Percentage</c>, which the engine resets to zero, and
///         <c>Owner_Income_Percentage</c>, which it clamps to 0.99. A repair hung on the handler
///         would be right for one of them and wrong for the other.
///     </para>
///     <para>
///         Deliberately NOT in the schema. The schema says what is legal; this says what the engine
///         does about the illegal, which never changes whether a document validates - and these are
///         2018 measurements that will be revisited against the 64-bit build.
///     </para>
/// </remarks>
public sealed class EngineValueRepairsTest
{
    [Theory]
    // LeechShieldsAbilityClass::Validate_Data assigns 0.0 to both ranges.
    [InlineData("LeechShieldsAbility", "Activation_Min_Range", "-5", "0.0")]
    [InlineData("LeechShieldsAbility", "Activation_Max_Range", "-1.5", "0.0")]
    public void A_measured_repair_is_offered_for_its_own_owner(
        string owner, string tag, string value, string expected)
    {
        var d = Assert.Single(new NonNegativeValueHandler().Handle(
            Fact(owner, tag, value, XmlValueType.Float), XmlHandlerTestFixtures.EmptyCtx));

        Assert.Equal(expected, d.SuggestedFix);
        Assert.Contains("engine", d.FixTitle!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The same rule on a tag nobody measured offers nothing at all.</summary>
    [Fact]
    public void An_unmeasured_tag_under_the_same_rule_offers_nothing()
    {
        var d = Assert.Single(new NonNegativeValueHandler().Handle(
            Fact("StunAbility", "Stun_Range", "-3", XmlValueType.Float),
            XmlHandlerTestFixtures.EmptyCtx));

        Assert.Null(d.SuggestedFix);
        Assert.Null(d.FixTitle);
    }

    /// <summary>
    ///     The same TAG under a different owner is a different rule with a different repair, which
    ///     is the whole reason the table is keyed on both.
    /// </summary>
    [Fact]
    public void The_owner_decides_the_repair()
    {
        var d = Assert.Single(new FractionBelowOneHandler().Handle(
            Fact("PoliticalTransitionBonusAbility", "Time_Reduction_Percentage", "1.5",
                XmlValueType.Float), XmlHandlerTestFixtures.EmptyCtx));

        Assert.Equal("0.0", d.SuggestedFix);
    }

    [Fact]
    public void A_positive_bound_takes_its_owners_default()
    {
        var d = Assert.Single(new PositiveValueHandler().Handle(
            Fact("IncomeStreamAbility", "Base_Interval_In_Secs", "0", XmlValueType.Float),
            XmlHandlerTestFixtures.EmptyCtx));

        Assert.Equal("1.0", d.SuggestedFix);
    }

    /// <summary>
    ///     The duration rule's repair moved out of the handler into the table with everything else,
    ///     so there is one place to look and one place to be wrong.
    /// </summary>
    [Fact]
    public void The_duration_repair_still_comes_through()
    {
        var d = Assert.Single(new AtLeastOneSecondHandler().Handle(
            Fact("LeechShieldsAbility", "Duration_In_Secs", "0.25", XmlValueType.Float),
            XmlHandlerTestFixtures.EmptyCtx));

        Assert.Equal("1.0", d.SuggestedFix);
    }

    /// <summary>
    ///     A fact with no owning type cannot be keyed, and guessing from the tag name alone is the
    ///     mistake this table exists to prevent.
    /// </summary>
    [Fact]
    public void An_unknown_owner_offers_nothing()
    {
        var d = Assert.Single(new AtLeastOneSecondHandler().Handle(
            Fact(null, "Duration_In_Secs", "0.25", XmlValueType.Float),
            XmlHandlerTestFixtures.EmptyCtx));

        Assert.Null(d.SuggestedFix);
    }

    private static XmlTagValueFact Fact(string? owner, string tag, string value, XmlValueType type)
    {
        return new XmlTagValueFact("file:///test.xml", 0, 0, value.Length,
            XmlHandlerTestFixtures.MakeTag(tag, type), value, owner);
    }
}

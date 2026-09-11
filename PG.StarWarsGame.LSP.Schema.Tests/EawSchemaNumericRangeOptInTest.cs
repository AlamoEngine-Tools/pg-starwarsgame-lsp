// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Schema.Yaml;

namespace PG.StarWarsGame.LSP.Schema.Tests;

/// <summary>
///     Numeric range rules are per OWNING TYPE, not per tag name.
/// </summary>
/// <remarks>
///     <para>
///         Two abilities can carry a tag of the same name under different bounds, and the engine
///         states each one separately in its own <c>Validate_Data</c>. Keying on the name alone
///         gives one of the two owners the wrong rule, which is either a false positive or a miss.
///     </para>
///     <para>
///         Each case below was read out of the 2018 binary's decompiled comparison rather than
///         inferred from the message text, because the wording does not always match the
///         comparison - see <c>Percentage_Income_Modifier</c>.
///     </para>
/// </remarks>
public sealed class EawSchemaNumericRangeOptInTest
{
    /// <summary>
    ///     <c>Time_Reduction_Percentage</c> has two owners and two different rules.
    /// </summary>
    /// <remarks>
    ///     <c>PoliticalTransitionBonusAbilityClass::Validate_Data</c> (<c>01016100</c>) tests
    ///     <c>(x &lt; 0.0) || (x >= 1.0)</c> - floored AND capped. <c>ReduceProductionTimeAbility</c>
    ///     (<c>010180b0</c>) tests only <c>1.0 &lt;= x</c>. Both carried the weaker
    ///     <c>below-one</c> until the two messages were traced to their owners, which let a negative
    ///     value through on the political ability.
    /// </remarks>
    [Theory]
    [InlineData("PoliticalTransitionBonusAbility.yaml", "Time_Reduction_Percentage", "fraction-below-one")]
    [InlineData("ReduceProductionTimeAbility.yaml", "Time_Reduction_Percentage", "below-one")]
    public void TimeReductionPercentage_TakesItsOwnersRule(string file, string tag, string expected)
    {
        Assert.Equal(expected, ValidationId(file, tag));
    }

    /// <summary>
    ///     The message says "cannot be less than -1.0" and the code agrees: -1.0 itself is legal.
    /// </summary>
    /// <remarks>
    ///     <c>PlanetIncomeBonusAbilityClass::Validate_Data</c> (<c>01014820</c>) tests
    ///     <c>x &lt;= -1.0 &amp;&amp; x != -1.0</c>, which is just <c>x &lt; -1.0</c>. That is a
    ///     different bound from the eight <c>*_Bonus_Percentage</c> tags, whose message is "cannot
    ///     be -1.0 or less" and which therefore reject -1.0. This tag carried
    ///     <c>bonus-percentage</c>, so it would have flagged a value the engine accepts.
    /// </remarks>
    [Fact]
    public void PercentageIncomeModifier_AllowsExactlyMinusOne()
    {
        Assert.Equal("at-least-negative-one",
            ValidationId("PlanetIncomeBonusAbility.yaml", "Percentage_Income_Modifier"));
    }

    // The exclusive-bound family this was mistaken for, kept alongside so the contrast is pinned.
    [Theory]
    [InlineData("CombatBonusAbility.yaml", "Damage_Bonus_Percentage")]
    [InlineData("CombatBonusAbility.yaml", "Health_Bonus_Percentage")]
    public void BonusPercentageTags_RejectMinusOne(string file, string tag)
    {
        Assert.Equal("bonus-percentage", ValidationId(file, tag));
    }

    /// <summary>
    ///     <c>Damage_Amount</c> is floored at zero for six owners and above zero for two.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Two engine wordings, two comparisons. "cannot be less than zero" (<c>0155129c</c>,
    ///         <c>015529f4</c>) is <c>x &lt;= 0.0 &amp;&amp; x != 0.0</c>, i.e. <c>x &lt; 0</c>;
    ///         "must be greater than zero" (<c>015534a0</c>) is a plain <c>x &lt;= 0.0</c>, which
    ///         rejects zero as well.
    ///     </para>
    ///     <para>
    ///         <c>ForceWhirlwindAbilityClass::Validate_Data</c> (<c>010086d0</c>) has both forms in
    ///         the same function - <c>Activation_Min_Range</c> the permissive one, two lines above
    ///         <c>Damage_Amount</c> the strict one - so the distinction is deliberate, not sloppy
    ///         wording. All ten owners carried the permissive rule, which let a zero-damage force
    ///         attack through on the two that reject it.
    ///     </para>
    /// </remarks>
    [Theory]
    [InlineData("ForceWhirlwindAbility.yaml", "positive-value")]
    [InlineData("ForceLightningAbility.yaml", "positive-value")]
    [InlineData("DemolitionAbility.yaml", "non-negative-value")]
    [InlineData("ArcSweepAttackAbility.yaml", "non-negative-value")]
    [InlineData("GenericAttackAbility.yaml", "non-negative-value")]
    [InlineData("EatAttackAbility.yaml", "non-negative-value")]
    [InlineData("EarthquakeAttackAbility.yaml", "non-negative-value")]
    [InlineData("ForceTelekinesisAbility.yaml", "non-negative-value")]
    public void DamageAmount_TakesItsOwnersRule(string file, string expected)
    {
        Assert.Equal(expected, ValidationId(file, "Damage_Amount"));
    }

    /// <summary>
    ///     <c>Damage_Bonus_Percentage</c> is floored at zero on one owner and at -1.0 on the other.
    /// </summary>
    /// <remarks>
    ///     <c>FindWeaknessAbilityClass::Validate_Data</c> (<c>01004060</c>) tests
    ///     <c>x &lt;= 0.0 &amp;&amp; x != 0.0</c> - a weakness that heals the target makes no sense,
    ///     so nothing below zero. <c>CombatBonusAbility</c> (<c>00ffb810</c>) says "cannot be -1.0
    ///     or less" and does allow a penalty. FindWeakness carried the CombatBonus rule, so
    ///     anything in (-1, 0) passed.
    /// </remarks>
    [Theory]
    [InlineData("FindWeaknessAbility.yaml", "non-negative-value")]
    [InlineData("CombatBonusAbility.yaml", "bonus-percentage")]
    public void DamageBonusPercentage_TakesItsOwnersRule(string file, string expected)
    {
        Assert.Equal(expected, ValidationId(file, "Damage_Bonus_Percentage"));
    }

    /// <summary>
    ///     The two <c>Damage_Amount</c> owners the harvest says nothing about.
    /// </summary>
    /// <remarks>
    ///     <c>HeroClashType</c> and <c>PersonalFlameThrowerAbility</c> declare the tag but no engine
    ///     message names them, so their bound is UNMEASURED. They keep the permissive rule rather
    ///     than inheriting a neighbour's by assumption - pinned so the gap is visible instead of
    ///     looking settled.
    /// </remarks>
    [Theory]
    [InlineData("HeroClashType.yaml")]
    [InlineData("PersonalFlameThrowerAbility.yaml")]
    public void DamageAmount_UnmeasuredOwners_KeepTheFloor(string file)
    {
        Assert.Equal("non-negative-value", ValidationId(file, "Damage_Amount"));
    }

    private static string? ValidationId(string file, string name)
    {
        var tags = YamlSchemaParser.ParseTagFile(File.ReadAllText(Find(file)));
        var tag = tags.FirstOrDefault(t => string.Equals(t.Tag, name, StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(tag);
        return tag!.ValidationOverride?.ValidationId;
    }

    private static string Find(string file)
    {
        var dir = new DirectoryInfo(
            Path.GetDirectoryName(typeof(EawSchemaNumericRangeOptInTest).Assembly.Location)!);
        while (dir is not null)
        {
            if (dir.EnumerateFiles("*.slnx").Any())
            {
                var candidate = Path.Combine(dir.FullName, "schema", "eaw", "tags", file);
                if (File.Exists(candidate)) return candidate;
                break;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException($"schema/eaw/tags/{file} not found.");
    }
}

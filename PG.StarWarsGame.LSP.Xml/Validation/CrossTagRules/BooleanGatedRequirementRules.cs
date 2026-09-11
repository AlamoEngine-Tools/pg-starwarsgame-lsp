// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Xml.Validation.CrossTagRules;

/// <summary>
///     A <c>System_Spy_Ability</c> detail flag needs the summary flag it reads from.
/// </summary>
/// <remarks>
///     All five are stated in <c>SystemSpyAbilityClass::Validate_Data</c> (<c>0101da00</c>) as
///     <c>if (detail != false &amp;&amp; summary == false)</c>, followed by the engine setting the
///     summary flag to true itself. The pattern is consistent: you cannot see the breakdown of
///     something you are not being shown at all.
/// </remarks>
public sealed class SeeFleetContentsNeedsNumFleetsRule : BooleanGatedRequirementRuleBase
{
    protected override string ElementName => "system_spy_ability";
    protected override string GateTag => "See_Fleet_Contents";
    protected override string RequiredTag => "See_Num_Fleets";
}

/// <inheritdoc cref="SeeFleetContentsNeedsNumFleetsRule" />
public sealed class SeeMostPowerfulShipNeedsNumFleetsRule : BooleanGatedRequirementRuleBase
{
    protected override string ElementName => "system_spy_ability";
    protected override string GateTag => "See_Most_Powerful_Ship";
    protected override string RequiredTag => "See_Num_Fleets";
}

/// <inheritdoc cref="SeeFleetContentsNeedsNumFleetsRule" />
public sealed class SeeGroundCompanyContentsNeedsNumCompaniesRule : BooleanGatedRequirementRuleBase
{
    protected override string ElementName => "system_spy_ability";
    protected override string GateTag => "See_Ground_Company_Contents";
    protected override string RequiredTag => "See_Num_Ground_Companies";
}

/// <inheritdoc cref="SeeFleetContentsNeedsNumFleetsRule" />
public sealed class SeeCreditIncomeBreakdownNeedsIncomeRule : BooleanGatedRequirementRuleBase
{
    protected override string ElementName => "system_spy_ability";
    protected override string GateTag => "See_Credit_Income_Breakdown";
    protected override string RequiredTag => "See_Credit_Income";
}

/// <inheritdoc cref="SeeFleetContentsNeedsNumFleetsRule" />
public sealed class SeePoliticalControlBreakdownNeedsControlRule : BooleanGatedRequirementRuleBase
{
    protected override string ElementName => "system_spy_ability";
    protected override string GateTag => "See_Political_Control_Breakdown";
    protected override string RequiredTag => "See_Political_Control";
}

/// <summary>
///     A <c>Galactic_Sabotage_Ability</c> that can halt credit production needs a halt duration.
/// </summary>
/// <remarks>
///     The one rule of the six that gates a NUMBER rather than another flag.
///     <c>GalacticSabotageAbilityClass::Validate_Data</c> (<c>00ef6c30</c>) tests
///     <c>CanHaltCreditProduction == true &amp;&amp; DurationOfCreditHalt &lt;= 0.0</c> and then
///     forces the duration to 10.0 - so leaving it at zero does not disable the halt, it buys a
///     ten-second one at a value nobody chose.
/// </remarks>
public sealed class CreditHaltNeedsDurationRule : BooleanGatedRequirementRuleBase
{
    protected override string ElementName => "galactic_sabotage_ability";
    protected override string GateTag => "Can_Halt_Credit_Production";
    protected override string RequiredTag => "Duration_Of_Credit_Halt";
    protected override string Requirement => "a time greater than zero";
    protected override string Repair => "forces it to 10.0 seconds";

    protected override bool IsSatisfied(string value)
    {
        return IsPositiveNumber(value);
    }
}

/// <summary>Convenience grouping so callers and tests register the same six rules.</summary>
public static class CrossTagRuleSets
{
    public static IReadOnlyList<IXmlCrossTagRule> BooleanGatedRequirements()
    {
        return
        [
            new SeeFleetContentsNeedsNumFleetsRule(),
            new SeeMostPowerfulShipNeedsNumFleetsRule(),
            new SeeGroundCompanyContentsNeedsNumCompaniesRule(),
            new SeeCreditIncomeBreakdownNeedsIncomeRule(),
            new SeePoliticalControlBreakdownNeedsControlRule(),
            new CreditHaltNeedsDurationRule()
        ];
    }
}

// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Xml.Validation.CrossTagRules;

/// <summary>
///     Convenience groupings so callers and tests register the same rules.
/// </summary>
/// <remarks>
///     A family harvested from one engine message shape is declared once here rather than listed by
///     hand at each call site. DI still registers each rule individually, so every one stays
///     separately suppressible and the registration test counts them like any other.
/// </remarks>
public static class CrossTagRuleSets
{
    /// <summary>The engine's six "if you set A to true you must also set B" rules.</summary>
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

    /// <summary>Every "has not been set" / "you must specify" assert the engine states.</summary>
    public static IReadOnlyList<IXmlCrossTagRule> RequiredTags()
    {
        return
        [
            new LeechShieldsRequiredTagsRule(),
            new DemolitionBombTypeRule(),
            new ArcSweepAttackAnimationRule(),
            new GenericAttackAnimationRule(),
            new EatAttackAnimationRule()
        ];
    }

    /// <summary>The engine's three "you should set either A or B" pairs.</summary>
    public static IReadOnlyList<IXmlCrossTagRule> EitherOrRequirements()
    {
        return
        [
            new HeroAssassinTargetsRule(),
            new BaseDestructionTargetsRule(),
            new NeutralizeHeroTargetsRule()
        ];
    }
}

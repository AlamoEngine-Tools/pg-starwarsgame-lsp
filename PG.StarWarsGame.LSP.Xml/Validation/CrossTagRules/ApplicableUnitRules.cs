// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Xml.Validation.CrossTagRules;

/// <summary>
///     An ability that names neither a unit category nor a unit type, and so applies to nothing.
/// </summary>
/// <remarks>
///     <para>
///         One check - <c>categories == 0 &amp;&amp; Get_Count(types) &lt; 1</c> - stated in four
///         messages across nine ability classes, which differ only in how they describe what is
///         lost. Each class below carries the owners that reference its message, taken from the
///         xref rather than from the schema.
///     </para>
///     <para>
///         Found by the re-harvest, not the original one: every message here is labelled
///         <c>Warning</c> and wraps across two string literals, which is two independent reasons the
///         first pass could not see them.
///     </para>
///     <para>
///         Measured on the shipped corpus: 352 objects across the seven owning types vanilla
///         actually uses, and not one leaves both tags empty.
///     </para>
/// </remarks>
public abstract class ApplicableUnitRuleBase : EitherOrRequirementRuleBase
{
    protected override string FirstTag => "Applicable_Unit_Categories";
    protected override string SecondTag => "Applicable_Unit_Types";
    protected override string State => "empty";
    protected override string Remedy => "list at least one unit category or type";

    /// <summary>
    ///     A list is set when it has CONTENT. It must not inherit the boolean reading: a unit
    ///     category named <c>No</c> is a perfectly good category, and the engine counts entries
    ///     rather than reading them.
    /// </summary>
    protected override bool IsSatisfied(string value)
    {
        return value.Length > 0;
    }
}

/// <summary>Message <c>01552078</c>, referenced by three classes.</summary>
public sealed class CombatBonusApplicableUnitsRule : ApplicableUnitRuleBase
{
    protected override IReadOnlyList<string> ElementNames =>
    [
        "combat_bonus_ability",
        "reduce_production_time_ability",
        "reduce_production_price_ability",
    ];

    protected override string Consequence => "affects no units at all";
}

/// <summary>Message <c>015510b8</c>, referenced by <c>AbsorbBlasterAbilityClass</c>.</summary>
public sealed class AbsorbBlasterApplicableUnitsRule : ApplicableUnitRuleBase
{
    protected override IReadOnlyList<string> ElementNames => ["absorb_blaster_ability"];
    protected override string Consequence => "cannot absorb fire from any source";
}

/// <summary>Message <c>01551310</c>, referenced by four attack classes.</summary>
public sealed class AttackAbilityApplicableUnitsRule : ApplicableUnitRuleBase
{
    protected override IReadOnlyList<string> ElementNames =>
    [
        "cable_attack_ability",
        "earthquake_attack_ability",
        "demolition_ability",
        "concentrate_fire_attack_ability",
    ];

    protected override string Consequence => "cannot activate";
}

/// <summary>Message <c>01555f40</c>, referenced by <c>RedirectBlasterAbilityClass</c>.</summary>
public sealed class RedirectBlasterApplicableUnitsRule : ApplicableUnitRuleBase
{
    protected override IReadOnlyList<string> ElementNames => ["redirect_blaster_ability"];
    protected override string Consequence => "cannot block or redirect fire";
}

// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Globalization;
using HtmlAgilityPack;

namespace PG.StarWarsGame.LSP.Xml.Validation.CrossTagRules;

/// <summary>
///     An ability that names neither a unit category nor a unit type, and so applies to nothing.
/// </summary>
/// <remarks>
///     <para>
///         One check - <c>categories == 0 &amp;&amp; Get_Count(types) &lt; 1</c> - stated in four
///         messages across <b>30</b> ability classes, which differ only in how they describe what is
///         lost. Each rule below carries the owners that reference its message, taken from the xref.
///     </para>
///     <para>
///         The first version of this shipped with 9 owners because an xref query capped at 4 results
///         was read as a complete list; the "cannot activate" message alone has 23. Anything derived
///         from a bounded query needs the bound raised until the count stops moving.
///     </para>
///     <para>
///         Measured across all 30: 536 shipped objects, and not one leaves both tags empty.
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

/// <summary>Message <c>01552078</c> - the four owners that ask unconditionally.</summary>
public sealed class CombatBonusApplicableUnitsRule : ApplicableUnitRuleBase
{
    protected override IReadOnlyList<string> ElementNames =>
    [
        "combat_bonus_ability",
        "reduce_production_time_ability",
        "reduce_production_price_ability",
        "find_weakness_ability"
    ];

    protected override string Consequence => "affects no units at all";
}

/// <summary>
///     Message <c>01552078</c> again, for the one owner that asks conditionally.
/// </summary>
/// <remarks>
///     <c>ForceHealingAbilityClass::Validate_Data</c> guards the check with
///     <c>0.0 &lt; HealRange</c>: an ability that heals at no range is never asked which units it
///     applies to. Its own rule rather than a flag on the shared one, because the gate is the whole
///     difference and burying it would make the other four look conditional too.
/// </remarks>
public sealed class ForceHealingApplicableUnitsRule : ApplicableUnitRuleBase
{
    private const string RangeTag = "Heal_Range";

    protected override IReadOnlyList<string> ElementNames => ["force_healing_ability"];
    protected override string Consequence => "affects no units at all";

    protected override bool Applies(IReadOnlyDictionary<string, IReadOnlyList<HtmlNode>> childrenByName)
    {
        if (!childrenByName.TryGetValue(RangeTag, out var nodes)) return false;

        return nodes.Any(n =>
            double.TryParse(n.InnerText.Trim().TrimEnd('f', 'F'), NumberStyles.Float,
                CultureInfo.InvariantCulture, out var range) && range > 0.0);
    }
}

/// <summary>Message <c>015510b8</c>, referenced by <c>AbsorbBlasterAbilityClass</c> alone.</summary>
public sealed class AbsorbBlasterApplicableUnitsRule : ApplicableUnitRuleBase
{
    protected override IReadOnlyList<string> ElementNames => ["absorb_blaster_ability"];
    protected override string Consequence => "cannot absorb fire from any source";
}

/// <summary>Message <c>01555f40</c>, referenced by <c>RedirectBlasterAbilityClass</c> alone.</summary>
public sealed class RedirectBlasterApplicableUnitsRule : ApplicableUnitRuleBase
{
    protected override IReadOnlyList<string> ElementNames => ["redirect_blaster_ability"];
    protected override string Consequence => "cannot block or redirect fire";
}

/// <summary>Message <c>01551310</c> - 23 classes, the widest-reaching rule here.</summary>
public sealed class AttackAbilityApplicableUnitsRule : ApplicableUnitRuleBase
{
    protected override IReadOnlyList<string> ElementNames =>
    [
        "arc_sweep_attack_ability",
        "cable_attack_ability",
        "concentrate_fire_attack_ability",
        "demolition_ability",
        "drain_life_ability",
        "earthquake_attack_ability",
        "eat_attack_ability",
        "energy_weapon_attack_ability",
        "force_lightning_ability",
        "force_telekinesis_ability",
        "force_whirlwind_ability",
        "generic_attack_ability",
        "grenade_attack_ability",
        "hack_ability",
        "ion_cannon_shot_attack_ability",
        "leech_shields_ability",
        "lucky_shot_attack_ability",
        "maximum_firepower_attack_ability",
        "personal_flame_thrower_ability",
        "repair_ability",
        "super_laser_ability",
        "tractor_beam_attack_ability",
        "vehicle_thief_ability"
    ];

    protected override string Consequence => "cannot activate";
}
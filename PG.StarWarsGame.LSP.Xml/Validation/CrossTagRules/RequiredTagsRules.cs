// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Xml.Validation.CrossTagRules;

/// <summary>
///     A <c>Demolition_Ability</c> with no bomb to spawn.
/// </summary>
/// <remarks>
///     <para>
///         <c>DemolitionAbilityClass::Validate_Data</c> refers to the message at <c>01552660</c>:
///         <c>You must specify a Bomb_Type. Defaulting to "Demolition_Bomb".</c> The engine
///         repairs rather than refuses, so the ability runs and spawns a bomb type the author
///         never chose.
///     </para>
///     <para>
///         Only this class. Our schema declares <c>Bomb_Type</c> on
///         <c>Cluster_Bomb_Ability</c> and <c>Remote_Bomb_Ability</c> as well, and neither
///         references the string - taking the owner from the schema rather than from the xref
///         would have invented two rules the engine does not state.
///     </para>
/// </remarks>
public sealed class DemolitionBombTypeRule : RequiredTagsRuleBase
{
    protected override string ElementName => "demolition_ability";
    protected override string OwningType => "DemolitionAbility";
    protected override IReadOnlyList<string> RequiredTags => ["Bomb_Type"];
    protected override string Repair => "defaults it to Demolition_Bomb";

    /// <summary>Measured: the message itself names the substitute, <c>Demolition_Bomb</c>.</summary>
    protected override string? DefaultValue => "Demolition_Bomb";
}

/// <summary>
///     An attack ability with no animation to play.
/// </summary>
/// <remarks>
///     <c>Error: (%s) You must specify an Attack_Animation!</c> (<c>0155122c</c>), referenced by
///     exactly three classes: <c>ArcSweepAttackAbilityClass</c> (<c>00ff5e4b</c>),
///     <c>GenericAttackAbilityClass</c> (<c>0100a95b</c>) and <c>EatAttackAbilityClass</c>
///     (<c>0100046b</c>). All 19 shipped instances across <c>eaw/</c> and <c>foc/</c> set the tag,
///     so the base game agrees with the engine and the rule is silent on vanilla data.
/// </remarks>
public sealed class ArcSweepAttackAnimationRule : RequiredTagsRuleBase
{
    protected override string ElementName => "arc_sweep_attack_ability";
    protected override string OwningType => "ArcSweepAttackAbility";
    protected override IReadOnlyList<string> RequiredTags => ["Attack_Animation"];
}

/// <inheritdoc cref="ArcSweepAttackAnimationRule" />
public sealed class GenericAttackAnimationRule : RequiredTagsRuleBase
{
    protected override string ElementName => "generic_attack_ability";
    protected override string OwningType => "GenericAttackAbility";
    protected override IReadOnlyList<string> RequiredTags => ["Attack_Animation"];
}

/// <inheritdoc cref="ArcSweepAttackAnimationRule" />
public sealed class EatAttackAnimationRule : RequiredTagsRuleBase
{
    protected override string ElementName => "eat_attack_ability";
    protected override string OwningType => "EatAttackAbility";
    protected override IReadOnlyList<string> RequiredTags => ["Attack_Animation"];
}

/// <summary>
///     A bounty with nobody to collect it from.
/// </summary>
/// <remarks>
///     <c>BountyOnFactionAbilityClass::Validate_Data</c> (<c>00ff8a10</c>) warns when the faction
///     list is empty. Its message says <c>Target_Factions</c>, which is a stale field name - the
///     parser table registers <c>Target_Faction_Names</c>, and that is what the XML takes.
/// </remarks>
public sealed class BountyTargetFactionsRule : RequiredTagsRuleBase
{
    protected override string ElementName => "bounty_on_faction_ability";
    protected override string OwningType => "BountyOnFactionAbility";
    protected override IReadOnlyList<string> RequiredTags => ["Target_Faction_Names"];
}

/// <summary>
///     A garrison upgrade that adds no units.
/// </summary>
/// <remarks>
///     <c>GarrisonUpgradeAbilityClass::Validate_Data</c> (<c>0100a35e</c>) counts the list and warns
///     at zero: "otherwise this ability has no effect".
/// </remarks>
public sealed class GarrisonUpgradeUnitsRule : RequiredTagsRuleBase
{
    protected override string ElementName => "garrison_upgrade_ability";
    protected override string OwningType => "GarrisonUpgradeAbility";
    protected override IReadOnlyList<string> RequiredTags => ["Additional_Garrison_Units"];
}

/// <summary>
///     A toss animation the engine could not resolve.
/// </summary>
/// <remarks>
///     <para>
///         Both classes test the RESOLVED animation - <c>AnimType == ANIM_INVALID</c> - rather than
///         the text, so the engine's complaint covers two different mistakes: a tag that is absent,
///         and one naming an animation that does not resolve.
///     </para>
///     <para>
///         Only the absent half belongs here. The other half is already the enum's own business:
///         both tags are typed <c>DynamicEnumValue</c> against <c>AnimationType</c>, so a name that
///         is not an animation is reported by the enum handler, in better words than a required-tag
///         rule could manage.
///     </para>
/// </remarks>
public sealed class GrenadeTossAnimationRule : RequiredTagsRuleBase
{
    protected override string ElementName => "grenade_attack_ability";
    protected override string OwningType => "GrenadeAttackAbility";
    protected override IReadOnlyList<string> RequiredTags => ["Grenade_Toss_Anim"];
}

/// <inheritdoc cref="GrenadeTossAnimationRule" />
public sealed class RemoteBombTossAnimationRule : RequiredTagsRuleBase
{
    protected override string ElementName => "remote_bomb_ability";
    protected override string OwningType => "RemoteBombAbility";
    protected override IReadOnlyList<string> RequiredTags => ["Toss_Anim"];
}

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

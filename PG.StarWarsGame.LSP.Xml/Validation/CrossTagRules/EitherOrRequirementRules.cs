// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Xml.Validation.CrossTagRules;

/// <summary>
///     A <c>Hero_Assassin_Ability</c> that can assassinate nobody.
/// </summary>
/// <remarks>
///     <c>Error: (%s) You should set either Can_Assassinate_Minor_Heroes or
///     Can_Assassinate_Major_Heroes to 'Yes', otherwise this ability won't do anything.</c>
///     (<c>01554898</c>), referenced by <c>HeroAssassinAbilityClass::Validate_Data</c>
///     (<c>0100df66</c>). No vanilla object uses this ability class at all - it is one of the three
///     abandoned hero abilities - so the rule exists for mods, which are the only things that can
///     reach it.
/// </remarks>
public sealed class HeroAssassinTargetsRule : EitherOrRequirementRuleBase
{
    protected override IReadOnlyList<string> ElementNames => ["hero_assassin_ability"];
    protected override string FirstTag => "Can_Assassinate_Minor_Heroes";
    protected override string SecondTag => "Can_Assassinate_Major_Heroes";
}

/// <summary>
///     A <c>Base_Destruction_Ability</c> that can destroy neither kind of base.
/// </summary>
/// <remarks>
///     <c>Error: (%s) You should set either Destroy_Starbase or Destroy_Land_Base to "Yes"</c>
///     (<c>01551560</c>), referenced by <c>BaseDestructionAbilityClass::Validate_Data</c>
///     (<c>00ff6c6b</c>). The only shipped instance is the Death Star's, and it is commented out -
///     though it sets <c>Destroy_Starbase</c> and would pass regardless.
/// </remarks>
public sealed class BaseDestructionTargetsRule : EitherOrRequirementRuleBase
{
    protected override IReadOnlyList<string> ElementNames => ["base_destruction_ability"];
    protected override string FirstTag => "Destroy_Starbase";
    protected override string SecondTag => "Destroy_Land_Base";
}

/// <summary>
///     A <c>Neutralize_Hero_Ability</c> that can neutralise nobody.
/// </summary>
/// <remarks>
///     <para>
///         <c>Warning: (%s) You should set either Can_Neutralize_Minor_Heroes or
///         Can_Neutralize_Major_Heroes to "Yes", otherwise this ability won't do anything!</c>
///         (<c>015431b8</c>), referenced by <c>NeutralizeHeroAbilityClass::Validate_Data</c>
///         (<c>00f24027</c>).
///     </para>
///     <para>
///         This rule is NOT in the harvested rule list: the harvest keyed on the <c>Error: (%s) </c>
///         prefix, and this is the one member of the family the engine labels <c>Warning</c> while
///         stating the identical consequence. The same message is also split across two string
///         constants by an embedded newline, which is why a grep for the whole sentence finds
///         nothing.
///     </para>
///     <para>
///         The only one of the three with live vanilla instances - ten of them, across the bounty
///         hunters in both games, and every one sets at least one flag.
///     </para>
/// </remarks>
public sealed class NeutralizeHeroTargetsRule : EitherOrRequirementRuleBase
{
    protected override IReadOnlyList<string> ElementNames => ["neutralize_hero_ability"];
    protected override string FirstTag => "Can_Neutralize_Minor_Heroes";
    protected override string SecondTag => "Can_Neutralize_Major_Heroes";
}

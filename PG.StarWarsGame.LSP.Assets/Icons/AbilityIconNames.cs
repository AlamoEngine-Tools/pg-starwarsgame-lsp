// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;

namespace PG.StarWarsGame.LSP.Assets.Icons;

/// <summary>
///     Ability types whose command-bar icon is not named after the type.
/// </summary>
/// <remarks>
///     <para>
///         The engine hardcodes which icon each ability draws, and no <c>Unit_Ability</c> names its
///         default icon in data, so there is nothing to read the mapping from. Most abilities happen
///         to use <c>I_SA_&lt;TYPE&gt;</c>, which needs no table. The entries below are the ones that
///         do not, recorded here because they cannot be derived.
///     </para>
///     <para>
///         Several are near-misses that would be tempting to guess and easy to get wrong:
///         <c>CAPTURE_VEHICLE</c> resolves to a PLURAL icon name, <c>FORCE_LIGHTNING</c> to an entry
///         Petroglyph misspelled as <c>FORCE_LIGHTING</c>, and <c>INVULNERABILITY</c> to
///         <c>EVASIVE_MANEUVERS</c>, which no naming rule would ever produce. Do not extend this
///         table by pattern-matching.
///     </para>
///     <para>
///         All but two were confirmed against the running game. The exceptions are
///         <c>RADIOACTIVE_CONTAMINATE</c> and <c>TARGETED_REPAIR</c>, which are INFERRED from an
///         orphan pairing: no <c>Unit_Ability</c> has type <c>CONTAMINATE</c> or
///         <c>REPAIR_VEHICLE</c>, so those atlas entries belong to nothing, and these are the only
///         contaminate- and repair-flavoured abilities that reach the command bar at all. Narrower
///         than a name resemblance, but still not an observation - if either draws the wrong art in
///         game, that is the pair to suspect.
///     </para>
///     <para>
///         Not exhaustive: seven ability types remain unmapped. Six of them
///         (<c>AREA_EFFECT_CONVERT</c>, <c>AREA_EFFECT_STUN</c>, <c>EJECT_VEHICLE_THIEF</c>,
///         <c>FIRE_LOBBING_SUPERWEAPON</c>, <c>TARGETED_INVULNERABILITY</c>,
///         <c>UNTARGETED_STICKY_BOMB</c>) carry no <c>GUI_Activated_Ability_Name</c> on any shipped
///         instance, so they never reach the command bar and have NO icon to map - do not invent one.
///         Only <c>SUPER_LASER</c> is genuinely unknown. See <c>docs/ability-icon-mapping.md</c>.
///         Unmapped slots fall back to the ability type as text.
///     </para>
/// </remarks>
public static class AbilityIconNames
{
    private static readonly ImmutableDictionary<string, string> ByType =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["BARRAGE"] = "I_SA_BARRAGE_AREA.TGA",
            ["CABLE_ATTACK"] = "I_SA_TOW_CABLE_ATTACK.TGA",
            ["CAPTURE_VEHICLE"] = "I_SA_CAPTURE_VEHICLES.TGA",
            ["CONCENTRATE_FIRE"] = "I_SA_ALL_SHIPS_CONCENTRATE_FIRE.TGA",
            ["DEFEND"] = "I_SA_DEFEND_MODE.TGA",
            ["DEPLOY_TROOPERS"] = "I_SA_DEPLOY_STORMTROOPERS.TGA",
            ["ENERGY_WEAPON"] = "I_SA_FIRE_ENERGY_WEAPON.TGA",
            ["FORCE_LIGHTNING"] = "I_SA_FORCE_LIGHTING.TGA",
            ["FORCE_TELEKINESIS"] = "I_SA_FORCE_CRUSH.TGA",
            ["FORCE_WHIRLWIND"] = "I_SA_FORCE_PUSH.TGA",
            ["FOW_REVEAL_PING"] = "I_SA_SENSOR_PING.TGA",
            ["INVULNERABILITY"] = "I_SA_EVASIVE_MANEUVERS.TGA",
            ["JET_PACK"] = "I_SA_JETPACK_JUMP.TGA",
            ["MISSILE_SHIELD"] = "I_SA_MISSILE_JAMMER.TGA",
            ["REPLENISH_WINGMEN"] = "I_SA_COVER_ME.TGA",
            ["SPOILER_LOCK"] = "I_SA_S_FOIL_MODE.TGA",
            ["TARGETED_HACK"] = "I_SA_HACK_TURRET.TGA",
            ["TURBO"] = "I_SA_POWER_TO_ENGINES.TGA",

            // INFERRED, not observed in game - see the third remark above before trusting these.
            ["RADIOACTIVE_CONTAMINATE"] = "I_SA_CONTAMINATE.TGA",
            ["TARGETED_REPAIR"] = "I_SA_REPAIR_VEHICLE.TGA"
        }.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    ///     The atlas entry for <paramref name="abilityType" />, or <see langword="null" /> when the
    ///     type is not one of the exceptions - in which case <c>I_SA_&lt;TYPE&gt;</c> applies.
    /// </summary>
    public static string? For(string abilityType)
    {
        return string.IsNullOrWhiteSpace(abilityType)
            ? null
            : ByType.GetValueOrDefault(abilityType.Trim());
    }

    /// <summary>Every mapped ability type. Exposed for tests and tooling.</summary>
    public static IEnumerable<KeyValuePair<string, string>> All => ByType;
}

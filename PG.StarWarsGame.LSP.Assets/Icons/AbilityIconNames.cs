// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;

namespace PG.StarWarsGame.LSP.Assets.Icons;

/// <summary>
///     Ability types whose command-bar icon is not named after the type.
/// </summary>
/// <remarks>
///     <para>
///         The engine hardcodes which icon each ability draws: <c>I_SA_*</c> occurs in exactly one
///         file across a game install - the mega texture directory itself - so there is nothing in
///         the data to read the mapping from. Most abilities happen to use <c>I_SA_&lt;TYPE&gt;</c>,
///         which needs no table. The entries below are the ones that do not, confirmed against the
///         running game rather than inferred, and recorded here because they cannot be derived.
///     </para>
///     <para>
///         Several are near-misses that would be tempting to guess and easy to get wrong:
///         <c>CAPTURE_VEHICLE</c> resolves to a PLURAL icon name, <c>FORCE_LIGHTNING</c> to an entry
///         Petroglyph misspelled as <c>FORCE_LIGHTING</c>, and <c>INVULNERABILITY</c> to
///         <c>EVASIVE_MANEUVERS</c>, which no naming rule would ever produce. Do not extend this
///         table by pattern-matching; confirm against the game, as everything here was.
///     </para>
///     <para>
///         Not exhaustive. Nine ability types remain unmapped and are UNKNOWN rather than known to
///         have no icon - see <c>docs/ability-icon-mapping.md</c>. Their slots fall back to the
///         ability type as text.
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
            ["TURBO"] = "I_SA_POWER_TO_ENGINES.TGA"
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

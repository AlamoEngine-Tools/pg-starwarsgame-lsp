// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Assets.Icons;

/// <summary>
///     Where an ability's default name and description text live.
/// </summary>
/// <remarks>
///     <para>
///         The counterpart to <see cref="AbilityIconNames" />, and a much happier story. Icons need a
///         recorded table because the engine hardcodes them and no rule produces
///         <c>INVULNERABILITY -> I_SA_EVASIVE_MANEUVERS</c>. The TEXT follows a convention exactly.
///     </para>
///     <para>
///         Measured 2026-08-27 against the shipped FoC data: of the <b>70</b> ability types that
///         appear in a <c>Unit_Ability</c> block, <b>70</b> have both
///         <c>TEXT_TOOLTIP_ABILITY_&lt;TYPE&gt;_NAME</c> and
///         <c>TEXT_TOOLTIP_ABILITY_&lt;TYPE&gt;_DESCRIPTION</c> in the master text file - including
///         the 29 types that carry no <c>GUI_Activated_Ability_Name</c> on any instance and so never
///         reach the command bar. No exception table is needed, and one should not be started: if a
///         key is missing, the honest answer is that the mod has not written it.
///     </para>
///     <para>
///         These are the DEFAULTS. <c>Alternate_Name_Text</c> (31 uses) and
///         <c>Alternate_Description_Text</c> (33 uses) override them per INSTANCE, exactly as
///         <c>Alternate_Icon_Name</c> overrides the icon - so two units can give one ability type
///         different text. Resolve the override first and only fall back to these.
///     </para>
/// </remarks>
public static class AbilityTextKeys
{
    private const string Prefix = "TEXT_TOOLTIP_ABILITY_";

    /// <summary>The key holding the ability's display name, or null when no type was given.</summary>
    public static string? NameKeyFor(string? type) => KeyFor(type, "_NAME");

    /// <summary>The key holding the ability's tooltip text, or null when no type was given.</summary>
    public static string? DescriptionKeyFor(string? type) => KeyFor(type, "_DESCRIPTION");

    private static string? KeyFor(string? type, string suffix)
    {
        return string.IsNullOrWhiteSpace(type)
            ? null
            : Prefix + type.Trim().ToUpperInvariant() + suffix;
    }
}

// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Assets.Icons;
using PG.StarWarsGame.LSP.Core.Localisation;
using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Server.Preview;

/// <summary>
///     Fills an ability row's display name, tooltip and command-bar icon.
/// </summary>
/// <remarks>
///     <para>
///         All three follow one precedence, and it comes from the shipped data rather than from
///         taste: an <c>Alternate_*</c> tag overrides per INSTANCE - two units may give the same
///         ability type different text and different art - and otherwise the engine's own convention
///         applies.
///     </para>
///     <para>
///         For TEXT the convention is exact. Measured 2026-08-27 across the shipped FoC data: all
///         <b>70</b> ability types appearing in a <c>Unit_Ability</c> block have both
///         <c>TEXT_TOOLTIP_ABILITY_&lt;TYPE&gt;_NAME</c> and <c>..._DESCRIPTION</c>, including the 29
///         that never reach the command bar. So unlike the ICONS - which needed
///         <see cref="AbilityIconNames" /> because the engine hardcodes them and no rule produces
///         <c>INVULNERABILITY -&gt; I_SA_EVASIVE_MANEUVERS</c> - there is no table here and none
///         should be started. A missing name means the mod removed it.
///     </para>
/// </remarks>
public static class PreviewAbilityText
{
    /// <param name="ability">The row built from the XML, with its text and icon still empty.</param>
    /// <param name="alternateNameKey">The instance's <c>Alternate_Name_Text</c>, if any.</param>
    /// <param name="alternateIconName">The instance's <c>Alternate_Icon_Name</c>, if any.</param>
    /// <param name="icons">The project's icons; null when unavailable.</param>
    /// <param name="localisation">The project's text; null when unavailable.</param>
    /// <param name="problems">Collects anything the author can act on.</param>
    /// <param name="alternateDescriptionKey">
    ///     The instance's <c>Alternate_Description_Text</c>, if any.
    /// </param>
    public static PreviewAbility Fill(
        PreviewAbility ability,
        string? alternateNameKey,
        string? alternateIconName,
        IconCatalog? icons,
        ILocalisationIndex? localisation,
        IList<PreviewProblem> problems,
        string? alternateDescriptionKey = null)
    {
        ArgumentNullException.ThrowIfNull(ability);
        ArgumentNullException.ThrowIfNull(problems);

        return ability with
        {
            Name = Text(localisation, alternateNameKey, AbilityTextKeys.NameKeyFor(ability.Type)),
            Description = Text(
                localisation, alternateDescriptionKey, AbilityTextKeys.DescriptionKeyFor(ability.Type)),
            IconDataUri = Icon(ability, alternateIconName, icons, problems)
        };
    }

    /// <summary>
    ///     The override's text when the instance declares one, otherwise the convention's.
    /// </summary>
    /// <remarks>
    ///     A declared override that does not resolve yields NOTHING rather than falling through to
    ///     the convention. Falling through would show the stock text for an ability the mod has
    ///     deliberately redefined, which reads as though the redefinition never happened.
    /// </remarks>
    private static string? Text(ILocalisationIndex? localisation, string? overrideKey, string? conventionKey)
    {
        if (localisation is null)
            return null;

        var key = string.IsNullOrWhiteSpace(overrideKey) ? conventionKey : overrideKey.Trim();
        return string.IsNullOrWhiteSpace(key) ? null : localisation.GetValue(key);
    }

    private static string? Icon(
        PreviewAbility ability, string? alternateIconName, IconCatalog? icons,
        IList<PreviewProblem> problems)
    {
        var resolved = AbilityIconResolver.Resolve(icons, ability.Type, alternateIconName);

        switch (resolved.Outcome)
        {
            case AbilityIconOutcome.Resolved:
                return DataUri(resolved.Icon!.Png);

            // The ability names art the project does not ship. The engine would draw its missing-art
            // marker, so the row does too - and unlike the ambiguous case below, this one IS the
            // author's to fix, so it is reported rather than passed over in silence.
            case AbilityIconOutcome.DeclaredButMissing:
                problems.Add(new PreviewProblem(DiagnosticIds.PreviewAbilityIconNotFound, "warning",
                    $"Ability {ability.Type} names icon '{alternateIconName}', which is not in this "
                    + "project's mega texture or icon sources. The game will draw missing art."));
                return DataUri(FallbackIcon.Png);

            // Ambiguous, and deliberately silent: I_SA_<TYPE> is only a guess at the name the engine
            // hardcodes, so a miss can equally mean the real name is something else entirely while
            // the game shows perfectly good art. The row falls back to the ability type as text.
            default:
                return null;
        }
    }

    private static string DataUri(byte[] png) => "data:image/png;base64," + Convert.ToBase64String(png);
}

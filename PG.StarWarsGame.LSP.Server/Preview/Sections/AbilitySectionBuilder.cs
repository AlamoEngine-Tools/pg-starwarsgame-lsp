// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Assets.Icons;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Localisation;
using PG.StarWarsGame.LSP.Core.Messages;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Server.Abilities;
using PG.StarWarsGame.LSP.Xml.Validation;

namespace PG.StarWarsGame.LSP.Server.Preview.Sections;

/// <summary>
///     The abilities a unit declares in <c>Unit_Abilities_Data</c>: each row with its timing, its
///     stat modifiers, the particle effects bound to it by proxy prefix, the deploy clips, and the
///     localised text and icon the text convention resolves (#314).
/// </summary>
public sealed class AbilitySectionBuilder(IMessageCatalog messages)
{
    /// <summary>
    ///     The abilities the subject declares, with whatever each one drives on the model.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Every ability, not only the ones that DO something visible. 68 types exist over the
    ///         two trees and most drive nothing - SPREAD_OUT and HUNT are orders - so filtering to
    ///         the ones with a bone or a particle would hide most of what a unit can do.
    ///     </para>
    ///     <para>
    ///         A proxy whose prefix names an ability the object does not declare is reported as an
    ///         UNBOUND effect at <c>info</c>, never as a warning: it is normal authoring.
    ///         <c>Ev_mdu_fieldgen</c> carries <c>prs_at-aa_fx</c> and declares no ability at all.
    ///     </para>
    /// </remarks>
    public List<PreviewAbility> Abilities(
        EffectiveObject effective,
        IReadOnlyList<PreviewParticle> particles,
        IReadOnlyList<string> animations,
        List<PreviewProblem> problems,
        IconCatalog? icons = null,
        ILocalisationIndex? localisation = null)
    {
        var declared = UnitAbilityReader.Read(effective.FragmentOf(EncyclopediaTags.UnitAbilitiesData));

        // Deliberately NOT gated on `declared.Count > 0`. An object with proxies and no abilities at
        // all is exactly where an unbound effect is most likely to be a real mistake - and returning
        // early there is a hole a live server found.
        var bound = AbilityProxyPrefix.Bind(
            particles.Select(particle => particle.Bone), declared.Select(a => a.Type));

        foreach (var (ability, names) in bound.Unbound)
            problems.Add(new PreviewProblem(DiagnosticIds.PreviewUnboundAbilityEffect, "info",
                messages.Format("PreviewScene_UnboundAbilityEffect", string.Join(", ", names), ability)));

        var abilities = new List<PreviewAbility>();

        // ReSharper disable once LoopCanBeConvertedToQuery - the row is built in two steps and the
        // second reports problems as it goes; a query would hide that.
        foreach (var ability in declared)
        {
            var row = new PreviewAbility(
                ability.Type,
                ability.Tag("GUI_Activated_Ability_Name"),
                ability.Tag("Owner_Attachment_Bone"),
                ability.Tag("Particle_Effect"),
                EngineNumber.ReadFloat(ability.Tag("Recharge_Seconds")),
                EngineNumber.ReadFloat(ability.Tag("Expiration_Seconds")),
                bound.For(ability.Type),
                animations.FirstOrDefault(IsDeploy),
                animations.FirstOrDefault(IsUndeploy),
                Modifiers(ability));

            // The Alternate_* trio is read here and not carried on the DTO: they are localisation
            // KEYS and an icon NAME, none of which the client can do anything with. What it wants is
            // the resolved text and the pixels.
            abilities.Add(PreviewAbilityText.Fill(
                row,
                ability.Tag("Alternate_Name_Text"),
                ability.Tag("Alternate_Icon_Name"),
                icons,
                localisation,
                problems,
                ability.Tag("Alternate_Description_Text"),
                messages));
        }

        return abilities;
    }

    /// <summary>
    ///     The stats an ability multiplies while it is active.
    /// </summary>
    /// <remarks>
    ///     The whole of what a STAT-ONLY ability does, and there are many: DEFEND declares no proxy,
    ///     no bone, no particle and no clip, so without these its row is a switch with nothing
    ///     behind it. The shipped values carry an `f` suffix - `0.8f` - which the parse has to cope
    ///     with; a row that is not a number at all names no modifier and is dropped.
    /// </remarks>
    private static List<PreviewAbilityModifier> Modifiers(UnitAbility ability)
    {
        var modifiers = new List<PreviewAbilityModifier>();

        foreach (var row in ability.Tags("Mod_Multiplier"))
        {
            var parts = row.Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length < 2 || parts[0].Length == 0)
                continue;

            if (EngineNumber.ReadFloat(parts[1]) is { } factor)
                modifiers.Add(new PreviewAbilityModifier(parts[0], factor));
        }

        return modifiers;
    }

    /// <summary>
    ///     An <c>_undeploy</c> clip. Tested BEFORE the deploy one, because `undeploy` contains
    ///     `deploy` and a naive check claims the same file for both.
    /// </summary>
    private static bool IsUndeploy(string name)
    {
        return name.Contains("_undeploy", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDeploy(string name)
    {
        return !IsUndeploy(name) && name.Contains("_deploy", StringComparison.OrdinalIgnoreCase);
    }
}

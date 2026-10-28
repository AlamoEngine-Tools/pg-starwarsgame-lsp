// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Assets.Models;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Messages;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Xml;
using PG.StarWarsGame.LSP.Server.Assets;
using PG.StarWarsGame.LSP.Xml.Validation;

namespace PG.StarWarsGame.LSP.Server.Preview.Sections;

/// <summary>
///     The parts a scene is assembled from and what rides on them: a mounted hardpoint with its
///     model, bone, weapon and problems, the breakoff props the hardpoints shed, and the particle
///     proxies a part's model carries (#314). The hardpoint's weapon reading is the weapon section's.
/// </summary>
public sealed class PartsSectionBuilder(
    IGameAssetResolver assets,
    IVariantTagSource tagSource,
    IMessageCatalog messages)
{
    private const string HardPointsTag = "HardPoints";
    private const string ModelToAttachTag = "Model_To_Attach";
    private const string AttachmentBoneTag = "Attachment_Bone";
    private readonly WeaponSectionBuilder _weapons = new(tagSource, messages);

    public static List<string> HardpointIds(EffectiveObject effective)
    {
        return effective.ValueOf(HardPointsTag) is { } raw
            ? XmlUtility.SplitList(raw)
            : [];
    }

    public void AddHardpoint(GameIndex index,
        EffectiveObjectResolver resolver,
        string? hull,
        string hardpointId,
        bool diesWithHardpoints,
        List<PreviewPart> parts,
        List<PreviewHardpoint> hardpoints,
        List<PreviewWeapon> weapons,
        List<PreviewProblem> problems)
    {
        var effective = resolver.Resolve(hardpointId);
        if (!effective.Found)
        {
            problems.Add(new PreviewProblem(DiagnosticIds.PreviewHardpointNotDefined, "warning",
                messages.Format("PreviewScene_HardpointMountedButNotDefined", hardpointId), hardpointId));
            return;
        }

        var attachBone = effective.ValueOf(AttachmentBoneTag);
        var model = effective.ValueOf(ModelToAttachTag);
        string? partId = null;

        if (!string.IsNullOrEmpty(model))
        {
            partId = $"hp:{hardpointId}";
            var resolved = assets.Locate(PreviewModelReference.ModelPath(model)) is not null;

            // Emitted even when unresolved: a hardpoint whose model is missing is exactly the kind of
            // authoring mistake this preview exists to surface, and dropping it would hide it.
            parts.Add(new PreviewPart(partId, model, "hull", attachBone,
                PreviewPartOrigin.Hardpoint, hardpointId, resolved));

            if (!resolved)
                problems.Add(new PreviewProblem(DiagnosticIds.PreviewHardpointModelNotFound, "warning",
                    messages.Format("PreviewScene_HardpointAttachesModelWhichWas", hardpointId, model),
                    hardpointId));
        }

        var isTargetable = effective.BoolOf("Is_Targetable");

        // Measured in the game (FoC x64, 2024 release): a hardpoint with no attachment bone is
        // attached to NOTHING. Its world position falls back to Fire_Bone_A and, with that unset
        // too, to the map origin; its Model_To_Attach is never attached; the targeting reticle
        // consults the attachment bone only and is drawn in the top-left corner of the screen.
        // Damage reads no bone, so a targetable one is hit only where the game places it - at the
        // origin, in practice never - and a unit dies only once every targetable hardpoint is
        // destroyed, so one of these makes the whole unit indestructible.
        //
        // Two findings rather than one, because they differ in what the author has to do about
        // them and in what suppressing them would cost. Both are errors: the unit is broken either
        // way, shot at or not.
        if (string.IsNullOrEmpty(attachBone))
        {
            var fireBoneA = effective.ValueOf("Fire_Bone_A");
            var placed = string.IsNullOrEmpty(fireBoneA)
                ? messages.Format("PreviewScene_PlacedAtOrigin")
                : messages.Format("PreviewScene_PlacedAtFireBone", fireBoneA);
            problems.Add(isTargetable
                ? new PreviewProblem(DiagnosticIds.PreviewTargetableHardpointNoBone, "error",
                    messages.Format(string.IsNullOrEmpty(fireBoneA)
                            ? "PreviewScene_TargetableNoBoneUnkillable"
                            : "PreviewScene_TargetableNoBone",
                        hardpointId, AttachmentBoneTag, placed),
                    hardpointId)
                : new PreviewProblem(DiagnosticIds.PreviewHardpointNoBone, "error",
                    messages.Format("PreviewScene_HardpointNoBone", hardpointId, AttachmentBoneTag, placed),
                    hardpointId));
        }
        else if (hull is not null &&
                 !HardpointBoneModelResolver.ModelHasBone(index, hull, attachBone))
        {
            problems.Add(new PreviewProblem(DiagnosticIds.PreviewHardpointBoneMissing, "warning",
                messages.Format("PreviewScene_HardpointBoneMissing", hardpointId, attachBone, hull), hardpointId));
        }

        // Can a shot ever REACH this hardpoint? Only one shape makes it impossible: no Collision_Mesh
        // at all.
        //
        // The engine's damage path finds the hardpoint by NAME, an exact _stricmp against each
        // Collision_Mesh - but first replaces the name with the hardpoint's OWN value when the hit is
        // aimed at it, and in a second path taken from the attacker. The lookup
        // then compares a value with itself, so a Collision_Mesh the model lacks is still reachable
        // by aimed fire: the Gargantuan's eight hardpoints all write one, and it dies through them.
        // An EMPTY name fails the size check before the lookup, aimed or not, so that
        // hardpoint never dies and the all-destroyed branch can never complete.
        //
        // The mismatched-name case used to be reported here as well, as an error saying no shot could
        // reach it. It is reported once, by HardpointBoneNotOnModelHandler in the editor, as a warning
        // with a quick fix - repeating it here put every Gargantuan hardpoint on two surfaces.
        //
        // Silent where the object does not die with its hardpoints - the palace keeps its
        // generators as destructible scenery, so one that cannot be shot takes nothing away.
        if (effective.BoolOf("Is_Destroyable")
            && diesWithHardpoints
            && string.IsNullOrWhiteSpace(effective.ValueOf("Collision_Mesh")))
            problems.Add(new PreviewProblem(DiagnosticIds.PreviewHardpointUnreachable, "error",
                messages.Format("PreviewScene_HardpointUnreachable", hardpointId),
                hardpointId));

        var tooltipKey = effective.ValueOf("Tooltip_Text");

        hardpoints.Add(new PreviewHardpoint(
            hardpointId,
            partId,
            effective.ValueOf("Type"),
            attachBone,
            effective.BoolOf("Is_Destroyable"),
            isTargetable,
            effective.FloatOf("Health"),
            effective.ValueOf("Damage_Particles"),
            effective.ValueOf("Damage_Decal"),
            effective.ValueOf("Collision_Mesh"),
            effective.ValueOf("Engine_Particles"),
            effective.ValueOf("Death_Explosion_Particles"),
            effective.ValueOf("Death_Breakoff_Prop"),
            EngineBoolean.IsTrueUnlessDenied(effective.ValueOf("Engine_Death_Hide_Engine_Particles")),
            tooltipKey,
            // Resolved here rather than left to the client, which has no text index at all. The
            // localisation index matches case-insensitively, and the shipped files are not
            // consistent about the case they write - so the preview must not be stricter.
            tooltipKey is null
                ? null
                : index.Localisation.GetValue(tooltipKey),
            PreviewFireArcs.Turret(effective)));

        if (_weapons.Weapon(index, effective, hardpointId) is { } weapon)
            weapons.Add(weapon);
    }

    /// <summary>
    ///     The wreckage this subject's hardpoints shed, one entry per DISTINCT prop.
    /// </summary>
    /// <remarks>
    ///     Mirrored hardpoints share a prop - the Star Destroyer's four weapon batteries pair up - so this
    ///     is deduplicated rather than emitted per hardpoint, which would have the client instantiate
    ///     the same wreck twice.
    /// </remarks>
    public List<PreviewBreakoffProp> BreakoffProps(GameIndex index,
        EffectiveObjectResolver resolver,
        IReadOnlyList<PreviewHardpoint> hardpoints,
        List<PreviewProblem> problems)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var props = new List<PreviewBreakoffProp>();

        foreach (var id in hardpoints
                     .Select(h => h.DeathBreakoffProp)
                     .Where(id => !string.IsNullOrEmpty(id))
                     .Select(id => id!)
                     .Where(seen.Add))
        {
            var effective = resolver.Resolve(id);
            if (!effective.Found)
            {
                problems.Add(new PreviewProblem(DiagnosticIds.PreviewBreakoffPropNotDefined, "warning",
                    messages.Format("PreviewScene_BreakoffPropNotDefined", id)));
                props.Add(new PreviewBreakoffProp(id, null, false, null, null, null, null, null,
                    null, false));
                continue;
            }

            // A breakoff prop is space debris; a ground hardpoint names none, so the land tag is not
            // consulted here.
            var model = effective.ValueOf("Space_Model_Name");

            var modelRef = string.IsNullOrEmpty(model) ? null : model;

            props.Add(new PreviewBreakoffProp(
                id,
                modelRef,
                true,
                PreviewTagReader.Vector3Tag(effective, "Debris_Movement_Vector"),
                PreviewTagReader.Vector3Tag(effective, "Debris_Facing_Rotate_Vector"),
                effective.FloatOf("Debris_Min_Lifetime_Seconds"),
                effective.FloatOf("Debris_Max_Lifetime_Seconds"),
                effective.ValueOf("Debris_Attached_Particle"),
                effective.ValueOf("Death_Explosions"),
                effective.BoolOf("Remove_Upon_Death"),
                // Its OWN model's, like a death clone's - a burning piece of debris trails its own
                // fire, and `Debris_Attached_Particle` is a second, separate effect the XML names.
                modelRef is null ? [] : AnimationSectionBuilder.AnimationsFor(index, modelRef),
                modelRef is null ? [] : ProxiesOf(modelRef, id, [], problems)));
        }

        return props;
    }

    /// <summary>
    ///     The particle systems one part's model carries.
    /// </summary>
    /// <remarks>
    ///     Reads the model to get at its proxies, which nothing else in the index holds. Geometry is
    ///     skipped - this wants the skeleton and the connections block, and a capital ship's vertices
    ///     are megabytes of work for an answer that does not use them.
    /// </remarks>
    public IReadOnlyList<PreviewParticle> ParticlesOf(
        PreviewPart part, IReadOnlyList<PreviewHardpoint> hardpoints, List<PreviewProblem> problems)
    {
        // Only the hull's own hardpoints can claim its proxies; a turret's smoke is its own.
        var owning = part.Id == "hull"
            ? hardpoints
            : [.. hardpoints.Where(h => h.PartId == part.Id)];

        return ProxiesOf(part.ModelRef, part.Id, owning, problems);
    }

    /// <summary>
    ///     The particle proxies one MODEL carries, whoever ends up holding it.
    /// </summary>
    /// <remarks>
    ///     A passive subject - a death clone, a piece of wreckage - reads its own model through here
    ///     with NO owning hardpoints. The gate joins a proxy to the hardpoint whose
    ///     <c>Damage_Particles</c> bone is its parent, and that is a fact about the SUBJECT's
    ///     skeleton; a clone has its own, so nothing on it may be claimed by a hardpoint of the ship it
    ///     replaces. Everything it carries plays whenever it is on screen, which is what the death
    ///     of a ship is.
    /// </remarks>
    public IReadOnlyList<PreviewParticle> ProxiesOf(
        string modelRef,
        string partId,
        IReadOnlyList<PreviewHardpoint> owning,
        List<PreviewProblem> problems)
    {
        var bytes = assets.Read(PreviewModelReference.ModelPath(modelRef));
        if (bytes is null || AloFile.Classify(bytes) != AloFileKind.Model)
            return [];

        try
        {
            var model = AloModelReader.Read(bytes, AloReadOptions.SkipGeometry);

            return PreviewParticleResolver.Resolve(partId, model.Bones, model.Proxies, owning);
        }
        catch (AloFormatException e)
        {
            // The geometry request will fail the same way and say so; this only costs the effects.
            problems.Add(new PreviewProblem(DiagnosticIds.PreviewModelUnreadable, "warning",
                messages.Format("PreviewScene_CouldNotBeReadIts2", modelRef, e.Message)));
            return [];
        }
    }
}
// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Globalization;
using PG.StarWarsGame.LSP.Assets.Icons;
using PG.StarWarsGame.LSP.Assets.Models;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Localisation;
using PG.StarWarsGame.LSP.Core.Messages;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Xml;
using PG.StarWarsGame.LSP.Server.Abilities;
using PG.StarWarsGame.LSP.Server.Assets;
using PG.StarWarsGame.LSP.Server.Preview.Sections;
using PG.StarWarsGame.LSP.Xml.Validation;

namespace PG.StarWarsGame.LSP.Server.Preview;

/// <summary>
///     Assembles a preview scene from a GameObject and its XML, or from a single model file.
/// </summary>
/// <remarks>
///     <para>
///         This is the part no standalone tool can do. AloViewer sees one <c>.alo</c> and knows nothing
///         about XML, so it cannot mount a Star Destroyer's eight turrets on their attachment bones or
///         say which bone lights up when one is destroyed. Both facts live in the hardpoint XML, which
///         the server already indexes.
///     </para>
///     <para>
///         Bone-to-model resolution is delegated entirely to
///         <see cref="HardpointBoneModelResolver" />, never re-derived. That type already encodes which
///         model each hardpoint bone tag targets - parent hull versus the hardpoint's own
///         <c>Model_To_Attach</c>, with fire bones switching sides on <c>Is_Turret</c> - cross-checked
///         against all 194 vanilla hardpoints. A second copy of those rules here would drift from the
///         validator and the inlay hint, and the three would disagree about the same file.
///     </para>
/// </remarks>
public sealed class PreviewSceneBuilder(
    IGameIndexService indexService,
    ISchemaProvider schema,
    IVariantTagSource tagSource,
    IGameAssetResolver assets,
    IMessageCatalog messages)
{
    private readonly IMessageCatalog _messages = messages;
    private readonly FactionSectionBuilder _factions = new(schema, tagSource);
    private readonly ReticleSectionBuilder _reticles = new(tagSource, messages);
    private readonly AnimationSectionBuilder _animations = new(messages);
    private readonly ProjectileSectionBuilder _projectiles = new(tagSource, messages);
    private readonly WeaponSectionBuilder _weapons = new(tagSource, messages);
    private readonly AbilitySectionBuilder _abilities = new(messages);
    private readonly PartsSectionBuilder _parts = new(assets, tagSource, messages);
    private readonly DamageSectionBuilder _damage = new(tagSource, assets, messages);
    private readonly ModelSceneBuilder _models = new(assets, schema, tagSource, messages);

    /// <summary>
    ///     A scene for a single <c>.alo</c>, opened directly - a model or a particle system.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Deliberately the same shape as an assembled object: a bare model is a scene with one
    ///         part, so the client has exactly one thing to render rather than two code paths.
    ///     </para>
    ///     <para>
    ///         Which of the two formats the file holds is decided here, by reading its root chunk,
    ///         because the extension does not say and the client cannot guess. The two take different
    ///         endpoints from this point on.
    ///     </para>
    /// </remarks>
    public PreviewScene BuildForModel(string modelReference)
    {
        return _models.BuildForModel(indexService.Current, modelReference);
    }

    /// <summary>A scene for an <c>.ala</c> opened directly: the model it drives, found by its skeleton.</summary>
    public PreviewScene BuildForAnimation(string animationReference)
    {
        return _models.BuildForAnimation(indexService.Current, animationReference);
    }

    /// <summary>A scene for a GameObject: its tactical model plus everything its XML mounts on it.</summary>
    /// <param name="objectId">The GameObject's id, as the XML names it.</param>
    /// <param name="icons">
    ///     The project's icon catalog, for the ability rows' command-bar art. Optional and resolved
    ///     by the CALLER because building one is async and may decode a mega texture, which is not
    ///     something a synchronous scene build should be doing; a scene without it simply carries no
    ///     ability icons.
    /// </param>
    public PreviewScene BuildForObject(string objectId, IconCatalog? icons = null)
    {
        var index = indexService.Current;
        var resolver = new EffectiveObjectResolver(index, schema, tagSource);
        var effective = resolver.Resolve(objectId);

        if (!effective.Found)
            return PreviewScene.NotFound(objectId, _messages.Format("PreviewScene_NoObjectNamedIndexed", objectId),
                assets.Tiers);

        var problems = new List<PreviewProblem>();
        if (effective.Cyclic)
            problems.Add(new PreviewProblem(DiagnosticIds.PreviewInheritanceCycle, "error",
                _messages.Format("PreviewScene_InheritanceCycle", objectId, effective.CycleObjectId)));

        var bones = new HardpointBoneModelResolver(index, schema, tagSource);
        var parts = new List<PreviewPart>();
        var hardpoints = new List<PreviewHardpoint>();
        var weapons = new List<PreviewWeapon>();

        // DeclaredModels is variant-resolved and already restricted to the TACTICAL models - a
        // starbase's low-detail galactic mesh legitimately lacks the hardpoint bones, so including it
        // would produce a scene full of phantom problems.
        var hull = bones.DeclaredModels(effective.ObjectId).FirstOrDefault(m => !string.IsNullOrEmpty(m));

        if (hull is null)
            problems.Add(new PreviewProblem(DiagnosticIds.PreviewNoTacticalModel, "error",
                _messages.Format("PreviewScene_DeclaresNoTacticalModelSo", effective.ObjectId)));
        else
            parts.Add(new PreviewPart("hull", hull, null, null, PreviewPartOrigin.Hull, null,
                assets.Locate(PreviewModelReference.ModelPath(hull)) is not null));

        // Read once here rather than per hardpoint: it is the owner's tag, not theirs.
        var diesWithHardpoints = DamageSectionBuilder.DiesWithHardpoints(effective);

        foreach (var hardpointId in PartsSectionBuilder.HardpointIds(effective))
            _parts.AddHardpoint(index, resolver, hull, hardpointId, diesWithHardpoints,
                parts, hardpoints, weapons, problems);

        // The unit's own armament, for the 188 objects that carry a WEAPON behaviour instead of - or
        // as well as - hardpoints. Nine objects across the two trees have both.
        if (_weapons.UnitWeapon(index, effective, hull, problems) is { } unitWeapon)
            weapons.Add(unitWeapon);

        var animationSource = _animations.ResolveAnimationOverride(index, effective, hull, problems);

        // Read once: the scene carries it and the check below compares against it.
        var stages = DamageSectionBuilder.DamageStages(effective);
        _damage.ReportUntaggedStages(index, effective.ObjectId, hull, stages, problems);

        if (hull is not null && !parts[0].Resolved)
            problems.Add(new PreviewProblem(DiagnosticIds.PreviewModelNotFound, "error",
                _messages.Format("PreviewScene_ModelWasNotFound", hull, assets.Tiers.Explain(_messages))));

        // Every part, not just the hull: a turret carries its own muzzle flashes and engine glow.
        var particles = parts
            .Where(part => part.Resolved)
            .SelectMany(part => _parts.ParticlesOf(part, hardpoints, problems))
            .ToList();

        return new PreviewScene(PreviewSceneKind.Object, effective.ObjectId, parts, hardpoints,
            _factions.Factions(index), problems, assets.Tiers, animationSource, particles,
            // The clips are named after the model whose skeleton they were authored against, which
            // is the OVERRIDE when a unit borrows another model's set.
            AnimationSectionBuilder.AnimationsFor(index, animationSource ?? hull ?? string.Empty),
            // The HULL's cameras only. A turret carries its own skeleton and could in principle
            // declare one, but it was aimed at the turret, and the subject here is the whole unit.
            hull is null ? [] : _models.CamerasOf(hull, problems),
            effective.ValueOf("Type"),
            PreviewTagReader.CategoryTokens(effective.ValueOf("CategoryMask")),
            _parts.BreakoffProps(index, resolver, hardpoints, problems),
            weapons,
            _reticles.Reticles(index, hardpoints, problems),
            _projectiles.Projectiles(resolver, weapons, problems),
            // NAMED from here on. The positional list is twenty arguments long and every optional
            // one is a list or a record, so inserting in the wrong slot type-checks against its
            // neighbour often enough to be dangerous - it did, once.
            _damage.Defence(index, effective, hardpoints),
            _abilities.Abilities(effective, particles,
                AnimationSectionBuilder.AnimationsFor(index, animationSource ?? hull ?? string.Empty), problems,
                icons, index.Localisation),
            _damage.DeathClones(index, resolver, effective, problems),
            DamageSectionBuilder.SpinAway(effective),
            effective.ValueOf("Death_Explosions"),
            _projectiles.ProjectileCatalog(index),
            // What it wears when no faction colour applies, which is most of the time - its own
            // colour first, and whose fallback to use when it declares none.
            PreviewTagReader.Colour(effective, "No_Colorization_Color"),
            PreviewTagReader.FirstToken(effective.ValueOf("Affiliation")),
            stages,
            DamageSectionBuilder.DamageTable(effective),
            EngineScaleFactor.Of(effective.ValueOf("Scale_Factor")),
            effective.ValueOf(EncyclopediaTags.IconName));
    }

    // ── hardpoints ────────────────────────────────────────────────────────────

    // ── factions ──────────────────────────────────────────────────────────────

    // ── tag reading ───────────────────────────────────────────────────────────
}
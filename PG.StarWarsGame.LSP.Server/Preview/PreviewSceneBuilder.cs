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
    private const string HardPointsTag = "HardPoints";
    private const string ModelToAttachTag = "Model_To_Attach";
    private const string AttachmentBoneTag = "Attachment_Bone";

    /// <summary>What both shipped corpora write, and what the engine falls back to.</summary>
    private const float DefaultHullVsHardpointsConstraint = 0.2f;

    private readonly IMessageCatalog _messages = messages;
    private readonly FactionSectionBuilder _factions = new(schema, tagSource);
    private readonly ReticleSectionBuilder _reticles = new(tagSource, messages);
    private readonly AnimationSectionBuilder _animations = new(messages);
    private readonly ProjectileSectionBuilder _projectiles = new(tagSource, messages);
    private readonly WeaponSectionBuilder _weapons = new(tagSource, messages);
    private readonly AbilitySectionBuilder _abilities = new(messages);

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
        return BuildForModel(indexService.Current, modelReference);
    }

    private PreviewScene BuildForModel(GameIndex index, string modelReference)
    {
        var path = PreviewModelReference.ModelPath(modelReference);
        var location = assets.Locate(path);
        var resolved = location is not null;

        var problems = new List<PreviewProblem>();
        if (!resolved)
            problems.Add(new PreviewProblem(DiagnosticIds.PreviewModelNotFound, "error",
                _messages.Format("PreviewScene_ModelWasNotFound", modelReference, assets.Tiers.Explain(_messages))));

        var kind = PreviewSceneKind.Model;
        if (resolved)
            switch (AloFile.Classify(assets.Read(path) ?? []))
            {
                case AloFileKind.Particle:
                    kind = PreviewSceneKind.Particle;
                    break;

                case AloFileKind.Unknown:
                    problems.Add(new PreviewProblem(DiagnosticIds.PreviewNotAModel, "error",
                        _messages.Format("PreviewScene_NotAModel", modelReference)));
                    break;

                case AloFileKind.Model:
                default:
                    // The kind it started as.
                    break;
            }

        var part = new PreviewPart("hull", modelReference, null, null, PreviewPartOrigin.Hull, null,
            resolved);

        var particles = kind == PreviewSceneKind.Model && resolved
            ? ParticlesOf(part, [], problems)
            : [];

        return new PreviewScene(
            kind,
            modelReference,
            [part],
            [],
            _factions.Factions(index),
            problems,
            assets.Tiers,
            null,
            particles,
            AnimationSectionBuilder.AnimationsFor(index, modelReference),
            resolved ? CamerasOf(modelReference, problems) : [])
        {
            SourceFile = SourceFileOf(location)
        };
    }

    /// <summary>
    ///     The file an external tool would be handed, or null when the model resolved nowhere.
    /// </summary>
    /// <remarks>
    ///     An archive hit's resolved path is the path INSIDE the MEG, which no tool can open - so it
    ///     travels as "packed" with no path rather than as something that looks openable.
    /// </remarks>
    private static PreviewSourceFile? SourceFileOf(GameAssetLocation? location)
    {
        if (location is null) return null;

        return location.Tier == GameAssetTier.Archive
            ? new PreviewSourceFile(null, true)
            : new PreviewSourceFile(location.ResolvedPath, false);
    }

    /// <summary>
    ///     The cameras a model carries in its own skeleton.
    /// </summary>
    /// <remarks>
    ///     Its own read, and a cheap one: <c>SkipGeometry</c> wants the skeleton and nothing else,
    ///     which is the same reason the particle resolver reads it that way. A camera that cannot be
    ///     read costs the shot and nothing else, so a malformed file is reported at warning and the
    ///     rest of the scene stands.
    /// </remarks>
    private IReadOnlyList<PreviewCamera> CamerasOf(
        string modelReference, List<PreviewProblem> problems)
    {
        var bytes = assets.Read(PreviewModelReference.ModelPath(modelReference));
        if (bytes is null || AloFile.Classify(bytes) != AloFileKind.Model)
            return [];

        try
        {
            return PreviewCameraReader.From(AloModelReader.Read(bytes, AloReadOptions.SkipGeometry));
        }
        catch (AloFormatException e)
        {
            problems.Add(new PreviewProblem(DiagnosticIds.PreviewModelUnreadable, "warning",
                _messages.Format("PreviewScene_CouldNotBeReadIts", modelReference, e.Message)));
            return [];
        }
    }

    /// <summary>
    ///     A scene for an animation file: the model it drives, with the clip already selected.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         An <c>.ala</c> names no model, so the pairing is recovered from the filename - the
    ///         longest model name that prefixes it - and then VERIFIED, never assumed. Verification is
    ///         not optional: FoC re-exported some models without re-exporting their animations, so a
    ///         file sitting right next to its apparent model can belong to a different skeleton, and
    ///         50 shipped animations match no model at all.
    ///     </para>
    ///     <para>
    ///         Candidates come from the indexed bone catalog rather than a directory scan, which means
    ///         no filesystem walk and the same layered view of the game the rest of the server has.
    ///     </para>
    /// </remarks>
    public PreviewScene BuildForAnimation(string animationReference)
    {
        var animation = PreviewModelReference.Normalise(animationReference);
        var stem = Path.GetFileNameWithoutExtension(animation).ToLowerInvariant();
        var index = indexService.Current;

        var bytes = assets.Read(PreviewModelReference.ModelPath(animation));
        AlamoAnimationContent? clip = null;

        if (bytes is not null)
            try
            {
                clip = AlaAnimationReader.Read(bytes);
            }
            catch (AloFormatException)
            {
                // Reported below as "does not match any model" - a file that will not parse cannot be
                // paired either, and one message is clearer than two.
            }

        var candidates = index.ModelBones.Keys
            .Where(key => stem.StartsWith(
                Path.GetFileNameWithoutExtension(key) + "_", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(key => key.Length)
            .ToList();

        // ReSharper disable once ForeachCanBePartlyConvertedToQueryUsingAnotherGetEnumerator - the
        // first fitting model ends the search with a built scene; a query would bury that exit.
        foreach (var key in candidates)
        {
            if (clip is null || !clip.MatchesModel(index.ModelBones[key]))
                continue;

            var scene = BuildForModel(index, key);
            return scene with
            {
                Subject = animation,
                Problems = [.. scene.Problems]
            };
        }

        var reason = candidates.Count == 0
            ? _messages.Format("PreviewScene_NoModelForAnimation", animation)
            : _messages.Format("PreviewScene_SkeletonMismatch", animation, string.Join(", ", candidates.Take(3)));

        return PreviewScene.NotFound(animation, reason, assets.Tiers);
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

        // Whether the hardpoints are a route to this object's death at all. Read once here rather
        // than per hardpoint: it is the owner's tag, not theirs.
        var diesWithHardpoints = EngineBoolean.IsTrueUnlessDenied(
            effective.ValueOf("Should_Be_Destroyed_When_All_Hardpoints_Destroyed"));

        foreach (var hardpointId in HardpointIds(effective))
            AddHardpoint(index, resolver, hull, hardpointId, diesWithHardpoints,
                parts, hardpoints, weapons, problems);

        // The unit's own armament, for the 188 objects that carry a WEAPON behaviour instead of - or
        // as well as - hardpoints. Nine objects across the two trees have both.
        if (_weapons.UnitWeapon(index, effective, hull, problems) is { } unitWeapon)
            weapons.Add(unitWeapon);

        var animationSource = _animations.ResolveAnimationOverride(index, effective, hull, problems);

        // Read once: the scene carries it and the check below compares against it.
        var stages = DamageStages(effective);
        ReportUntaggedStages(index, effective.ObjectId, hull, stages, problems);

        if (hull is not null && !parts[0].Resolved)
            problems.Add(new PreviewProblem(DiagnosticIds.PreviewModelNotFound, "error",
                _messages.Format("PreviewScene_ModelWasNotFound", hull, assets.Tiers.Explain(_messages))));

        // Every part, not just the hull: a turret carries its own muzzle flashes and engine glow.
        var particles = parts
            .Where(part => part.Resolved)
            .SelectMany(part => ParticlesOf(part, hardpoints, problems))
            .ToList();

        return new PreviewScene(PreviewSceneKind.Object, effective.ObjectId, parts, hardpoints,
            _factions.Factions(index), problems, assets.Tiers, animationSource, particles,
            // The clips are named after the model whose skeleton they were authored against, which
            // is the OVERRIDE when a unit borrows another model's set.
            AnimationSectionBuilder.AnimationsFor(index, animationSource ?? hull ?? string.Empty),
            // The HULL's cameras only. A turret carries its own skeleton and could in principle
            // declare one, but it was aimed at the turret, and the subject here is the whole unit.
            hull is null ? [] : CamerasOf(hull, problems),
            effective.ValueOf("Type"),
            PreviewTagReader.CategoryTokens(effective.ValueOf("CategoryMask")),
            BreakoffProps(index, resolver, hardpoints, problems),
            weapons,
            _reticles.Reticles(index, hardpoints, problems),
            _projectiles.Projectiles(resolver, weapons, problems),
            // NAMED from here on. The positional list is twenty arguments long and every optional
            // one is a list or a record, so inserting in the wrong slot type-checks against its
            // neighbour often enough to be dangerous - it did, once.
            Defence(index, effective, hardpoints),
            _abilities.Abilities(effective, particles,
                AnimationSectionBuilder.AnimationsFor(index, animationSource ?? hull ?? string.Empty), problems,
                icons, index.Localisation),
            DeathClones(index, resolver, effective, problems),
            SpinAway(effective),
            effective.ValueOf("Death_Explosions"),
            _projectiles.ProjectileCatalog(index),
            // What it wears when no faction colour applies, which is most of the time - its own
            // colour first, and whose fallback to use when it declares none.
            PreviewTagReader.Colour(effective, "No_Colorization_Color"),
            PreviewTagReader.FirstToken(effective.ValueOf("Affiliation")),
            stages,
            DamageTable(effective),
            EngineScaleFactor.Of(effective.ValueOf("Scale_Factor")),
            effective.ValueOf(EncyclopediaTags.IconName));
    }

    /// <summary>
    ///     Reports a declared damage stage the hull's model tags nothing for.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Read off <see cref="GameIndex.ModelBones" /> - bones UNION mesh names - rather than
    ///         from a fresh read of the file, so this and the XML validator's own version of the
    ///         check answer from the same source and cannot disagree about one object. The catalogue
    ///         holds no PROXY names, which measures out as no loss: of the 149 shipped models
    ///         carrying an ALT-tagged proxy, not one tags a level that no bone or mesh also tags.
    ///     </para>
    ///     <para>
    ///         The HULL only. A hardpoint mount is its own render object and the declaration belongs
    ///         to the unit; none of the six shipped objects this fires on carries a hardpoint
    ///         anyway, so widening it to the parts would buy nothing and let a turret answer for a
    ///         stage the hull is missing.
    ///     </para>
    ///     <para>
    ///         Silent when the model is not catalogued at all. That is undecidable rather than
    ///         wrong, and the warning would be about the index rather than about the file.
    ///     </para>
    /// </remarks>
    private void ReportUntaggedStages(GameIndex index,
        string objectId, string? hull, IReadOnlyList<int> stages, List<PreviewProblem> problems)
    {
        if (hull is null || stages.Count == 0)
            return;

        if (!index.ModelBones.TryGetValue(ModelBoneKey.From(hull), out var names))
            return;

        var missing = ModelLevelTag.StagesNotTagged(stages, ModelLevelTag.AltLevelsIn(names));
        if (missing.Count == 0)
            return;

        var which = missing.Count == 1
            ? _messages.Format("DamageStageNotOnModel_Stage", missing[0])
            : _messages.Format("DamageStageNotOnModel_Stages", string.Join(", ", missing));

        problems.Add(new PreviewProblem(DiagnosticIds.PreviewDamageStageNotInModel, "warning",
            _messages.Format("PreviewScene_DamageStageUntagged", objectId, which, hull)));
    }

    /// <summary>
    ///     What this object leaves behind when it dies, one entry per declared damage type.
    /// </summary>
    /// <remarks>
    ///     Straight off <c>effective.Tags</c>, with no <c>RepeatedTagReader</c>: <c>Death_Clone</c>
    ///     is the one tag in the schema with <c>variantMode: merge</c> plus <c>multipleAllowed</c>,
    ///     so the resolver already keeps every occurrence - and keeps a variant's base clones too,
    ///     which the schema calls out as deliberate.
    /// </remarks>
    private List<PreviewDeathClone> DeathClones(GameIndex index,
        EffectiveObjectResolver resolver, EffectiveObject effective, List<PreviewProblem> problems)
    {
        // On the OBJECT, not on the row, so it is the same answer for every clone it names.
        var playsIdle = effective.BoolOf("Should_Death_Clone_Play_Idle");
        var clones = new List<PreviewDeathClone>();

        foreach (var tag in effective.Tags.Where(t =>
                     t.TagName.Equals("Death_Clone", StringComparison.OrdinalIgnoreCase)))
        {
            var row = tag.Value.Split(',', StringSplitOptions.TrimEntries);

            // One of foc's 345 rows is empty, and a row naming no object names no clone. Padding it
            // would invent one.
            if (row.Length < 2 || row[1].Length == 0)
                continue;

            var clone = resolver.Resolve(row[1]);

            if (!clone.Found)
            {
                // A real mistake, unlike an unbound ability effect: the wreck simply never appears
                // and the game says nothing about it.
                problems.Add(new PreviewProblem(DiagnosticIds.PreviewDeathCloneNotDefined, "warning",
                    _messages.Format("PreviewScene_DeathCloneNotDefinedThis", row[1])));
                clones.Add(new PreviewDeathClone(PreviewTagReader.Blank(row[0]), row[1], null, playsIdle));
                continue;
            }

            var model = clone.ValueOf("Space_Model_Name") ?? clone.ValueOf("Land_Model_Name");

            // Its OWN model's clips and effects. Everything else on the wire describes the subject,
            // and a clone is a replacement for it rather than a piece of it.
            clones.Add(new PreviewDeathClone(PreviewTagReader.Blank(row[0]), row[1], model, playsIdle,
                model is null ? [] : AnimationSectionBuilder.AnimationsFor(index, model),
                model is null ? [] : ProxiesOf(model, row[1], [], problems),
                // The clone's OWN scale, read off the resolved clone - variants included - through
                // the same reader the subject's scale uses.
                EngineScaleFactor.Of(clone.ValueOf("Scale_Factor"))));
        }

        return clones;
    }

    /// <summary>
    ///     What a hit on this subject has to get through.
    /// </summary>
    /// <remarks>
    ///     The previewed object is the TARGET - the attacker is a weapon the reader builds - so this
    ///     is the defensive half of the damage model and the only half that comes from the file.
    ///     Only the two armor COLUMNS this target uses are sent; see <see cref="PreviewTargetDefence" />.
    /// </remarks>
    private PreviewTargetDefence Defence(GameIndex index,
        EffectiveObject effective, IReadOnlyList<PreviewHardpoint> hardpoints)
    {
        var table = PreviewDamageTable.From(index, tagSource);

        var armor = effective.ValueOf("Armor_Type");
        var shieldArmor = effective.ValueOf("Shield_Armor_Type");

        // Only the hardpoints that can actually die. An indestructible one can never be part of a
        // death, so counting its health would put a floor under the bar that nothing could remove.
        var destructible = hardpoints.Where(h => h.IsDestroyable).ToList();

        return new PreviewTargetDefence(
            IsShielded(effective),
            string.IsNullOrWhiteSpace(armor) ? null : armor.Trim(),
            string.IsNullOrWhiteSpace(shieldArmor) ? null : shieldArmor.Trim(),
            effective.FloatOf("Shield_Points"),
            effective.FloatOf("Tactical_Health"),
            effective.FloatOf("Energy_Capacity"),
            destructible.Count == 0 ? null : destructible.Sum(h => h.Health ?? 0f),
            EngineBoolean.IsTrueUnlessDenied(
                effective.ValueOf("Should_Be_Destroyed_When_All_Hardpoints_Destroyed")),
            HullVsHardpointsConstraint(index),
            table.FactorsFor(armor),
            table.FactorsFor(shieldArmor),
            table.DamageTypes);
    }

    /// <summary>
    ///     <c>Hull_Vs_Hard_Points_Health_Constraint</c>, or the shipped 0.2 when GameConstants does
    ///     not set it to a number.
    /// </summary>
    /// <remarks>
    ///     The default is 0.2 rather than 0 deliberately: 0 would tie the two pools together
    ///     exactly, which is the opposite of the constant being absent. A value the engine could not
    ///     parse is treated the same as no value at all.
    /// </remarks>
    private float HullVsHardpointsConstraint(GameIndex index)
    {
        foreach (var value in RepeatedTagReader.Values(
                     index, tagSource, EncyclopediaTags.GameConstantsId,
                     "Hull_Vs_Hard_Points_Health_Constraint"))
            if (EngineNumber.ReadFloat(value) is { } parsed)
                return parsed;

        return DefaultHullVsHardpointsConstraint;
    }

    /// <summary>Whether any behaviour list names <c>SHIELDED</c>.</summary>
    /// <remarks>
    ///     <para>
    ///         All THREE lists, unlike <see cref="HasWeaponBehavior" />, and the asymmetry is
    ///         measured rather than assumed. Over foc, <c>SHIELDED</c> appears in
    ///         <c>SpaceBehavior</c> 98 times, in plain <c>Behavior</c> 32 and in <c>LandBehavior</c>
    ///         19; <c>WEAPON</c> never appears in plain <c>Behavior</c> at all. Reading only two
    ///         lists here would call a fifth of the shielded objects unshielded.
    ///     </para>
    ///     <para>
    ///         Whole tokens, so <c>UNSHIELDED</c> does not read as <c>SHIELDED</c>.
    ///     </para>
    /// </remarks>
    private static bool IsShielded(EffectiveObject effective)
    {
        return PreviewTagReader.BehaviorListTags
            .SelectMany(tag => (effective.ValueOf(tag) ?? string.Empty)
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .Any(token => token.Equals("SHIELDED", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    ///     The wreckage this subject's hardpoints shed, one entry per DISTINCT prop.
    /// </summary>
    /// <remarks>
    ///     Mirrored hardpoints share a prop - the Star Destroyer's four weapon batteries pair up - so this
    ///     is deduplicated rather than emitted per hardpoint, which would have the client instantiate
    ///     the same wreck twice.
    /// </remarks>
    private List<PreviewBreakoffProp> BreakoffProps(GameIndex index,
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
                    _messages.Format("PreviewScene_BreakoffPropNotDefined", id)));
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
    private IReadOnlyList<PreviewParticle> ParticlesOf(
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
    private IReadOnlyList<PreviewParticle> ProxiesOf(
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
                _messages.Format("PreviewScene_CouldNotBeReadIts2", modelRef, e.Message)));
            return [];
        }
    }

    // ── hardpoints ────────────────────────────────────────────────────────────

    private static List<string> HardpointIds(EffectiveObject effective)
    {
        return effective.ValueOf(HardPointsTag) is { } raw
            ? XmlUtility.SplitList(raw)
            : [];
    }

    private void AddHardpoint(GameIndex index,
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
                _messages.Format("PreviewScene_HardpointMountedButNotDefined", hardpointId), hardpointId));
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
                    _messages.Format("PreviewScene_HardpointAttachesModelWhichWas", hardpointId, model),
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
                ? _messages.Format("PreviewScene_PlacedAtOrigin")
                : _messages.Format("PreviewScene_PlacedAtFireBone", fireBoneA);
            problems.Add(isTargetable
                ? new PreviewProblem(DiagnosticIds.PreviewTargetableHardpointNoBone, "error",
                    _messages.Format(string.IsNullOrEmpty(fireBoneA)
                            ? "PreviewScene_TargetableNoBoneUnkillable"
                            : "PreviewScene_TargetableNoBone",
                        hardpointId, AttachmentBoneTag, placed),
                    hardpointId)
                : new PreviewProblem(DiagnosticIds.PreviewHardpointNoBone, "error",
                    _messages.Format("PreviewScene_HardpointNoBone", hardpointId, AttachmentBoneTag, placed),
                    hardpointId));
        }
        else if (hull is not null &&
                 !HardpointBoneModelResolver.ModelHasBone(index, hull, attachBone))
        {
            problems.Add(new PreviewProblem(DiagnosticIds.PreviewHardpointBoneMissing, "warning",
                _messages.Format("PreviewScene_HardpointBoneMissing", hardpointId, attachBone, hull), hardpointId));
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
                _messages.Format("PreviewScene_HardpointUnreachable", hardpointId),
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
    ///     The damage stages an object declares, ascending and unique.
    /// </summary>
    /// <remarks>
    ///     Non-numeric entries are dropped rather than reported. This column sits beside
    ///     <c>Land_Damage_SFX</c>, which uses the literal <c>null</c> as its "no sound" placeholder.
    ///     What the preview needs from THIS column is which stages exist.
    ///     <para>
    ///         The looseness is in the SFX column alone, and worth stating precisely because it used
    ///         to read as a reason to distrust the whole table: measured over foc with XML comments
    ///         stripped, 42 objects declare an SFX count that disagrees with their alternates - but
    ///         <c>Land_Damage_Thresholds</c> disagrees in ZERO of the 219. The threshold pairing is
    ///         sound, which is what lets <see cref="DamageTable" /> rely on it.
    ///     </para>
    /// </remarks>
    private static IReadOnlyList<int> DamageStages(EffectiveObject effective)
    {
        var raw = effective.ValueOf("Land_Damage_Alternates");
        if (string.IsNullOrWhiteSpace(raw))
            return [];

        var stages = new SortedSet<int>();
        foreach (var token in raw.Split(
                     ',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            if (int.TryParse(token, out var stage) && stage >= 0)
                stages.Add(stage);

        return [.. stages];
    }

    /// <summary>
    ///     The damage table as ordered bands, or empty when the object declares none usable.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The pairing is POSITIONAL, so this keeps the written order and does not sort or
    ///         de-duplicate anything - a stage may legitimately appear in more than one band.
    ///     </para>
    ///     <para>
    ///         Empty rather than salvaged when the two columns disagree in length. Inventing an
    ///         alignment would put the wrong mesh on screen at the wrong health, which is worse than
    ///         leaving the reader the manual slider. Measured over foc with XML comments stripped:
    ///         219 objects declare the table and the two columns NEVER disagree, so this guard is an
    ///         invariant being held rather than a case being handled. (The "42 objects declare
    ///         counts that do not match" beside <see cref="DamageStages" /> is about
    ///         <c>Land_Damage_SFX</c>, a different column.)
    ///     </para>
    /// </remarks>
    private static IReadOnlyList<PreviewDamageBand> DamageTable(EffectiveObject effective)
    {
        var stages = PreviewTagReader.Numbers<int>(effective.ValueOf("Land_Damage_Alternates"), int.TryParse);
        var thresholds = PreviewTagReader.Numbers<float>(effective.ValueOf("Land_Damage_Thresholds"), float.TryParse);

        if (stages.Count == 0 || stages.Count != thresholds.Count)
            return [];

        return [.. stages.Select((stage, at) => new PreviewDamageBand(thresholds[at], stage))];
    }

    // ── factions ──────────────────────────────────────────────────────────────

    // ── tag reading ───────────────────────────────────────────────────────────

    /// <summary>
    ///     How the subject comes apart when it declares no death clone, or null where it does not.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Read off the TAG, not the locomotor. The 34 shipped objects that declare it are not
    ///         only fighters - they include a <c>GroundVehicle</c> and a <c>LandBombingUnit</c> - and
    ///         no shipped object declares it without meaning it, so the tag is the whole condition.
    ///     </para>
    ///     <para>
    ///         The four numbers all come out of the file. Only the corkscrew itself is invented, and
    ///         that is the client's business - nothing in any file describes it.
    ///     </para>
    /// </remarks>
    private static PreviewSpinAway? SpinAway(EffectiveObject effective)
    {
        if (!effective.BoolOf("Spin_Away_On_Death"))
            return null;

        return new PreviewSpinAway(
            // 2.0 where the file is silent: 31 of the 34 write exactly that, so it is the corpus's
            // own answer rather than a number picked to fill a gap.
            effective.FloatOf("Spin_Away_On_Death_Time") ?? 2.0f,
            effective.FloatOf("Spin_Away_On_Death_Chance") ?? 0f,
            effective.ValueOf("Spin_Away_On_Death_Explosion"),
            effective.FloatOf("Max_Speed") ?? 0f);
    }

}
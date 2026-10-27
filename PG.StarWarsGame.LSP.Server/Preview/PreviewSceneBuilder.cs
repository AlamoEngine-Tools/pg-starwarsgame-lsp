// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Globalization;
using System.Runtime.CompilerServices;
using PG.StarWarsGame.LSP.Assets.Icons;
using PG.StarWarsGame.LSP.Assets.Models;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Localisation;
using PG.StarWarsGame.LSP.Core.Messages;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Server.Abilities;
using PG.StarWarsGame.LSP.Server.Assets;
using PG.StarWarsGame.LSP.Xml.Util;
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
    private const string FactionTypeName = "Faction";

    /// <summary>What both shipped corpora write, and what the engine falls back to.</summary>
    private const float DefaultHullVsHardpointsConstraint = 0.2f;

    /// <summary>
    ///     The engine's defaults for the two turret extents, from its object type constructor.
    /// </summary>
    /// <remarks>
    ///     Both comparisons in the cone-of-fire test are then always true, so an object that
    ///     sets neither fires in ANY direction. 175 of the 291 objects with a <c>WEAPON</c>
    ///     behaviour set neither, which makes unrestricted the COMMON case - reading an absent tag
    ///     as zero gave the majority of armed units the narrowest arc possible.
    /// </remarks>
    private const float DefaultTurretRotateExtentDegrees = 360f;

    private const float DefaultTurretElevateExtentDegrees = 180f;

    /// <summary>
    ///     The same two defaults for a HARDPOINT turret, which are not the same numbers.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <c>HardPointDataClass</c> writes
    ///         <c>TurretRotateExtentDegrees = 180.0</c> and
    ///         <c>TurretElevateExtentDegrees = 90.0</c>. A hardpoint's turret fields are a parallel
    ///         set to the unit's rather than the same ones, and so are their defaults.
    ///     </para>
    ///     <para>
    ///         180 is the exact threshold at which
    ///         the turret-angle calculation skips its motion clamp - the
    ///         gate is <c>extent &lt; 180.0 &amp;&amp; extent &gt; 0.0</c> - so the default yaw is a
    ///         free turn and the default pitch is a real stop at plus or minus 90.
    ///     </para>
    /// </remarks>
    private const float DefaultHardpointRotateExtentDegrees = 180f;

    private const float DefaultHardpointElevateExtentDegrees = 90f;

    /// <summary>A full angle covers everything at 360; there is nothing past a whole turn.</summary>
    private const float FullTurnDegrees = 360f;

    /// <summary>
    ///     The animation-set overrides, land first.
    /// </summary>
    /// <remarks>
    ///     Only the land tag appears in the shipped data - about 47 uses, all infantry and indigenous -
    ///     but the space one is in the schema and in the engine's own parser, so a mod may well use it.
    /// </remarks>
    private static readonly string[] AnimationOverrideTags =
        ["Land_Model_Anim_Override_Name", "Space_Model_Anim_Override_Name"];

    private static readonly string[] ShieldBehaviorTags = ["Behavior", "SpaceBehavior", "LandBehavior"];

    /// <summary>
    ///     Every projectile the tree defines, resolved.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The attacker panel picks from all of them, not from the handful this subject happens
    ///         to fire - you are building a weapon to shoot AT the subject, so its own armament is
    ///         the wrong list entirely.
    ///     </para>
    ///     <para>
    ///         NAMES only. Resolving all 105 through the variant chain was measured at 830ms added
    ///         to every scene open - a real cost for a list most readers scroll past - against about
    ///         nothing for the behaviour scan that finds them. The values for the ones this subject
    ///         actually fires are already on the wire; the rest are fetched when one is picked.
    ///     </para>
    /// </remarks>
    /// <summary>
    ///     Cached per index, because finding the catalogue costs a parse of the whole workspace.
    /// </summary>
    /// <remarks>
    ///     <c>TryGetTags</c> parses a document the first time anything asks about an object in it,
    ///     so reading a behaviour off EVERY object pulls every XML file through the parser -
    ///     measured at about 830ms on the eaw tree. Cached against the index instance, the cost is
    ///     paid once rather than on every scene open. It should really move off the scene request
    ///     altogether and onto one the panel makes when the attacker section first opens.
    /// </remarks>
    private static readonly ConditionalWeakTable<GameIndex, List<string>> CatalogCache = new();

    private static readonly string[] BehaviorTags = ["SpaceBehavior", "LandBehavior"];

    private static readonly string[] FireModeTags =
    [
        "Fire_When_Deployed", "Fire_When_Undeployed", "Fire_When_In_Rocket_Attack_Mode",
        "Fire_When_In_Normal_Attack_Mode", "Fire_When_In_Defend_Mode"
    ];

    private readonly IMessageCatalog _messages = messages;

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
            Factions(index, new EffectiveObjectResolver(index, schema, tagSource)),
            problems,
            assets.Tiers,
            null,
            particles,
            AnimationsFor(index, modelReference),
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
        if (UnitWeapon(index, effective, hull, problems) is { } unitWeapon)
            weapons.Add(unitWeapon);

        var animationSource = ResolveAnimationOverride(index, effective, hull, problems);

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
            Factions(index, resolver), problems, assets.Tiers, animationSource, particles,
            // The clips are named after the model whose skeleton they were authored against, which
            // is the OVERRIDE when a unit borrows another model's set.
            AnimationsFor(index, animationSource ?? hull ?? string.Empty),
            // The HULL's cameras only. A turret carries its own skeleton and could in principle
            // declare one, but it was aimed at the turret, and the subject here is the whole unit.
            hull is null ? [] : CamerasOf(hull, problems),
            effective.ValueOf("Type"),
            CategoryTokens(effective.ValueOf("CategoryMask")),
            BreakoffProps(index, resolver, hardpoints, problems),
            weapons,
            Reticles(index, hardpoints, problems),
            Projectiles(resolver, weapons, problems),
            // NAMED from here on. The positional list is twenty arguments long and every optional
            // one is a list or a record, so inserting in the wrong slot type-checks against its
            // neighbour often enough to be dangerous - it did, once.
            Defence(index, effective, hardpoints),
            Abilities(effective, particles,
                AnimationsFor(index, animationSource ?? hull ?? string.Empty), problems,
                icons, index.Localisation),
            DeathClones(index, resolver, effective, problems),
            SpinAway(effective),
            effective.ValueOf("Death_Explosions"),
            ProjectileCatalog(index),
            // What it wears when no faction colour applies, which is most of the time - its own
            // colour first, and whose fallback to use when it declares none.
            Colour(effective, "No_Colorization_Color"),
            FirstToken(effective.ValueOf("Affiliation")),
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
                clones.Add(new PreviewDeathClone(Blank(row[0]), row[1], null, playsIdle));
                continue;
            }

            var model = clone.ValueOf("Space_Model_Name") ?? clone.ValueOf("Land_Model_Name");

            // Its OWN model's clips and effects. Everything else on the wire describes the subject,
            // and a clone is a replacement for it rather than a piece of it.
            clones.Add(new PreviewDeathClone(Blank(row[0]), row[1], model, playsIdle,
                model is null ? [] : AnimationsFor(index, model),
                model is null ? [] : ProxiesOf(model, row[1], [], problems),
                // The clone's OWN scale, read off the resolved clone - variants included - through
                // the same reader the subject's scale uses.
                EngineScaleFactor.Of(clone.ValueOf("Scale_Factor"))));
        }

        return clones;
    }

    private static string? Blank(string value)
    {
        return value.Length == 0 ? null : value;
    }

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
    private List<PreviewAbility> Abilities(
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
                _messages.Format("PreviewScene_UnboundAbilityEffect", string.Join(", ", names), ability)));

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
                _messages));
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
        return ShieldBehaviorTags
            .SelectMany(tag => (effective.ValueOf(tag) ?? string.Empty)
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .Any(token => token.Equals("SHIELDED", StringComparison.OrdinalIgnoreCase));
    }

    private List<string> ProjectileCatalog(GameIndex index)
    {
        if (CatalogCache.TryGetValue(index, out var cached))
            return cached;

        var built = BuildProjectileCatalog(index);
        CatalogCache.AddOrUpdate(index, built);

        return built;
    }

    private List<string> BuildProjectileCatalog(GameIndex index)
    {
        // By BEHAVIOUR, which the user pointed at and which measurement then settled outright: over
        // the eaw tree, `PROJECTILE` in a behaviour list appears on 0 non-projectiles, and following
        // `Variant_Of_Existing_Type` from the objects that declare it reaches 102 of 102. Exact,
        // both ways.
        //
        // Nothing else works. Every game object shares the one registered type - `GameObjectType`
        // covers units, structures, props, planets and projectiles alike - and the best tag marker,
        // `Projectile_Damage`, is on only 83% of projectiles and on 58 hardpoints besides.
        var declared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (id, _) in index.WorkspaceDefinitions)
            if (DeclaresProjectileBehavior(index, id))
                declared.Add(id);

        // Then everything that VARIES one of them. Read off the symbol's own `VariantBaseId`, so no
        // object has to be resolved to find out - 54 of foc's 173 projectiles are variants that
        // restate nothing but their model and colour.
        var ids = new SortedSet<string>(declared, StringComparer.OrdinalIgnoreCase);

        foreach (var (id, symbols) in index.WorkspaceDefinitions)
        {
            var at = symbols.FirstOrDefault()?.VariantBaseId;

            // Bounded by the object count: a mod can write a variant cycle, and the preview reports
            // one rather than being attached to it.
            for (var hops = 0; !string.IsNullOrEmpty(at) && hops < 64; hops++)
            {
                if (declared.Contains(at))
                {
                    ids.Add(id);
                    break;
                }

                at = index.Resolve(at)?.VariantBaseId;
            }
        }

        return [.. ids];
    }

    /// <summary>
    ///     The projectiles this subject's weapons name, one entry per DISTINCT projectile.
    /// </summary>
    /// <remarks>
    ///     Only what this subject fires, not the 173 in the tree. Deduplicated because a ship's
    ///     mirrored batteries fire the same bolt and a copy each would have the client build the
    ///     same material twice.
    /// </remarks>
    private List<PreviewProjectile> Projectiles(
        EffectiveObjectResolver resolver,
        IReadOnlyList<PreviewWeapon> weapons,
        List<PreviewProblem> problems)
    {
        return ProjectilesFor(
            resolver,
            weapons.Select(w => w.ProjectileType).Where(id => !string.IsNullOrEmpty(id))
                .Select(id => id!),
            problems,
            _messages);
    }

    /// <summary>Whether an object's own behaviour lists name <c>PROJECTILE</c>.</summary>
    /// <remarks>
    ///     Read from the raw tag sources rather than through the resolver: this runs over every
    ///     object in the workspace, and a variant chain per object would turn a dictionary scan into
    ///     real work. Inheritance is handled separately, by walking the symbols' own base ids.
    /// </remarks>
    private bool DeclaresProjectileBehavior(GameIndex index, string objectId)
    {
        return ShieldBehaviorTags
            .SelectMany(tagName => RepeatedTagReader.Values(index, tagSource, objectId, tagName))
            .SelectMany(value => value.Split(
                ',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .Any(token => token.Equals("PROJECTILE", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Resolves a list of projectile ids, skipping repeats.</summary>
    /// <remarks>
    ///     Internal rather than private because <c>aet/getProjectile</c> resolves one on demand for
    ///     the attacker panel, which offers the whole tree and not just what this subject fires. The
    ///     twenty-odd tags below are the definition of what a projectile IS to the preview, and a
    ///     second reader of them would be free to drift.
    /// </remarks>
    internal static List<PreviewProjectile> ProjectilesFor(
        EffectiveObjectResolver resolver,
        IEnumerable<string> ids,
        List<PreviewProblem> problems,
        IMessageCatalog messages)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var projectiles = new List<PreviewProjectile>();

        foreach (var id in ids.Where(seen.Add))
        {
            var effective = resolver.Resolve(id);
            if (!effective.Found)
            {
                problems.Add(new PreviewProblem(DiagnosticIds.PreviewProjectileNotDefined, "warning",
                    messages.Format("PreviewScene_ProjectileNotDefinedThisProject", id)));
                continue;
            }

            var model = effective.ValueOf("Space_Model_Name") ?? effective.ValueOf("Land_Model_Name");

            projectiles.Add(new PreviewProjectile(
                id,
                effective.BoolOf("Projectile_Custom_Render")
                    ? PreviewProjectileRender.CustomQuad
                    : PreviewProjectileRender.Model,
                model,
                effective.FloatOf("Projectile_Width"),
                effective.FloatOf("Projectile_Length"),
                effective.ValueOf("Projectile_Texture_Slot"),
                effective.ValueOf("Projectile_Laser_Color"),
                effective.FloatOf("Max_Speed"),
                effective.FloatOf("Projectile_Max_Flight_Distance"),
                effective.FloatOf("Max_Rate_Of_Turn"),
                effective.FloatOf("Projectile_Damage"),
                effective.ValueOf("Damage_Type"),
                effective.ValueOf("Projectile_Category"),
                effective.BoolOf("Projectile_Does_Shield_Damage"),
                effective.BoolOf("Projectile_Does_Energy_Damage"),
                effective.BoolOf("Projectile_Does_Hitpoint_Damage"),
                effective.FloatOf("Projectile_Blast_Area_Damage"),
                effective.FloatOf("Projectile_Blast_Area_Range"),
                effective.IntOf("Projectile_Blast_Area_Max_Victims"),
                effective.BoolOf("Projectile_Blast_Area_Dropoff"),
                effective.IntOf("Projectile_Blast_Area_Dropoff_Tiers"),
                effective.ValueOf("Projectile_Object_Detonation_Particle"),
                effective.ValueOf("Projectile_Absorbed_By_Shields_Particle"),
                effective.ValueOf("Projectile_SFXEvent_Detonate")));
        }

        return projectiles;
    }

    /// <summary>
    ///     The reticle art for the hardpoint types this subject actually mounts, without the images.
    /// </summary>
    /// <remarks>
    ///     Only the types present, not all thirteen. The PNGs are attached later by
    ///     <c>GetPreviewSceneHandler</c>, which is where the icon catalog lives - resolving them is
    ///     asynchronous and this builder is not.
    /// </remarks>
    private PreviewReticles? Reticles(GameIndex index,
        IReadOnlyList<PreviewHardpoint> hardpoints, List<PreviewProblem> problems)
    {
        var targetable = hardpoints
            .Where(h => h.IsTargetable && !string.IsNullOrEmpty(h.Type))
            .Select(h => h.Type!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (targetable.Count == 0)
            return null;

        var map = PreviewReticleMap.From(index, tagSource);
        var byType = new Dictionary<string, PreviewReticleStates>(StringComparer.OrdinalIgnoreCase);

        foreach (var type in targetable)
            if (map.ForType(type) is { } states)
                byType[type] = states;
            else
                problems.Add(new PreviewProblem(DiagnosticIds.PreviewNoReticleForType, "info",
                    _messages.Format("PreviewScene_NoReticle", type)));

        return new PreviewReticles(byType, new Dictionary<string, string>(),
            map.EnemyScreenSize, map.FriendlyScreenSize);
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
                Vector3Tag(effective, "Debris_Movement_Vector"),
                Vector3Tag(effective, "Debris_Facing_Rotate_Vector"),
                effective.FloatOf("Debris_Min_Lifetime_Seconds"),
                effective.FloatOf("Debris_Max_Lifetime_Seconds"),
                effective.ValueOf("Debris_Attached_Particle"),
                effective.ValueOf("Death_Explosions"),
                effective.BoolOf("Remove_Upon_Death"),
                // Its OWN model's, like a death clone's - a burning piece of debris trails its own
                // fire, and `Debris_Attached_Particle` is a second, separate effect the XML names.
                modelRef is null ? [] : AnimationsFor(index, modelRef),
                modelRef is null ? [] : ProxiesOf(modelRef, id, [], problems)));
        }

        return props;
    }

    /// <summary>A comma-separated triple, or null when the tag is absent or malformed.</summary>
    private static PreviewVector3? Vector3Tag(EffectiveObject effective, string tagName)
    {
        var raw = effective.ValueOf(tagName);
        if (string.IsNullOrEmpty(raw))
            return null;

        var parts = raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3)
            return null;

        return float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) &&
               float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) &&
               float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var z)
            ? new PreviewVector3(x, y, z)
            : null;
    }

    /// <summary>
    ///     The animations that belong to a model, by filename.
    /// </summary>
    /// <remarks>
    ///     An <c>.ala</c> names no model, so the pairing is the filename: the clips sit beside their
    ///     model under a <c>&lt;model&gt;_</c> prefix, which is the same rule
    ///     <see cref="BuildForAnimation" /> uses in reverse. The separator matters -
    ///     <c>Ei_bobafettish_wave.ala</c> is not Boba Fett's.
    ///     Offered, not verified: each is checked against the skeleton when it is actually loaded,
    ///     and 50 of the shipped animations sit beside a model they do not fit. Listing one that
    ///     turns out not to match costs an entry in a picker; listing none at all - which is what
    ///     happened while nothing could enumerate them - reads as a model with no animations.
    ///     <para>
    ///         The matching lives in <see cref="ModelAnimationClips" />, shared with the death animation
    ///         diagnostic. The longest model name prefixing a clip owns it there: without that a hull
    ///         collected its own death clone's clips - <c>rv_gargantuan_</c> prefixes
    ///         <c>rv_gargantuan_dc_die_00</c> - and since a clip binds by INDEX and carries VISIBILITY tracks,
    ///         the clone's clip switched off whichever hull bones sat at the indices its own skeleton hides.
    ///         Two of the Gargantuan's turrets were mounted on those bones and went with them.
    ///     </para>
    /// </remarks>
    private static IReadOnlyList<string> AnimationsFor(GameIndex index, string modelReference)
    {
        return ModelAnimationClips.For(index, PreviewModelReference.Normalise(modelReference));
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

    /// <summary>
    ///     The model whose animations this object actually plays, or null when it uses its own.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The override borrows another model's animation set. This used to insist the two
    ///         skeletons be IDENTICAL and report a difference as a warning, on the reasoning that the
    ///         engine could not otherwise bind the tracks. The user reported that the message fires
    ///         all over the BASE GAME, on units that animate correctly in the engine - so the rule
    ///         was simply wrong, and the clips are loaded and bound by index either way.
    ///     </para>
    ///     <para>
    ///         It is still reported, as INFORMATION rather than a warning: where the two skeletons
    ///         disagree about what a bone is called, the clip drives whatever sits at that index, and
    ///         a reader watching a model move oddly deserves to know that is possible. What the exact
    ///         engine rule is - the user's guess is that the animated bones need only share a tree -
    ///         is not settled, and nothing here claims it is.
    ///     </para>
    /// </remarks>
    private string? ResolveAnimationOverride(GameIndex index,
        EffectiveObject effective, string? hull, List<PreviewProblem> problems)
    {
        var overrideModel = AnimationOverrideTags
            .Select(tag => effective.ValueOf(tag))
            .FirstOrDefault(value => value is not null);

        if (overrideModel is null)
            return null;

        if (hull is null)
            return overrideModel;

        var hullBones = index.ModelBones.GetValueOrDefault(ModelBoneKey.From(hull));
        var overrideBones = index.ModelBones.GetValueOrDefault(ModelBoneKey.From(overrideModel));

        // Only compare when both are catalogued; an unknown model is already reported elsewhere.
        if (!hullBones.IsDefaultOrEmpty && !overrideBones.IsDefaultOrEmpty
                                        && !hullBones.SequenceEqual(overrideBones, StringComparer.OrdinalIgnoreCase))
            problems.Add(new PreviewProblem(DiagnosticIds.PreviewAnimationSkeletonMismatch, "info",
                _messages.Format("PreviewScene_AnimationSkeletonDiffers", effective.ObjectId, overrideModel, hull,
                    overrideBones.Length, hullBones.Length)));

        return overrideModel;
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
            Turret(effective)));

        if (Weapon(index, effective, hardpointId) is { } weapon)
            weapons.Add(weapon);
    }

    /// <summary>The turret pose, or null when the hardpoint is not one.</summary>
    private static PreviewTurret? Turret(EffectiveObject effective)
    {
        if (!effective.BoolOf("Is_Turret"))
            return null;

        // EFFECTIVE, not authored: the client clamps a drag handle to these, and the two owners
        // default differently. See DefaultHardpointRotateExtentDegrees.
        return new PreviewTurret(
            effective.FloatOf("Turret_Rest_Angle"),
            effective.FloatOf("Turret_Rotate_Extent_Degrees")
            ?? DefaultHardpointRotateExtentDegrees,
            effective.FloatOf("Turret_Elevate_Extent_Degrees")
            ?? DefaultHardpointElevateExtentDegrees,
            effective.ValueOf("Turret_Bone_Name"),
            effective.ValueOf("Barrel_Bone_Name"));
    }

    /// <summary>
    ///     The unit's own weapon, for the <c>WEAPON</c>-behaviour case, or null when it has none.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         188 objects across the two trees are armed this way against 68 with hardpoints, so
    ///         this is the COMMON case rather than the exception - it just had no support at all.
    ///     </para>
    ///     <para>
    ///         Nothing in the XML says where the shot leaves from: the engine cycles the hull's
    ///         <c>MuzzleA_#</c> bones, one per volley. That convention is resolved here rather than in
    ///         the webview because every other naming rule in this product lives server-side, and
    ///         completion and validation on <c>Turret_Bone_Name</c> want the same knowledge.
    ///     </para>
    /// </remarks>
    private PreviewWeapon? UnitWeapon(GameIndex index,
        EffectiveObject effective, string? hull, List<PreviewProblem> problems)
    {
        if (!HasWeaponBehavior(effective))
            return null;

        var turret = UnitTurret(effective);
        var bones = MuzzleBones(index, hull, turret);

        // ReSharper disable once InvertIf - a guard, read as one; the weapon stays the last word.
        if (bones.Count == 0)
        {
            problems.Add(new PreviewProblem(DiagnosticIds.PreviewNoMuzzleBones, "warning",
                _messages.Format("PreviewScene_NoMuzzleBones", effective.ObjectId)));
            return null;
        }

        return new PreviewWeapon(
            "weapon:A",
            PreviewWeaponSource.Unit,
            null,
            _messages.Format("PreviewScene_Muzzle"),
            bones,
            // Randomize_Between_Fire_Bones is a HardPoint parameter and is not on GameObjectType at
            // all, so this case has no other branch.
            PreviewFirePointMode.CycleBones,
            effective.BoolOf("Fires_Forward"),
            // A list, but only 2 of 95 shipped objects name more than one.
            FirstToken(effective.ValueOf("Projectile_Types")),
            effective.FloatOf("Damage"),
            effective.ValueOf("Damage_Type"),
            effective.FloatOf("Targeting_Max_Attack_Distance"),
            null,
            UnitArcYawDegrees(effective),
            UnitArcPitchDegrees(effective),
            Inaccuracy(index, effective.ObjectId),
            effective.IntOf("Projectile_Fire_Pulse_Count"),
            effective.FloatOf("Projectile_Fire_Pulse_Delay_Seconds"),
            effective.FloatOf("Projectile_Fire_Recharge_Seconds"),
            [],
            turret,
            null,
            UnitArcYawDegrees(effective, true),
            UnitArcPitchDegrees(effective, true));
    }

    /// <summary>
    ///     Whether the engine can ever put this unit in the deployed state.
    /// </summary>
    /// <remarks>
    ///     the is-deployed test requires the type's <c>Deploys</c> flag
    ///     (the is-deployable test, field <c>+0xf6</c>) and then asks the locomotor. The base
    ///     the is-deployed test returns false; only
    ///     the WALK locomotor behaviour answers yes, in <c>LST_WALK_DEPLOYED</c>. Any other locomotor
    ///     with <c>Deploys</c> set is never deployed, so it has no second arc to draw.
    /// </remarks>
    private static bool CanDeploy(EffectiveObject effective)
    {
        return effective.BoolOf("Deploys")
               && ObjectBehaviors.Has(effective, "WALK_LOCOMOTOR");
    }

    /// <summary>
    ///     The unit weapon's horizontal arc as a FULL angle, which is what
    ///     <see cref="PreviewWeapon.ConeWidthDegrees" /> carries.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The conversion is the point. the cone-of-fire test tests
    ///         <c>|yaw| &gt; Turret_Rotate_Extent_Degrees</c> WITHOUT halving, so the extent is a
    ///         plus-or-minus bound and the swept angle is twice it. The hardpoint path on the same
    ///         DTO field tests <c>|yaw| &gt; Fire_Cone_Width / 2.0</c>, so THAT number is already a
    ///         full angle. Two conventions, one field - so the normalisation belongs here, where
    ///         both are known, rather than at a consumer that cannot tell which it was handed.
    ///     </para>
    ///     <para>
    ///         An authored zero survives as zero: a weapon that can only fire dead ahead is a real
    ///         thing and must not be confused with the default.
    ///     </para>
    /// </remarks>
    /// <param name="effective">The unit, with its variant chain resolved.</param>
    /// <param name="deployed">
    ///     The arc while deployed: <c>Deployed_Turret_Rotate_Extent_Degrees</c>, whose default is the same
    ///     360, and null for a unit that is never deployed (<see cref="CanDeploy" />).
    /// </param>
    private static float? UnitArcYawDegrees(EffectiveObject effective, bool deployed = false)
    {
        if (FiresForward(effective) || (deployed && !CanDeploy(effective))) return null;

        var tag = deployed ? "Deployed_Turret_Rotate_Extent_Degrees" : "Turret_Rotate_Extent_Degrees";
        return FullAngle(effective.FloatOf(tag) ?? DefaultTurretRotateExtentDegrees);
    }

    /// <summary>
    ///     Whether the weapon skips the arc check entirely, in which case it HAS no arc.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         the projectile-facing calculation returns the object's own facing and returns true
    ///         before the cone-of-fire test is reached, so authored extents are not widened or
    ///         narrowed - they are never read. Null then means "no arc concept", which is a
    ///         different statement from 360 ("unrestricted") and 0 ("dead ahead only").
    ///     </para>
    ///     <para>
    ///         The guard is not hypothetical: <c>Landbombingrununits.xml</c> ships
    ///         <c>Fires_Forward</c> alongside a 20/20 arc, with a comment saying the flag exists to
    ///         skip the check that reads it.
    ///     </para>
    /// </remarks>
    private static bool FiresForward(EffectiveObject effective)
    {
        return effective.BoolOf("Fires_Forward");
    }

    /// <summary>The vertical arc, as a full angle.</summary>
    /// <remarks>
    ///     <c>Turret_XY_Only</c> makes the cone-of-fire test skip the pitch test ENTIRELY rather
    ///     than flattening it, so the elevation is unbounded however the extent is authored.
    /// </remarks>
    /// <param name="effective">The unit, with its variant chain resolved.</param>
    /// <param name="deployed">As for <see cref="UnitArcYawDegrees" />.</param>
    private static float? UnitArcPitchDegrees(EffectiveObject effective, bool deployed = false)
    {
        if (FiresForward(effective) || (deployed && !CanDeploy(effective))) return null;

        // Tested before the pitch is read in the cone-of-fire test, so it drops the deployed test too.
        if (effective.BoolOf("Turret_XY_Only"))
            return FullTurnDegrees;

        var tag = deployed ? "Deployed_Turret_Elevate_Extent_Degrees" : "Turret_Elevate_Extent_Degrees";
        return FullAngle(effective.FloatOf(tag) ?? DefaultTurretElevateExtentDegrees);
    }

    /// <summary>
    ///     A plus-or-minus bound as the full angle it sweeps, saturating at a whole turn.
    /// </summary>
    /// <remarks>
    ///     The defaults double to 720 and 360; both mean "everything", and a full angle cannot
    ///     usefully exceed 360, so the clamp discards nothing.
    /// </remarks>
    private static float FullAngle(float halfAngleDegrees)
    {
        return Math.Min(halfAngleDegrees * 2f, FullTurnDegrees);
    }

    /// <summary>
    ///     A hardpoint's horizontal arc as a full angle - from its turret extent when it is one,
    ///     and from its fire cone when it is not.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         the weapon-aim test branches and the two arms are EXCLUSIVE:
    ///         <c>if (Is_Turret)</c> it tests <c>Turret_Rotate_Extent_Degrees &lt; |yaw|</c>, and
    ///         the fire cone is tested only in the <c>else</c>. A turret's cone is therefore never
    ///         read - all seven shipped turret hardpoints declare one anyway, and
    ///         <c>HP_Gargantuan_Small_Turret_Front_Left</c>'s 450-degree height has survived in both
    ///         games precisely because nothing looks at it.
    ///     </para>
    ///     <para>
    ///         The extent is a plus-or-minus bound the engine does not halve; the cone is a full
    ///         angle it does. Hence one doubled and the other passed through.
    ///     </para>
    /// </remarks>
    private static float? HardpointArcYawDegrees(EffectiveObject effective)
    {
        if (!effective.BoolOf("Is_Turret"))
            return effective.FloatOf("Fire_Cone_Width");

        return FullAngle(effective.FloatOf("Turret_Rotate_Extent_Degrees") ?? 0f);
    }

    /// <summary>The vertical arc, which a turret hardpoint simply does not have.</summary>
    /// <remarks>
    ///     The turret arm of that branch tests yaw and then returns - there is no elevation test on
    ///     the SHOT at all. <c>Turret_Elevate_Extent_Degrees</c> is read only by
    ///     the turret-angle calculation, which bounds where the BARREL may point. Reporting
    ///     the barrel's limit as the shot's would draw an arc the engine does not enforce.
    /// </remarks>
    private static float? HardpointArcPitchDegrees(EffectiveObject effective)
    {
        if (!effective.BoolOf("Is_Turret"))
            return effective.FloatOf("Fire_Cone_Height");

        return FullTurnDegrees;
    }

    /// <summary>Whether either behaviour list names <c>WEAPON</c>.</summary>
    private static bool HasWeaponBehavior(EffectiveObject effective)
    {
        return BehaviorTags
            .SelectMany(tag => (effective.ValueOf(tag) ?? string.Empty)
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .Any(token => token.Equals("WEAPON", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    ///     The turret pose for a unit weapon, or null when the unit names no turret bone.
    /// </summary>
    /// <remarks>
    ///     Separate from <see cref="Turret" />, which gates on a hardpoint's <c>Is_Turret</c> flag -
    ///     a tag the unit case does not have. Here the bone names are the evidence.
    /// </remarks>
    private static PreviewTurret? UnitTurret(EffectiveObject effective)
    {
        var turretBone = effective.ValueOf("Turret_Bone_Name");
        var barrelBone = effective.ValueOf("Barrel_Bone_Name");

        if (string.IsNullOrEmpty(turretBone) && string.IsNullOrEmpty(barrelBone))
            return null;

        // EFFECTIVE, like the hardpoint's - but off the UNIT's defaults, which are 360 and 180. The
        // deployed pair has the same defaults, and Adjust_Turret_Facing swaps it in
        // whenever the unit is deployed - so only a unit that can deploy gets one.
        var canDeploy = CanDeploy(effective);
        return new PreviewTurret(
            null,
            effective.FloatOf("Turret_Rotate_Extent_Degrees")
            ?? DefaultTurretRotateExtentDegrees,
            effective.FloatOf("Turret_Elevate_Extent_Degrees")
            ?? DefaultTurretElevateExtentDegrees,
            string.IsNullOrEmpty(turretBone) ? null : turretBone,
            string.IsNullOrEmpty(barrelBone) ? null : barrelBone,
            canDeploy
                ? effective.FloatOf("Deployed_Turret_Rotate_Extent_Degrees") ?? DefaultTurretRotateExtentDegrees
                : null,
            canDeploy
                ? effective.FloatOf("Deployed_Turret_Elevate_Extent_Degrees") ?? DefaultTurretElevateExtentDegrees
                : null);
    }

    /// <summary>
    ///     The hull's <c>MuzzleA_#</c> bones, in number order.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The A bones only. <c>MuzzleB</c> is a naming convention rather than something the engine
    ///         consumes for this case, and <c>MuzzleC</c> is a single bone on <c>Rv_bwing.alo</c> in
    ///         the whole corpus.
    ///     </para>
    ///     <para>
    ///         Two exclusions, both measured. A <c>*_flash</c> name is the flash GEOMETRY beside the
    ///         fire point, driven by the clip's own visibility tracks - firing from it would double
    ///         every shot. And a bone already named as the turret or barrel is not repeated:
    ///         <c>T2B_Tank</c> names <c>MuzzleA_00</c> as its <c>Barrel_Bone_Name</c>, so the two
    ///         conventions overlap on exactly that unit.
    ///     </para>
    /// </remarks>
    private static List<string> MuzzleBones(GameIndex index, string? hull, PreviewTurret? turret)
    {
        if (string.IsNullOrEmpty(hull) ||
            !index.ModelBones.TryGetValue(ModelBoneKey.From(hull), out var names))
            return [];

        var claimed = new[] { turret?.TurretBone, turret?.BarrelBone }
            .Where(b => !string.IsNullOrEmpty(b))
            .Select(b => b!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return
        [
            .. names
                .Where(n => n.StartsWith("MuzzleA_", StringComparison.OrdinalIgnoreCase))
                .Where(n => !n.Contains("flash", StringComparison.OrdinalIgnoreCase))
                .Where(n => !claimed.Contains(n))
                .OrderBy(MuzzleNumber)
                .ThenBy(n => n, StringComparer.OrdinalIgnoreCase)
        ];
    }

    /// <summary>The trailing number of a muzzle bone, or <see cref="int.MaxValue" /> when it has none.</summary>
    private static int MuzzleNumber(string boneName)
    {
        var underscore = boneName.LastIndexOf('_');

        return underscore >= 0 &&
               int.TryParse(boneName.AsSpan(underscore + 1), NumberStyles.Integer,
                   CultureInfo.InvariantCulture, out var number)
            ? number
            : int.MaxValue;
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
        var stages = Numbers<int>(effective.ValueOf("Land_Damage_Alternates"), int.TryParse);
        var thresholds = Numbers<float>(effective.ValueOf("Land_Damage_Thresholds"), float.TryParse);

        if (stages.Count == 0 || stages.Count != thresholds.Count)
            return [];

        return [.. stages.Select((stage, at) => new PreviewDamageBand(thresholds[at], stage))];
    }

    /// <summary>
    ///     Every entry of a comma-separated list that parses, in written order.
    /// </summary>
    /// <remarks>
    ///     A non-numeric entry is DROPPED rather than skipped-with-a-hole, which is the same rule
    ///     <see cref="DamageStages" /> uses. It also means a table carrying one is caught by the
    ///     length check above and discarded whole, which is the safe direction for a positional
    ///     pairing.
    /// </remarks>
    private static List<T> Numbers<T>(string? raw, ParseNumber<T> parse)
    {
        var found = new List<T>();
        if (string.IsNullOrWhiteSpace(raw))
            return found;

        foreach (var token in raw.Split(
                     ',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            if (parse(token, out var value))
                found.Add(value);

        return found;
    }

    /// <summary>The first entry of a comma-separated list, or null when there is none.</summary>
    private static string? FirstToken(string? value)
    {
        return (value ?? string.Empty)
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
    }

    /// <summary>
    ///     The hardpoint's weapon, or null when it names no fire bone.
    /// </summary>
    /// <remarks>
    ///     A shield generator or a docking bay is a hardpoint with no armament, so an absent weapon
    ///     is the normal case for roughly half of them, not a failure.
    /// </remarks>
    private PreviewWeapon? Weapon(GameIndex index, EffectiveObject effective, string hardpointId)
    {
        var bones = new[] { effective.ValueOf("Fire_Bone_A"), effective.ValueOf("Fire_Bone_B") }
            .Where(b => !string.IsNullOrEmpty(b))
            .Select(b => b!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (bones.Count == 0)
            return null;

        return new PreviewWeapon(
            $"hardpoint:{hardpointId}",
            PreviewWeaponSource.Hardpoint,
            hardpointId,
            effective.ValueOf("Type") ?? hardpointId,
            bones,
            effective.BoolOf("Randomize_Between_Fire_Bones")
                ? PreviewFirePointMode.RandomAlongLine
                : PreviewFirePointMode.CycleBones,
            // Fires_Forward lives on GameObjectType, never on a hardpoint.
            false,
            effective.ValueOf("Fire_Projectile_Type"),
            effective.FloatOf("Projectile_Damage"),
            effective.ValueOf("Damage_Type"),
            effective.FloatOf("Fire_Range_Distance"),
            effective.FloatOf("Fire_Min_Range_Distance"),
            HardpointArcYawDegrees(effective),
            HardpointArcPitchDegrees(effective),
            Inaccuracy(index, hardpointId),
            effective.IntOf("Fire_Pulse_Count"),
            effective.FloatOf("Fire_Pulse_Delay_Seconds"),
            effective.FloatOf("Fire_Min_Recharge_Seconds") ??
            effective.FloatOf("Fire_Max_Recharge_Seconds"),
            FireModes(effective),
            Turret(effective),
            effective.ValueOf("Fire_SFXEvent"));
    }

    /// <summary>
    ///     How far this weapon's shots stray, per target category.
    /// </summary>
    /// <remarks>
    ///     Through <see cref="RepeatedTagReader" /> rather than the effective object, exactly as the
    ///     reticle and armor tables are: the resolver COLLAPSES a repeated tag to one occurrence, so
    ///     an object declaring seven categories would answer with whichever survived. It was read as
    ///     a single <c>Number</c> before, which cannot parse <c>Fighter, 30.0</c> - so the field has
    ///     never once been non-null, on any of the 1017 rows the shipped tree declares.
    /// </remarks>
    private List<PreviewInaccuracy> Inaccuracy(GameIndex index, string objectId)
    {
        var rows = new List<PreviewInaccuracy>();

        foreach (var row in RepeatedTagReader.Rows(
                     index, tagSource, objectId, "Fire_Inaccuracy_Distance", 2))
        {
            // A row naming no category, or a distance that is not a number, is a broken row rather
            // than a zero - dropping it leaves the weapon with one fewer category, which is what the
            // engine would do with a value it cannot read.
            if (row[0].Length == 0
                || !float.TryParse(row[1], NumberStyles.Float, CultureInfo.InvariantCulture,
                    out var distance))
                continue;

            rows.Add(new PreviewInaccuracy(row[0], distance));
        }

        return rows;
    }

    /// <summary>The <c>Fire_When_*</c> gates this hardpoint sets.</summary>
    private static List<string> FireModes(EffectiveObject effective)
    {
        return [.. FireModeTags.Where(tag => effective.BoolOf(tag))];
    }

    // ── factions ──────────────────────────────────────────────────────────────

    /// <summary>
    ///     Every indexed faction's colours.
    /// </summary>
    /// <remarks>
    ///     All of them travel, not just the object's own. The preview lets an author try a unit in any
    ///     faction's colours and compare two side by side, and shipping the whole set makes that a
    ///     local switch rather than a round trip each time.
    /// </remarks>
    private List<PreviewFaction> Factions(GameIndex index, EffectiveObjectResolver resolver)
    {
        var factions = new List<PreviewFaction>();

        // Workspace first, then the baseline: a mod's own faction shadows a shipped one of the same
        // id, and DistinctBy keeps whichever it saw first.
        //
        // Each group in DECLARATION order, which is the order the palette is drawn in. It used to
        // come straight off the dictionaries, which have no order to give - so the swatches came
        // back arranged differently on every reload and the reader reached for the position they
        // used last time and got someone else's colour. Sorted within the groups rather than across
        // them, so the shadowing above still means what it says.
        var candidates = InXmlOrder(index.WorkspaceDefinitions.SelectMany(kv => kv.Value))
            .Concat(InXmlOrder(index.Baseline.Symbols.Values));

        // ReSharper disable once LoopCanBeConvertedToQuery - each faction is resolved and the
        // unresolvable skipped; a query would hide the resolve.
        foreach (var symbol in candidates
                     .Where(s => string.Equals(s.TypeName, FactionTypeName, StringComparison.OrdinalIgnoreCase))
                     .DistinctBy(s => s.Id, StringComparer.OrdinalIgnoreCase))
        {
            var effective = resolver.Resolve(symbol.Id);
            if (!effective.Found)
                continue;

            factions.Add(new PreviewFaction(
                effective.ObjectId,
                Colour(effective, "Color"),
                Colour(effective, "No_Colorization_Color"),
                Colour(effective, "Display_Font_Color")));
        }

        return factions;
    }

    /// <summary>
    ///     Symbols in the order their file declares them: by file, then by position within it.
    /// </summary>
    /// <remarks>
    ///     A symbol with no file to point at - one out of a MEG archive, or one whose origin was
    ///     never resolved - sorts after the rest and then by id, so it has SOME fixed place instead
    ///     of wherever the dictionary happened to put it. Being last is the honest answer: nothing
    ///     is known about where it was written.
    /// </remarks>
    private static IEnumerable<GameSymbol> InXmlOrder(IEnumerable<GameSymbol> symbols)
    {
        return symbols
            .OrderBy(s => s.Origin is FileOrigin ? 0 : 1)
            .ThenBy(s => (s.Origin as FileOrigin)?.Uri ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => (s.Origin as FileOrigin)?.Line ?? 0)
            .ThenBy(s => (s.Origin as FileOrigin)?.Column ?? 0)
            .ThenBy(s => s.Id, StringComparer.OrdinalIgnoreCase);
    }

    // ── tag reading ───────────────────────────────────────────────────────────

    /// <summary>
    ///     One category per entry, out of a mask that names several.
    /// </summary>
    /// <remarks>
    ///     <c>CategoryMask</c> is written <c>Vehicle | AntiInfantry | AntiVehicle</c>, so it is not
    ///     a value that can be looked up - it is a set that has to be tested against. Splitting it
    ///     server-side keeps the client from having to know the engine's punctuation.
    /// </remarks>
    private static List<string> CategoryTokens(string? mask)
    {
        if (string.IsNullOrWhiteSpace(mask))
            return [];

        return
        [
            .. mask
                .Split(['|', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(token => token.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
        ];
    }

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

    private static PreviewRgba? Colour(EffectiveObject effective, string tagName)
    {
        if (effective.ValueOf(tagName) is not { } raw)
            return null;

        var tokens = raw.Split([' ', '\t', ',', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 3)
            return null;

        var channels = new int[4];
        channels[3] = 255;

        for (var i = 0; i < Math.Min(4, tokens.Length); i++)
        {
            if (!int.TryParse(tokens[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
                return null;
            channels[i] = Math.Clamp(v, 0, 255);
        }

        return new PreviewRgba(channels[0], channels[1], channels[2], channels[3]);
    }

    private delegate bool ParseNumber<T>(string written, out T value);
}
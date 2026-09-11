// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Globalization;
using System.Runtime.CompilerServices;
using PG.StarWarsGame.LSP.Assets.Models;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Assets.Icons;
using PG.StarWarsGame.LSP.Core.Localisation;
using PG.StarWarsGame.LSP.Server.Abilities;
using PG.StarWarsGame.LSP.Server.Assets;
using PG.StarWarsGame.LSP.Xml.Util;
using PG.StarWarsGame.LSP.Xml.Validation;
using PG.StarWarsGame.LSP.Core.Diagnostics;

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
    IGameAssetResolver assets)
{
    private const string HardPointsTag = "HardPoints";

    /// <summary>
    ///     The animation-set overrides, land first.
    /// </summary>
    /// <remarks>
    ///     Only the land tag appears in the shipped data - about 47 uses, all infantry and indigenous -
    ///     but the space one is in the schema and in the engine's own parser, so a mod may well use it.
    /// </remarks>
    private static readonly string[] AnimationOverrideTags =
        ["Land_Model_Anim_Override_Name", "Space_Model_Anim_Override_Name"];
    private const string ModelToAttachTag = "Model_To_Attach";
    private const string AttachmentBoneTag = "Attachment_Bone";
    private const string FactionTypeName = "Faction";

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
        var path = PreviewModelReference.ModelPath(modelReference);
        var resolved = assets.Locate(path) is not null;

        var problems = new List<PreviewProblem>();
        if (!resolved)
            problems.Add(new PreviewProblem(DiagnosticIds.PreviewModelNotFound, "error",
                $"Model '{modelReference}' was not found. {assets.Tiers.Explain()}"));

        var kind = PreviewSceneKind.Model;
        if (resolved)
            switch (AloFile.Classify(assets.Read(path) ?? []))
            {
                case AloFileKind.Particle:
                    kind = PreviewSceneKind.Particle;
                    break;

                case AloFileKind.Unknown:
                    problems.Add(new PreviewProblem(DiagnosticIds.PreviewNotAModel, "error",
                        $"'{modelReference}' is not an Alamo model or particle system: Its root chunk id is "
                        + "neither. The file may be renamed or truncated."));
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
            Factions(new EffectiveObjectResolver(indexService.Current, schema, tagSource)),
            problems,
            assets.Tiers,
            null,
            particles,
            AnimationsFor(modelReference),
            resolved ? CamerasOf(modelReference, problems) : []);
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
                $"'{modelReference}' could not be read for its cameras: {e.Message}"));
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

        foreach (var key in candidates)
        {
            if (clip is null || !clip.MatchesModel(index.ModelBones[key]))
                continue;

            var scene = BuildForModel(key);
            return scene with
            {
                Subject = animation,
                Problems = [.. scene.Problems]
            };
        }

        var reason = candidates.Count == 0
            ? $"No model name prefixes '{animation}', so there is nothing to play it on."
            : $"'{animation}' does not match the skeleton of {string.Join(", ", candidates.Take(3))}. "
              + "An animation exported against a different revision of a model cannot be played on it.";

        return PreviewScene.NotFound(animation, reason, assets.Tiers);
    }

    /// <summary>A scene for a GameObject: its tactical model plus everything its XML mounts on it.</summary>
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
            return PreviewScene.NotFound(objectId, $"No object named '{objectId}' is indexed.", assets.Tiers);

        var problems = new List<PreviewProblem>();
        if (effective.Cyclic)
            problems.Add(new PreviewProblem(DiagnosticIds.PreviewInheritanceCycle, "error",
                $"'{objectId}' inherits in a cycle through '{effective.CycleObjectId}'; " +
                "the assembled scene may be incomplete."));

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
                $"'{effective.ObjectId}' declares no tactical model, so there is nothing to draw."));
        else
            parts.Add(new PreviewPart("hull", hull, null, null, PreviewPartOrigin.Hull, null,
                assets.Locate(PreviewModelReference.ModelPath(hull)) is not null));

        // Whether the hardpoints are a route to this object's death at all. Read once here rather
        // than per hardpoint: it is the owner's tag, not theirs.
        var diesWithHardpoints = EngineBoolean.IsTrueUnlessDenied(
            Tag(effective, "Should_Be_Destroyed_When_All_Hardpoints_Destroyed"));

        foreach (var hardpointId in HardpointIds(effective))
            AddHardpoint(resolver, hull, hardpointId, diesWithHardpoints,
                parts, hardpoints, weapons, problems);

        // The unit's own armament, for the 188 objects that carry a WEAPON behaviour instead of - or
        // as well as - hardpoints. Nine objects across the two trees have both.
        if (UnitWeapon(effective, hull, problems) is { } unitWeapon)
            weapons.Add(unitWeapon);

        var animationSource = ResolveAnimationOverride(effective, hull, problems);

        // Read once: the scene carries it and the check below compares against it.
        var stages = DamageStages(effective);
        ReportUntaggedStages(effective.ObjectId, hull, stages, problems);

        if (hull is not null && !parts[0].Resolved)
            problems.Add(new PreviewProblem(DiagnosticIds.PreviewModelNotFound, "error",
                $"Model '{hull}' was not found. {assets.Tiers.Explain()}"));

        // Every part, not just the hull: a turret carries its own muzzle flashes and engine glow.
        var particles = parts
            .Where(part => part.Resolved)
            .SelectMany(part => ParticlesOf(part, hardpoints, problems))
            .ToList();

        return new PreviewScene(PreviewSceneKind.Object, effective.ObjectId, parts, hardpoints,
            Factions(resolver), problems, assets.Tiers, animationSource, particles,
            // The clips are named after the model whose skeleton they were authored against, which
            // is the OVERRIDE when a unit borrows another model's set.
            AnimationsFor(animationSource ?? hull ?? string.Empty),
            // The HULL's cameras only. A turret carries its own skeleton and could in principle
            // declare one, but it was aimed at the turret, and the subject here is the whole unit.
            hull is null ? [] : CamerasOf(hull, problems),
            Tag(effective, "Type"),
            CategoryTokens(Tag(effective, "CategoryMask")),
            BreakoffProps(resolver, hardpoints, problems),
            weapons,
            Reticles(hardpoints, problems),
            Projectiles(resolver, weapons, problems),
            // NAMED from here on. The positional list is twenty arguments long and every optional
            // one is a list or a record, so inserting in the wrong slot type-checks against its
            // neighbour often enough to be dangerous - it did, once.
            Defence: Defence(effective, hardpoints),
            Abilities: Abilities(effective, particles,
                AnimationsFor(animationSource ?? hull ?? string.Empty), problems,
                icons, index.Localisation),
            DeathClones: DeathClones(resolver, effective, problems),
            SpinAway: SpinAway(effective),
            DeathExplosions: Tag(effective, "Death_Explosions"),
            ProjectileCatalog: ProjectileCatalog(),
            // What it wears when no faction colour applies, which is most of the time - its own
            // colour first, and whose fallback to use when it declares none.
            NoColorizationColor: Colour(effective, "No_Colorization_Color"),
            Affiliation: FirstToken(Tag(effective, "Affiliation")),
            DamageStages: stages,
            DamageTable: DamageTable(effective));
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
    private void ReportUntaggedStages(
        string objectId, string? hull, IReadOnlyList<int> stages, List<PreviewProblem> problems)
    {
        if (hull is null || stages.Count == 0)
            return;

        if (!indexService.Current.ModelBones.TryGetValue(ModelBoneKey.From(hull), out var names))
            return;

        var missing = ModelLevelTag.StagesNotTagged(stages, ModelLevelTag.AltLevelsIn(names));
        if (missing.Count == 0)
            return;

        var which = missing.Count == 1
            ? $"stage {missing[0]}"
            : $"stages {string.Join(", ", missing)}";

        problems.Add(new PreviewProblem(DiagnosticIds.PreviewDamageStageNotInModel, "warning",
            $"'{objectId}' declares damage {which}, which nothing in '{hull}' is tagged for. "
            + "The unit reaches that state and does not change."));
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
    private List<PreviewDeathClone> DeathClones(
        EffectiveObjectResolver resolver, EffectiveObject effective, List<PreviewProblem> problems)
    {
        // On the OBJECT, not on the row, so it is the same answer for every clone it names.
        var playsIdle = EngineBoolean.IsTrue(Tag(effective, "Should_Death_Clone_Play_Idle"));
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
                    $"Death clone '{row[1]}' is not defined in this project."));
                clones.Add(new PreviewDeathClone(Blank(row[0]), row[1], null, playsIdle));
                continue;
            }

            var model = Tag(clone, "Space_Model_Name") is { Length: > 0 } space
                ? space
                : Tag(clone, "Land_Model_Name") is { Length: > 0 } land
                    ? land
                    : null;

            // Its OWN model's clips and effects. Everything else on the wire describes the subject,
            // and a clone is a replacement for it rather than a piece of it.
            clones.Add(new PreviewDeathClone(Blank(row[0]), row[1], model, playsIdle,
                model is null ? [] : AnimationsFor(model),
                model is null ? [] : ProxiesOf(model, row[1], [], problems)));
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
    private static List<PreviewAbility> Abilities(
        EffectiveObject effective,
        IReadOnlyList<PreviewParticle> particles,
        IReadOnlyList<string> animations,
        List<PreviewProblem> problems,
        IconCatalog? icons = null,
        ILocalisationIndex? localisation = null)
    {
        var declared = UnitAbilityReader.Read(Fragment(effective, EncyclopediaTags.UnitAbilitiesData));

        // Deliberately NOT gated on `declared.Count > 0`. An object with proxies and no abilities at
        // all is exactly where an unbound effect is most likely to be a real mistake - and returning
        // early there is a hole a live server found.
        var bound = AbilityProxyPrefix.Bind(
            particles.Select(particle => particle.Bone), declared.Select(a => a.Type));

        foreach (var (ability, names) in bound.Unbound)
            problems.Add(new PreviewProblem(DiagnosticIds.PreviewUnboundAbilityEffect, "info",
                $"{string.Join(", ", names)} names {ability} by its prefix, but this object "
                + "declares no such ability - the effect is unbound."));

        var abilities = new List<PreviewAbility>();

        foreach (var ability in declared)
        {
            var row = new PreviewAbility(
                ability.Type,
                ability.Tag("GUI_Activated_Ability_Name"),
                ability.Tag("Owner_Attachment_Bone"),
                ability.Tag("Particle_Effect"),
                Parse(ability.Tag("Recharge_Seconds")),
                Parse(ability.Tag("Expiration_Seconds")),
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
                ability.Tag("Alternate_Description_Text")));
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

            if (Parse(parts[1].TrimEnd('f', 'F')) is { } factor)
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

    /// <summary>The verbatim XML of a tag, for the sub-object lists whose value is only whitespace.</summary>
    private static string? Fragment(EffectiveObject effective, string tagName)
    {
        return effective.Tags
            .FirstOrDefault(t => t.TagName.Equals(tagName, StringComparison.OrdinalIgnoreCase))
            ?.Fragment;
    }

    /// <summary>
    ///     A whole-number tag, as a count.
    /// </summary>
    /// <remarks>
    ///     Read through <see cref="Number" /> because the engine's integer tags are written as
    ///     floats often enough to matter - <c>Projectile_Blast_Area_Max_Victims</c> ships as
    ///     <c>1.0</c> in both of the two places it appears, and an integer parse would drop it.
    /// </remarks>
    private static int? Count(EffectiveObject effective, string tagName)
    {
        return Number(effective, tagName) is { } value ? (int)value : null;
    }

    private static float? Parse(string? value)
    {
        return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    /// <summary>
    ///     What a hit on this subject has to get through.
    /// </summary>
    /// <remarks>
    ///     The previewed object is the TARGET - the attacker is a weapon the reader builds - so this
    ///     is the defensive half of the damage model and the only half that comes from the file.
    ///     Only the two armor COLUMNS this target uses are sent; see <see cref="PreviewTargetDefence" />.
    /// </remarks>
    private PreviewTargetDefence Defence(
        EffectiveObject effective, IReadOnlyList<PreviewHardpoint> hardpoints)
    {
        var table = PreviewDamageTable.From(indexService.Current, tagSource);

        var armor = Tag(effective, "Armor_Type");
        var shieldArmor = Tag(effective, "Shield_Armor_Type");

        // Only the hardpoints that can actually die. An indestructible one can never be part of a
        // death, so counting its health would put a floor under the bar that nothing could remove.
        var destructible = hardpoints.Where(h => h.IsDestroyable).ToList();

        return new PreviewTargetDefence(
            IsShielded(effective),
            string.IsNullOrWhiteSpace(armor) ? null : armor.Trim(),
            string.IsNullOrWhiteSpace(shieldArmor) ? null : shieldArmor.Trim(),
            Number(effective, "Shield_Points"),
            Number(effective, "Tactical_Health"),
            Number(effective, "Energy_Capacity"),
            destructible.Count == 0 ? null : destructible.Sum(h => h.Health ?? 0f),
            EngineBoolean.IsTrueUnlessDenied(
                Tag(effective, "Should_Be_Destroyed_When_All_Hardpoints_Destroyed")),
            HullVsHardpointsConstraint(),
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
    private float HullVsHardpointsConstraint()
    {
        foreach (var value in RepeatedTagReader.Values(
                     indexService.Current, tagSource, EncyclopediaTags.GameConstantsId,
                     "Hull_Vs_Hard_Points_Health_Constraint"))
            if (Parse(value) is { } parsed)
                return parsed;

        return DefaultHullVsHardpointsConstraint;
    }

    /// <summary>What both shipped corpora write, and what the engine falls back to.</summary>
    private const float DefaultHullVsHardpointsConstraint = 0.2f;

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
            .SelectMany(tag => (Tag(effective, tag) ?? string.Empty)
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .Any(token => token.Equals("SHIELDED", StringComparison.OrdinalIgnoreCase));
    }

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

    private List<string> ProjectileCatalog()
    {
        if (CatalogCache.TryGetValue(indexService.Current, out var cached))
            return cached;

        var built = BuildProjectileCatalog();
        CatalogCache.AddOrUpdate(indexService.Current, built);

        return built;
    }

    private List<string> BuildProjectileCatalog()
    {
        var index = indexService.Current;

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
    private static List<PreviewProjectile> Projectiles(
        EffectiveObjectResolver resolver,
        IReadOnlyList<PreviewWeapon> weapons,
        List<PreviewProblem> problems)
    {
        return ProjectilesFor(
            resolver,
            weapons.Select(w => w.ProjectileType).Where(id => !string.IsNullOrEmpty(id))
                .Select(id => id!),
            problems);
    }

    /// <summary>Whether an object's own behaviour lists name <c>PROJECTILE</c>.</summary>
    /// <remarks>
    ///     Read from the raw tag sources rather than through the resolver: this runs over every
    ///     object in the workspace, and a variant chain per object would turn a dictionary scan into
    ///     real work. Inheritance is handled separately, by walking the symbols' own base ids.
    /// </remarks>
    private bool DeclaresProjectileBehavior(GameIndex index, string objectId)
    {
        foreach (var tagName in ShieldBehaviorTags)
            foreach (var value in RepeatedTagReader.Values(index, tagSource, objectId, tagName))
                foreach (var token in value.Split(
                             ',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                    if (token.Equals("PROJECTILE", StringComparison.OrdinalIgnoreCase))
                        return true;

        return false;
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
        List<PreviewProblem> problems)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var projectiles = new List<PreviewProjectile>();

        foreach (var id in ids.Where(id => seen.Add(id)))
        {
            var effective = resolver.Resolve(id);
            if (!effective.Found)
            {
                problems.Add(new PreviewProblem(DiagnosticIds.PreviewProjectileNotDefined, "warning",
                    $"Projectile '{id}' is not defined in this project."));
                continue;
            }

            var model = Tag(effective, "Space_Model_Name") is { Length: > 0 } space
                ? space
                : Tag(effective, "Land_Model_Name") is { Length: > 0 } land
                    ? land
                    : null;

            projectiles.Add(new PreviewProjectile(
                id,
                EngineBoolean.IsTrue(Tag(effective, "Projectile_Custom_Render"))
                    ? PreviewProjectileRender.CustomQuad
                    : PreviewProjectileRender.Model,
                model,
                Number(effective, "Projectile_Width"),
                Number(effective, "Projectile_Length"),
                Tag(effective, "Projectile_Texture_Slot") is { Length: > 0 } slot ? slot : null,
                Tag(effective, "Projectile_Laser_Color") is { Length: > 0 } colour ? colour : null,
                Number(effective, "Max_Speed"),
                Number(effective, "Projectile_Max_Flight_Distance"),
                Number(effective, "Max_Rate_Of_Turn"),
                Number(effective, "Projectile_Damage"),
                Tag(effective, "Damage_Type") is { Length: > 0 } dmg ? dmg : null,
                Tag(effective, "Projectile_Category") is { Length: > 0 } cat ? cat : null,
                EngineBoolean.IsTrue(Tag(effective, "Projectile_Does_Shield_Damage")),
                EngineBoolean.IsTrue(Tag(effective, "Projectile_Does_Energy_Damage")),
                EngineBoolean.IsTrue(Tag(effective, "Projectile_Does_Hitpoint_Damage")),
                Number(effective, "Projectile_Blast_Area_Damage"),
                Number(effective, "Projectile_Blast_Area_Range"),
                Count(effective, "Projectile_Blast_Area_Max_Victims"),
                EngineBoolean.IsTrue(Tag(effective, "Projectile_Blast_Area_Dropoff")),
                Count(effective, "Projectile_Blast_Area_Dropoff_Tiers"),
                Tag(effective, "Projectile_Object_Detonation_Particle") is { Length: > 0 } det ? det : null,
                Tag(effective, "Projectile_Absorbed_By_Shields_Particle") is { Length: > 0 } abs ? abs : null,
                Tag(effective, "Projectile_SFXEvent_Detonate") is { Length: > 0 } sfx ? sfx : null));
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
    private PreviewReticles? Reticles(
        IReadOnlyList<PreviewHardpoint> hardpoints, List<PreviewProblem> problems)
    {
        var targetable = hardpoints
            .Where(h => h.IsTargetable && !string.IsNullOrEmpty(h.Type))
            .Select(h => h.Type!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (targetable.Count == 0)
            return null;

        var map = PreviewReticleMap.From(indexService.Current, tagSource);
        var byType = new Dictionary<string, PreviewReticleStates>(StringComparer.OrdinalIgnoreCase);

        foreach (var type in targetable)
            if (map.ForType(type) is { } states)
                byType[type] = states;
            else
                problems.Add(new PreviewProblem(DiagnosticIds.PreviewNoReticleForType, "info",
                    $"GameConstants maps no targeting reticle for hardpoint type '{type}', so the " +
                    "game draws nothing over those hardpoints."));

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
    private List<PreviewBreakoffProp> BreakoffProps(
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
                     .Where(id => seen.Add(id)))
        {
            var effective = resolver.Resolve(id);
            if (!effective.Found)
            {
                problems.Add(new PreviewProblem(DiagnosticIds.PreviewBreakoffPropNotDefined, "warning",
                    $"Breakoff prop '{id}' is not defined in this project, so the hardpoint will " +
                    "vanish instead of breaking off."));
                props.Add(new PreviewBreakoffProp(id, null, false, null, null, null, null, null,
                    null, false));
                continue;
            }

            // A breakoff prop is space debris; a ground hardpoint names none, so the land tag is not
            // consulted here.
            var model = Tag(effective, "Space_Model_Name");

            var modelRef = string.IsNullOrEmpty(model) ? null : model;

            props.Add(new PreviewBreakoffProp(
                id,
                modelRef,
                true,
                Vector3Tag(effective, "Debris_Movement_Vector"),
                Vector3Tag(effective, "Debris_Facing_Rotate_Vector"),
                Number(effective, "Debris_Min_Lifetime_Seconds"),
                Number(effective, "Debris_Max_Lifetime_Seconds"),
                Tag(effective, "Debris_Attached_Particle") is { Length: > 0 } p ? p : null,
                Tag(effective, "Death_Explosions") is { Length: > 0 } d ? d : null,
                EngineBoolean.IsTrue(Tag(effective, "Remove_Upon_Death")),
                // Its OWN model's, like a death clone's - a burning piece of debris trails its own
                // fire, and `Debris_Attached_Particle` is a second, separate effect the XML names.
                modelRef is null ? [] : AnimationsFor(modelRef),
                modelRef is null ? [] : ProxiesOf(modelRef, id, [], problems)));
        }

        return props;
    }

    /// <summary>A comma-separated triple, or null when the tag is absent or malformed.</summary>
    private static PreviewVector3? Vector3Tag(EffectiveObject effective, string tagName)
    {
        var raw = Tag(effective, tagName);
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
    ///
    ///     Offered, not verified: each is checked against the skeleton when it is actually loaded,
    ///     and 50 of the shipped animations sit beside a model they do not fit. Listing one that
    ///     turns out not to match costs an entry in a picker; listing none at all - which is what
    ///     happened while nothing could enumerate them - reads as a model with no animations.
    /// </remarks>
    private IReadOnlyList<string> AnimationsFor(string modelReference)
    {
        var stem = Path.GetFileNameWithoutExtension(
            PreviewModelReference.Normalise(modelReference));

        if (stem.Length == 0)
            return [];

        var index = indexService.Current;

        // Every OTHER model whose name also starts this one's - the variants that would otherwise
        // have their clips taken. `Rv_gargantuan_dc` beside `Rv_gargantuan` is the shipped case.
        var longer = index.ModelBones.Keys
            .Select(Path.GetFileNameWithoutExtension)
            .Where(other => other is not null
                            && other.Length > stem.Length
                            && other.StartsWith(stem + "_", StringComparison.OrdinalIgnoreCase))
            .Select(other => other! + "_")
            .ToList();

        return
        [
            .. index.AssetFiles
                .GetByExtension(".ala")
                .Select(Path.GetFileName)
                .Where(name => name is not null
                    && name.StartsWith(stem + "_", StringComparison.OrdinalIgnoreCase)
                    // The LONGEST model name that prefixes a clip owns it, which is the rule
                    // `BuildForAnimation` already uses coming the other way. Without it a hull
                    // collects its own death clone's clips - `rv_gargantuan_` prefixes
                    // `rv_gargantuan_dc_die_00` - and since a clip binds by INDEX and carries
                    // VISIBILITY tracks, the clone's clip switched off whichever hull bones sat at
                    // the indices its own skeleton hides. Two of the Gargantuan's turrets were
                    // mounted on those bones and went with them.
                    //
                    // Harmless only while the strict name check dropped such a clip downstream.
                    // Relaxing that check to bind by index is what made this reachable.
                    && !longer.Any(other =>
                        name.StartsWith(other, StringComparison.OrdinalIgnoreCase)))
                .Select(name => name!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
        ];
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
            : hardpoints.Where(h => h.PartId == part.Id).ToList();

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
                $"'{modelRef}' could not be read for its particle effects: {e.Message}"));
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
    private string? ResolveAnimationOverride(
        EffectiveObject effective, string? hull, List<PreviewProblem> problems)
    {
        var override_ = AnimationOverrideTags
            .Select(tag => Tag(effective, tag))
            .FirstOrDefault(value => value is not null);

        if (override_ is null)
            return null;

        if (hull is null)
            return override_;

        var index = indexService.Current;
        var hullBones = index.ModelBones.GetValueOrDefault(ModelBoneKey.From(hull));
        var overrideBones = index.ModelBones.GetValueOrDefault(ModelBoneKey.From(override_));

        // Only compare when both are catalogued; an unknown model is already reported elsewhere.
        if (!hullBones.IsDefaultOrEmpty && !overrideBones.IsDefaultOrEmpty
            && !hullBones.SequenceEqual(overrideBones, StringComparer.OrdinalIgnoreCase))
            problems.Add(new PreviewProblem(DiagnosticIds.PreviewAnimationSkeletonMismatch, "info",
                $"'{effective.ObjectId}' takes its animations from '{override_}', whose skeleton "
                + $"differs from '{hull}' ({overrideBones.Length} bones against "
                + $"{hullBones.Length}). The clips play, bound by bone index - where the two "
                + "skeletons disagree, a clip drives whatever bone sits at that index."));

        return override_;
    }

    // ── hardpoints ────────────────────────────────────────────────────────────

    private static IEnumerable<string> HardpointIds(EffectiveObject effective)
    {
        return Tag(effective, HardPointsTag) is { } raw
            ? XmlUtility.SplitList(raw)
            : [];
    }

    private void AddHardpoint(
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
                $"Hardpoint '{hardpointId}' is mounted but not defined in this project.", hardpointId));
            return;
        }

        var attachBone = Tag(effective, AttachmentBoneTag);
        var model = Tag(effective, ModelToAttachTag);
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
                    $"Hardpoint '{hardpointId}' attaches model '{model}', which was not found.",
                    hardpointId));
        }

        var isTargetable = EngineBoolean.IsTrue(Tag(effective, "Is_Targetable"));

        // A hardpoint with no attachment bone goes to the SCREEN root, not to the object's root -
        // told by the user, not measured here. That is bad placement for a hardpoint nobody shoots
        // at, and fatal for one they do: it cannot be hit, and a unit dies only once every
        // targetable hardpoint is destroyed, so the whole unit becomes indestructible.
        //
        // Two findings rather than one, because they differ in what the author has to do about
        // them and in what suppressing them would cost.
        if (string.IsNullOrEmpty(attachBone))
            problems.Add(isTargetable
                ? new PreviewProblem(DiagnosticIds.PreviewTargetableHardpointNoBone, "error",
                    $"Hardpoint '{hardpointId}' is targetable but names no {AttachmentBoneTag}, so "
                    + "the engine attaches it to the screen root. It can never be hit, and the unit "
                    + "can never be destroyed.",
                    hardpointId)
                : new PreviewProblem(DiagnosticIds.PreviewHardpointNoBone, "warning",
                    $"Hardpoint '{hardpointId}' names no {AttachmentBoneTag}, so the engine "
                    + "attaches it to the screen root rather than to the model.",
                    hardpointId));
        else if (hull is not null &&
                 !HardpointBoneModelResolver.ModelHasBone(indexService.Current, hull, attachBone))
            problems.Add(new PreviewProblem(DiagnosticIds.PreviewHardpointBoneMissing, "warning",
                $"Hardpoint '{hardpointId}' attaches to bone '{attachBone}', which model '{hull}' " +
                "does not have.", hardpointId));

        // Can a shot ever REACH this hardpoint? Damage is routed by the collision mesh a projectile
        // struck, matched with _stricmp against Collision_Mesh - exact but case-insensitive - so a
        // value naming nothing behaves exactly like an absent one: every shot meant for it lands on
        // the hull, the hardpoint never dies, and the all-destroyed branch can never complete.
        //
        // Against the union of BONES and mesh names, because vanilla points Collision_Mesh at the
        // hardpoint's own Attachment_Bone 28 times across the two trees. The hardpoint's attached
        // model counts as well as the hull's: 29 of the Executor's 30 meshes are in both, and one is
        // in the attached model only.
        //
        // Silent where the object does not die with its hardpoints - the palace keeps its
        // generators as destructible scenery, so one that cannot be shot takes nothing away.
        if (EngineBoolean.IsTrue(Tag(effective, "Is_Destroyable"))
            && diesWithHardpoints)
        {
            var collisionMesh = Tag(effective, "Collision_Mesh");
            var reachable = !string.IsNullOrWhiteSpace(collisionMesh)
                            && (HardpointBoneModelResolver.ModelHasBone(
                                    indexService.Current, hull, collisionMesh)
                                || HardpointBoneModelResolver.ModelHasBone(
                                    indexService.Current, model, collisionMesh));

            if (!reachable)
                problems.Add(new PreviewProblem(DiagnosticIds.PreviewHardpointUnreachable, "error",
                    string.IsNullOrWhiteSpace(collisionMesh)
                        ? $"Hardpoint '{hardpointId}' is destroyable but names no Collision_Mesh, so "
                          + "no shot can reach it. The unit can never be finished through its "
                          + "hardpoints."
                        : $"Hardpoint '{hardpointId}' names Collision_Mesh '{collisionMesh}', which "
                          + "is neither a mesh nor a bone of its model. No shot can reach it, and "
                          + "the unit can never be finished through its hardpoints.",
                    hardpointId));
        }

        var tooltipKey = Tag(effective, "Tooltip_Text");

        hardpoints.Add(new PreviewHardpoint(
            hardpointId,
            partId,
            Tag(effective, "Type"),
            attachBone,
            EngineBoolean.IsTrue(Tag(effective, "Is_Destroyable")),
            isTargetable,
            Number(effective, "Health"),
            Tag(effective, "Damage_Particles"),
            Tag(effective, "Damage_Decal"),
            Tag(effective, "Collision_Mesh"),
            Tag(effective, "Engine_Particles"),
            Tag(effective, "Death_Explosion_Particles"),
            Tag(effective, "Death_Breakoff_Prop"),
            EngineBoolean.IsTrueUnlessDenied(Tag(effective, "Engine_Death_Hide_Engine_Particles")),
            tooltipKey,
            // Resolved here rather than left to the client, which has no text index at all. The
            // localisation index matches case-insensitively, and the shipped files are not
            // consistent about the case they write - so the preview must not be stricter.
            tooltipKey is null
                ? null
                : indexService.Current.Localisation.GetValue(tooltipKey),
            Turret(effective)));

        if (Weapon(effective, hardpointId) is { } weapon)
            weapons.Add(weapon);
    }

    /// <summary>The turret pose, or null when the hardpoint is not one.</summary>
    private static PreviewTurret? Turret(EffectiveObject effective)
    {
        if (!EngineBoolean.IsTrue(Tag(effective, "Is_Turret")))
            return null;

        return new PreviewTurret(
            Number(effective, "Turret_Rest_Angle"),
            Number(effective, "Turret_Rotate_Extent_Degrees"),
            Number(effective, "Turret_Elevate_Extent_Degrees"),
            Tag(effective, "Turret_Bone_Name"),
            Tag(effective, "Barrel_Bone_Name"));
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
    private PreviewWeapon? UnitWeapon(
        EffectiveObject effective, string? hull, List<PreviewProblem> problems)
    {
        if (!HasWeaponBehavior(effective))
            return null;

        var turret = UnitTurret(effective);
        var bones = MuzzleBones(hull, turret);

        if (bones.Count == 0)
        {
            problems.Add(new PreviewProblem(DiagnosticIds.PreviewNoMuzzleBones, "warning",
                $"'{effective.ObjectId}' has a WEAPON behaviour but its model declares no " +
                "MuzzleA bones, so the engine has no muzzle to fire from."));
            return null;
        }

        return new PreviewWeapon(
            "weapon:A",
            PreviewWeaponSource.Unit,
            null,
            "Muzzle A",
            bones,
            // Randomize_Between_Fire_Bones is a HardPoint parameter and is not on GameObjectType at
            // all, so this case has no other branch.
            PreviewFirePointMode.CycleBones,
            EngineBoolean.IsTrue(Tag(effective, "Fires_Forward")),
            // A list, but only 2 of 95 shipped objects name more than one.
            FirstToken(Tag(effective, "Projectile_Types")),
            Number(effective, "Damage"),
            Tag(effective, "Damage_Type") is { Length: > 0 } dmg ? dmg : null,
            Number(effective, "Targeting_Max_Attack_Distance"),
            null,
            Number(effective, "Turret_Rotate_Extent_Degrees"),
            Number(effective, "Turret_Elevate_Extent_Degrees"),
            Inaccuracy(effective.ObjectId),
            Integer(effective, "Projectile_Fire_Pulse_Count"),
            Number(effective, "Projectile_Fire_Pulse_Delay_Seconds"),
            Number(effective, "Projectile_Fire_Recharge_Seconds"),
            [],
            turret,
            null);
    }

    /// <summary>Whether either behaviour list names <c>WEAPON</c>.</summary>
    private static bool HasWeaponBehavior(EffectiveObject effective)
    {
        return BehaviorTags
            .SelectMany(tag => (Tag(effective, tag) ?? string.Empty)
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .Any(token => token.Equals("WEAPON", StringComparison.OrdinalIgnoreCase));
    }

    private static readonly string[] BehaviorTags = ["SpaceBehavior", "LandBehavior"];

    /// <summary>
    ///     The turret pose for a unit weapon, or null when the unit names no turret bone.
    /// </summary>
    /// <remarks>
    ///     Separate from <see cref="Turret" />, which gates on a hardpoint's <c>Is_Turret</c> flag -
    ///     a tag the unit case does not have. Here the bone names are the evidence.
    /// </remarks>
    private static PreviewTurret? UnitTurret(EffectiveObject effective)
    {
        var turretBone = Tag(effective, "Turret_Bone_Name");
        var barrelBone = Tag(effective, "Barrel_Bone_Name");

        if (string.IsNullOrEmpty(turretBone) && string.IsNullOrEmpty(barrelBone))
            return null;

        return new PreviewTurret(
            null,
            Number(effective, "Turret_Rotate_Extent_Degrees"),
            Number(effective, "Turret_Elevate_Extent_Degrees"),
            string.IsNullOrEmpty(turretBone) ? null : turretBone,
            string.IsNullOrEmpty(barrelBone) ? null : barrelBone);
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
    private IReadOnlyList<string> MuzzleBones(string? hull, PreviewTurret? turret)
    {
        if (string.IsNullOrEmpty(hull) ||
            !indexService.Current.ModelBones.TryGetValue(ModelBoneKey.From(hull), out var names))
            return [];

        var claimed = new[] { turret?.TurretBone, turret?.BarrelBone }
            .Where(b => !string.IsNullOrEmpty(b))
            .Select(b => b!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return names
            .Where(n => n.StartsWith("MuzzleA_", StringComparison.OrdinalIgnoreCase))
            .Where(n => !n.Contains("flash", StringComparison.OrdinalIgnoreCase))
            .Where(n => !claimed.Contains(n))
            .OrderBy(MuzzleNumber)
            .ThenBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
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
        var raw = Tag(effective, "Land_Damage_Alternates");
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
        var stages = Numbers<int>(Tag(effective, "Land_Damage_Alternates"), int.TryParse);
        var thresholds = Numbers<float>(Tag(effective, "Land_Damage_Thresholds"), float.TryParse);

        if (stages.Count == 0 || stages.Count != thresholds.Count)
            return [];

        return [.. stages.Select((stage, at) => new PreviewDamageBand(thresholds[at], stage))];
    }

    private delegate bool ParseNumber<T>(string written, out T value);

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
    private PreviewWeapon? Weapon(EffectiveObject effective, string hardpointId)
    {
        var bones = new[] { Tag(effective, "Fire_Bone_A"), Tag(effective, "Fire_Bone_B") }
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
            Tag(effective, "Type") is { Length: > 0 } type ? type : hardpointId,
            bones,
            EngineBoolean.IsTrue(Tag(effective, "Randomize_Between_Fire_Bones"))
                ? PreviewFirePointMode.RandomAlongLine
                : PreviewFirePointMode.CycleBones,
            // Fires_Forward lives on GameObjectType, never on a hardpoint.
            false,
            Tag(effective, "Fire_Projectile_Type") is { Length: > 0 } proj ? proj : null,
            Number(effective, "Projectile_Damage"),
            Tag(effective, "Damage_Type") is { Length: > 0 } dmg ? dmg : null,
            Number(effective, "Fire_Range_Distance"),
            Number(effective, "Fire_Min_Range_Distance"),
            Number(effective, "Fire_Cone_Width"),
            Number(effective, "Fire_Cone_Height"),
            Inaccuracy(hardpointId),
            Integer(effective, "Fire_Pulse_Count"),
            Number(effective, "Fire_Pulse_Delay_Seconds"),
            Number(effective, "Fire_Min_Recharge_Seconds") ??
            Number(effective, "Fire_Max_Recharge_Seconds"),
            FireModes(effective),
            Turret(effective),
            Tag(effective, "Fire_SFXEvent") is { Length: > 0 } sfx ? sfx : null);
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
    private IReadOnlyList<PreviewInaccuracy> Inaccuracy(string objectId)
    {
        var rows = new List<PreviewInaccuracy>();

        foreach (var row in RepeatedTagReader.Rows(
                     indexService.Current, tagSource, objectId, "Fire_Inaccuracy_Distance", 2))
        {
            // A row naming no category, or a distance that is not a number, is a broken row rather
            // than a zero - dropping it leaves the weapon with one fewer category, which is what the
            // engine would do with a value it cannot read.
            if (row[0].Length == 0
                || !float.TryParse(row[1], NumberStyles.Float, CultureInfo.InvariantCulture,
                    out var distance))
            {
                continue;
            }

            rows.Add(new PreviewInaccuracy(row[0], distance));
        }

        return rows;
    }

    /// <summary>The <c>Fire_When_*</c> gates this hardpoint sets.</summary>
    private static IReadOnlyList<string> FireModes(EffectiveObject effective)
    {
        return FireModeTags
            .Where(tag => EngineBoolean.IsTrue(Tag(effective, tag)))
            .ToList();
    }

    private static readonly string[] FireModeTags =
    [
        "Fire_When_Deployed", "Fire_When_Undeployed", "Fire_When_In_Rocket_Attack_Mode",
        "Fire_When_In_Normal_Attack_Mode", "Fire_When_In_Defend_Mode"
    ];

    private static int? Integer(EffectiveObject effective, string tagName)
    {
        return Tag(effective, tagName) is { } raw &&
               int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
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
    private IReadOnlyList<PreviewFaction> Factions(EffectiveObjectResolver resolver)
    {
        var factions = new List<PreviewFaction>();

        var index = indexService.Current;

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
    private static IReadOnlyList<string> CategoryTokens(string? mask)
    {
        if (string.IsNullOrWhiteSpace(mask))
            return [];

        return mask
            .Split(['|', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => token.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
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
        if (!IsYes(Tag(effective, "Spin_Away_On_Death")))
            return null;

        return new PreviewSpinAway(
            // 2.0 where the file is silent: 31 of the 34 write exactly that, so it is the corpus's
            // own answer rather than a number picked to fill a gap.
            Float(Tag(effective, "Spin_Away_On_Death_Time")) ?? 2.0f,
            Float(Tag(effective, "Spin_Away_On_Death_Chance")) ?? 0f,
            Tag(effective, "Spin_Away_On_Death_Explosion"),
            Float(Tag(effective, "Max_Speed")) ?? 0f);
    }

    /// <summary>A boolean as the files write it. 34 of 34 spell this one <c>Yes</c>.</summary>
    private static bool IsYes(string? value)
    {
        return value is not null
            && (value.Equals("yes", StringComparison.OrdinalIgnoreCase)
                || value.Equals("true", StringComparison.OrdinalIgnoreCase)
                || value.Equals("1", StringComparison.Ordinal));
    }

    /// <summary>
    ///     A float as the files write it, <c>f</c> suffix and all.
    /// </summary>
    /// <remarks>
    ///     The suffix is not a curiosity: 31 shipped objects write <c>2.0f</c> and 3 write
    ///     <c>1.0f</c>, so a plain parse drops every spin time in the game to its default. Invariant
    ///     culture, because the files are written with a point whatever the reader's locale is.
    /// </remarks>
    private static float? Float(string? value)
    {
        var text = value?.Trim().TrimEnd('f', 'F');

        return string.IsNullOrEmpty(text)
            ? null
            : float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : null;
    }

    private static string? Tag(EffectiveObject effective, string tagName)
    {
        var value = effective.Tags
            .FirstOrDefault(t => string.Equals(t.TagName, tagName, StringComparison.OrdinalIgnoreCase))
            ?.Value.Trim();

        return string.IsNullOrEmpty(value) ? null : value;
    }

    /// <summary>
    ///     A numeric tag, or null when absent or unparsable.
    /// </summary>
    /// <remarks>
    ///     Null rather than a defaulted zero. A fire cone of 0 degrees and a hardpoint that declares no
    ///     cone are different things, and drawing the first for the second would be a confident lie.
    /// </remarks>
    private static float? Number(EffectiveObject effective, string tagName)
    {
        return Tag(effective, tagName) is { } raw &&
               float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static PreviewRgba? Colour(EffectiveObject effective, string tagName)
    {
        if (Tag(effective, tagName) is not { } raw)
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

}

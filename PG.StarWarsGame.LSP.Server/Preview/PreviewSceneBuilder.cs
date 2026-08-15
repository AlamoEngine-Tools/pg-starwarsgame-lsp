// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Globalization;
using PG.StarWarsGame.LSP.Assets.Models;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
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
            problems.Add(new PreviewProblem("error",
                $"Model '{modelReference}' was not found. {assets.Tiers.Explain()}"));

        var kind = PreviewSceneKind.Model;
        if (resolved)
            switch (AloFile.Classify(assets.Read(path) ?? []))
            {
                case AloFileKind.Particle:
                    kind = PreviewSceneKind.Particle;
                    break;

                case AloFileKind.Unknown:
                    problems.Add(new PreviewProblem("error",
                        $"'{modelReference}' is not an Alamo model or particle system - its first "
                        + "chunk is neither. A renamed or truncated file looks exactly like this."));
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
            problems.Add(new PreviewProblem("warning",
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
    public PreviewScene BuildForObject(string objectId)
    {
        var index = indexService.Current;
        var resolver = new EffectiveObjectResolver(index, schema, tagSource);
        var effective = resolver.Resolve(objectId);

        if (!effective.Found)
            return PreviewScene.NotFound(objectId, $"No object named '{objectId}' is indexed.", assets.Tiers);

        var problems = new List<PreviewProblem>();
        if (effective.Cyclic)
            problems.Add(new PreviewProblem("error",
                $"'{objectId}' inherits in a cycle through '{effective.CycleObjectId}'; " +
                "the assembled scene may be incomplete."));

        var bones = new HardpointBoneModelResolver(index, schema, tagSource);
        var parts = new List<PreviewPart>();
        var hardpoints = new List<PreviewHardpoint>();

        // DeclaredModels is variant-resolved and already restricted to the TACTICAL models - a
        // starbase's low-detail galactic mesh legitimately lacks the hardpoint bones, so including it
        // would produce a scene full of phantom problems.
        var hull = bones.DeclaredModels(effective.ObjectId).FirstOrDefault(m => !string.IsNullOrEmpty(m));

        if (hull is null)
            problems.Add(new PreviewProblem("error",
                $"'{effective.ObjectId}' declares no tactical model, so there is nothing to draw."));
        else
            parts.Add(new PreviewPart("hull", hull, null, null, PreviewPartOrigin.Hull, null,
                assets.Locate(PreviewModelReference.ModelPath(hull)) is not null));

        foreach (var hardpointId in HardpointIds(effective))
            AddHardpoint(resolver, hull, hardpointId, parts, hardpoints, problems);

        var animationSource = ResolveAnimationOverride(effective, hull, problems);

        if (hull is not null && !parts[0].Resolved)
            problems.Add(new PreviewProblem("error",
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
            CategoryTokens(Tag(effective, "CategoryMask")));
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

        return
        [
            .. indexService.Current.AssetFiles
                .GetByExtension(".ala")
                .Select(Path.GetFileName)
                .Where(name => name is not null
                    && name.StartsWith(stem + "_", StringComparison.OrdinalIgnoreCase))
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
        var bytes = assets.Read(PreviewModelReference.ModelPath(part.ModelRef));
        if (bytes is null || AloFile.Classify(bytes) != AloFileKind.Model)
            return [];

        try
        {
            var model = AloModelReader.Read(bytes, AloReadOptions.SkipGeometry);

            // Only the hull's own hardpoints can claim its proxies; a turret's smoke is its own.
            var owning = part.Id == "hull"
                ? hardpoints
                : hardpoints.Where(h => h.PartId == part.Id).ToList();

            return PreviewParticleResolver.Resolve(part.Id, model.Bones, model.Proxies, owning);
        }
        catch (AloFormatException e)
        {
            // The geometry request will fail the same way and say so; this only costs the effects.
            problems.Add(new PreviewProblem("warning",
                $"'{part.ModelRef}' could not be read for its particle effects: {e.Message}"));
            return [];
        }
    }

    /// <summary>
    ///     The model whose animations this object actually plays, or null when it uses its own.
    /// </summary>
    /// <remarks>
    ///     The override borrows another model's animation set, which the engine can only do because
    ///     the skeletons match - true of every one of the 20 shipped land overrides. A mismatch is
    ///     therefore a real authoring error and is reported: the game would play the wrong bones, and
    ///     nothing else in the toolchain checks it.
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
            problems.Add(new PreviewProblem("warning",
                $"'{effective.ObjectId}' takes its animations from '{override_}', but that model's "
                + $"skeleton differs from '{hull}' ({overrideBones.Length} bones against "
                + $"{hullBones.Length}). The override only works on an identical skeleton."));

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
        List<PreviewPart> parts,
        List<PreviewHardpoint> hardpoints,
        List<PreviewProblem> problems)
    {
        var effective = resolver.Resolve(hardpointId);
        if (!effective.Found)
        {
            problems.Add(new PreviewProblem("warning",
                $"Hardpoint '{hardpointId}' is mounted but not defined anywhere.", hardpointId));
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
                problems.Add(new PreviewProblem("warning",
                    $"Hardpoint '{hardpointId}' attaches model '{model}', which was not found.",
                    hardpointId));
        }

        if (string.IsNullOrEmpty(attachBone))
            problems.Add(new PreviewProblem("warning",
                $"Hardpoint '{hardpointId}' names no {AttachmentBoneTag}, so it sits at the hull's origin.",
                hardpointId));
        else if (hull is not null &&
                 !HardpointBoneModelResolver.ModelHasBone(indexService.Current, hull, attachBone))
            problems.Add(new PreviewProblem("warning",
                $"Hardpoint '{hardpointId}' attaches to bone '{attachBone}', which model '{hull}' " +
                "does not have.", hardpointId));

        hardpoints.Add(new PreviewHardpoint(
            hardpointId,
            partId,
            Tag(effective, "Type"),
            attachBone,
            EngineBoolean.IsTrue(Tag(effective, "Is_Destroyable")),
            Number(effective, "Health"),
            Tag(effective, "Damage_Particles"),
            Tag(effective, "Damage_Decal"),
            Tag(effective, "Collision_Mesh"),
            Tag(effective, "Engine_Particles"),
            Tag(effective, "Death_Explosion_Particles"),
            Tag(effective, "Death_Breakoff_Prop"),
            EngineBoolean.IsTrue(Tag(effective, "Engine_Death_Hide_Engine_Particles")),
            Tag(effective, "Tooltip_Text"),
            Turret(effective),
            Fire(effective)));
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

    /// <summary>The firing arc, or null when the hardpoint names no fire bone.</summary>
    private static PreviewFireArc? Fire(EffectiveObject effective)
    {
        var bones = new[] { Tag(effective, "Fire_Bone_A"), Tag(effective, "Fire_Bone_B") }
            .Where(b => !string.IsNullOrEmpty(b))
            .Select(b => b!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (bones.Count == 0)
            return null;

        return new PreviewFireArc(
            bones,
            Number(effective, "Fire_Cone_Width"),
            Number(effective, "Fire_Cone_Height"),
            Number(effective, "Fire_Range_Distance"));
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

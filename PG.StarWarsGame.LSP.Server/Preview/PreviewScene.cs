// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using JetBrains.Annotations;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Server.Assets;

namespace PG.StarWarsGame.LSP.Server.Preview;

/// <summary>What the scene was built from.</summary>
public enum PreviewSceneKind
{
    /// <summary>A single <c>.alo</c> file, opened directly.</summary>
    Model,

    /// <summary>A GameObject, assembled from its model and everything its XML mounts on it.</summary>
    Object,

    /// <summary>
    ///     A particle system, opened directly.
    /// </summary>
    /// <remarks>
    ///     Shares the <c>.alo</c> extension with models, so the client cannot tell from the file name
    ///     which of the two it is looking at - it has to be told, because the two take entirely
    ///     different endpoints from here.
    /// </remarks>
    Particle
}

/// <summary>Why a part is in the scene.</summary>
public enum PreviewPartOrigin
{
    /// <summary>The object's own tactical model.</summary>
    Hull,

    /// <summary>A hardpoint's <c>Model_To_Attach</c>.</summary>
    Hardpoint
}

/// <summary>Four 0-255 channels, alpha last, exactly as the game writes them.</summary>
[PublicAPI]
public sealed record PreviewRgba(int R, int G, int B, int A);

/// <summary>
///     One model instance in the scene.
/// </summary>
/// <param name="ModelRef">
///     The model name as the XML writes it. Left unresolved on purpose - the client asks for the GLB
///     by this name, and the resolver decides which layer supplies it.
/// </param>
/// <param name="AttachToPartId">The part this is attached to, or null for the root.</param>
/// <param name="AttachBone">The bone on that part, or null to sit at its origin.</param>
/// <param name="Resolved">
///     Whether the model file was found. A part is emitted either way: a hardpoint whose model is
///     missing is a fact the author needs to see, not something to quietly drop.
/// </param>
public sealed record PreviewPart(
    string Id,
    string ModelRef,
    string? AttachToPartId,
    string? AttachBone,
    PreviewPartOrigin Origin,
    string? HardpointId,
    bool Resolved);

/// <summary>Something the author should see about this scene.</summary>
/// <param name="DiagnosticId">
///     Which kind of finding this is. Required rather than optional, and first rather than last, so
///     a new preview finding cannot be added without one - which is exactly how every one of these
///     came to have no id at all.
/// </param>
/// <param name="Severity"><c>error</c>, <c>warning</c> or <c>info</c>.</param>
public sealed record PreviewProblem(
    DiagnosticId DiagnosticId,
    string Severity,
    string Message,
    string? HardpointId = null);

/// <summary>
///     Where a previewed model lives on disk, as far as an external tool is concerned.
/// </summary>
/// <param name="Path">
///     The loose file the name resolved to, highest layer first - or null when it exists only
///     inside a MEG archive, where no tool can open it.
/// </param>
/// <param name="Packed">True when the model was found only inside a MEG archive.</param>
[PublicAPI]
public sealed record PreviewSourceFile(string? Path, bool Packed);

/// <summary>
///     Everything needed to draw a preview, with no binary payload.
/// </summary>
/// <remarks>
///     The client walks this, fetches each distinct model once, and drives every stateful behaviour -
///     ALT/LOD, hardpoint destruction, faction tint, particle toggles - locally. Nothing round-trips
///     to the server when a slider moves.
/// </remarks>
/// <param name="AnimationSource">
///     The model whose animation set applies, when it is not the hull's own.
///     <para>
///         <c>Land_Model_Anim_Override_Name</c> and its space counterpart hand a unit another model's
///         animations. That only works because the two skeletons are identical - verified across all
///         20 shipped land overrides, every one a bone-for-bone match - so the clips are named after
///         the OVERRIDE model, not the hull. Without this the preview would offer a unit no animations
///         at all while the game plays a full set.
///     </para>
/// </param>
[PublicAPI]
public sealed record PreviewScene(
    PreviewSceneKind Kind,
    string Subject,
    IReadOnlyList<PreviewPart> Parts,
    IReadOnlyList<PreviewHardpoint> Hardpoints,
    IReadOnlyList<PreviewFaction> Factions,
    IReadOnlyList<PreviewProblem> Problems,
    GameAssetTiers Tiers,
    string? AnimationSource = null,
    IReadOnlyList<PreviewParticle>? Particles = null,
    // The animation files that belong to this subject's model, by filename.
    //
    // Enumerated rather than requested: previewing a MODEL offered no clips at all, because
    // nothing could ask which existed - the client sent an empty list and the GLB was baked
    // with none. Each is still verified against the skeleton when it is loaded, since 50
    // shipped animations sit beside a model they were not authored against.
    IReadOnlyList<string>? Animations = null,
    // The cameras the subject's own model carries, in MODEL space.
    //
    // Empty for the 87% of models that declare none, which the client reports rather than
    // hides - a control with nothing to act on is disabled, not absent.
    IReadOnlyList<PreviewCamera>? Cameras = null,
    // The subject's Type tag, or null when previewing a bare model.
    //
    // Carried so a camera preset can be BOUND to what a subject is rather than to its name.
    string? ObjectType = null,
    // The subject's category tokens, split out of its CategoryMask.
    //
    // Split here rather than sent whole because the mask is MULTI-VALUED -
    // Vehicle | AntiInfantry | AntiVehicle - which is exactly why it cannot be used as a
    // plain lookup key. One token per entry is the form a match rule can actually test.
    IReadOnlyList<string>? Categories = null,
    // The wreckage this subject's hardpoints shed, one entry per distinct prop.
    IReadOnlyList<PreviewBreakoffProp>? BreakoffProps = null,
    // Every weapon on the subject, wherever it is declared. See PreviewWeapon.
    IReadOnlyList<PreviewWeapon>? Weapons = null,
    // The targeting reticles for this subject's hardpoint types, or null for a bare model.
    PreviewReticles? Reticles = null,
    // The projectiles this subject's weapons name, one entry per distinct projectile.
    IReadOnlyList<PreviewProjectile>? Projectiles = null,
    // What a hit on this subject has to get through, or null for a bare model.
    PreviewTargetDefence? Defence = null,
    // The abilities the subject declares, in document order.
    IReadOnlyList<PreviewAbility>? Abilities = null,
    // What this subject leaves behind when it dies, one entry per declared damage type.
    IReadOnlyList<PreviewDeathClone>? DeathClones = null,
    // How the subject comes apart when it declares no death clone, or null where it does not.
    PreviewSpinAway? SpinAway = null,
    // The explosion the SUBJECT sets off when it dies, from its own Death_Explosions.
    // Distinct from a hardpoint's and from a breakoff prop's, both of which already travel on
    // their own records. This is the one that goes off where the ship was, and without it on
    // the wire the client had nothing to play when the last hardpoint died.
    string? DeathExplosions = null,
    // Every projectile the tree defines, BY NAME - the attacker panel picks from all of them,
    // because a weapon built to shoot AT the subject has nothing to do with its own armament.
    IReadOnlyList<string>? ProjectileCatalog = null,
    // The colour the subject wears when NO faction colour applies, or null when it declares
    // none.
    //     The usual case, not the exception: faction colour is a SKIRMISH thing, so most of the
    //     time a unit is wearing this. 25 shipped objects declare one and it is genuinely
    //     per-object - a TIE Fighter is 75,75,75 whoever owns it, an indigenous Bantha is
    //     128,101,79, and ten of the 25 write pure white, which is the identity for the
    //     Colorization multiply and means "leave my texture alone".
    //     Fed to the SAME Colorization parameter as the team tint - the effects declare
    //     exactly one colourisation uniform, so this is a different VALUE rather than a second
    //     mechanism. Null rather than white when absent, so the client can tell "declared
    //     untinted" from "said nothing".
    PreviewRgba? NoColorizationColor = null,
    // The faction this subject belongs to, or null when it names none.
    // Which faction's No_Colorization_Color applies when no team tint does - the usual
    // case, since team colour is a skirmish thing. 772 shipped objects declare an
    // Affiliation and only 24 of those also declare a colour of their own, so the
    // faction's is what most units actually wear.
    //
    // The FIRST of several: 29 of the 775 tags name more than one - Neutral, Rebel,
    // Empire on the capturable structures - and a unit cannot wear two fallback colours.
    string? Affiliation = null,
    // The damage stages the object DECLARES, ascending, or empty where it declares none.
    //     From Land_Damage_Alternates, the middle column of a positional table it shares
    //     with Land_Damage_Thresholds and Land_Damage_SFX: at this fraction of
    //     health, show this _ALT, and play this sound. 222 shipped objects declare it.
    //     Sent because the client CANNOT work this out. It derives its ALT levels by walking
    //     the loaded geometry for alamoAlt tags, which only ever finds stages the model
    //     draws something for - and a stage need not draw anything, being possibly no more
    //     than an explosion and a sound. The shipped data proves the gap rather than merely
    //     allowing for it: <b>35</b> objects declare the lone alternate 3, and
    //     <b>7</b> declare 1, 2, 3 with no stage zero at all.
    //     Sorted and de-duplicated here, because as a SET of which stages exist the written
    //     order is meaningless - it belongs to the thresholds standing beside it.
    IReadOnlyList<int>? DamageStages = null,
    // The damage table in WRITTEN order, or empty when the object declares none usable.
    // What drives the stage in Gameplay: the hull's remaining fraction picks a band. Empty
    // rather than guessed when the two columns disagree in length or the thresholds are
    // missing - the pairing is positional, so half a table is not a table, and inventing an
    // alignment would put the wrong mesh on screen at the wrong health.
    IReadOnlyList<PreviewDamageBand>? DamageTable = null,
    // The object's uniform render scale - Scale_Factor - or 1 where it declares none.
    //     THE BRIDGE BETWEEN TWO SPACES, and without it the preview mixes them.
    //     the engine's transform update builds the object's world matrix from a
    //     translation and three rotations with NO scale in it, then calls
    //     Model->the model-scale setter(the type's scale factor(Type)) - so the scale lives on the MODEL
    //     alone. Geometry and bones are model units and reach the world multiplied by this;
    //     positions, and therefore every range and distance the XML declares, are already
    //     world units and are not.
    //     The two are otherwise the same unit, which the shipped data states outright:
    //     TIE_Crawler writes <Overall_Length>25.0</...> with the
    //     comment "(35 by geometry)", and reading Ev_tiecrawler.alo gives 34.0.
    //     Measured: 118 of the 174 armed objects across eaw/ and foc/ declare a
    //     scale that is not 1 - the Corellian Corvette 0.5, the Tartan 0.56, the Nebulon-B and
    //     the TIE Fighter 0.7 - so drawing hulls unscaled put the MAJORITY of armed subjects
    //     at odds with their own weapon ranges.
    float ScaleFactor = 1f,
    // The subject's Icon_Name, verbatim, or null for a bare model or an object that
    // declares none.
    // Carried for the capture's file name. The large command-bar icon has no tag of its own: the
    // engine formats big_ + Icon_Name and falls back to the plain icon when that
    // texture is missing, so the name is the whole link between a capture and its slot.
    string? IconName = null,
    // The file a bare model resolved to, for handing it to AloViewer or the Particle Editor.
    // Null for an object scene and for a model that resolves nowhere.
    PreviewSourceFile? SourceFile = null)
{
    /// <summary>Never null, so the client has one shape to walk.</summary>
    public IReadOnlyList<int> DamageStages { get; init; } = DamageStages ?? [];

    /// <summary>Never null, so the client has one shape to walk.</summary>
    public IReadOnlyList<PreviewDamageBand> DamageTable { get; init; } = DamageTable ?? [];

    /// <summary>Never null, so the client has one shape to walk.</summary>
    public IReadOnlyList<PreviewAbility> Abilities { get; init; } = Abilities ?? [];

    /// <summary>Never null, so the client has one shape to walk.</summary>
    public IReadOnlyList<PreviewDeathClone> DeathClones { get; init; } = DeathClones ?? [];

    /// <summary>Never null, so the client has one shape to walk.</summary>
    public IReadOnlyList<string> ProjectileCatalog { get; init; } = ProjectileCatalog ?? [];

    /// <summary>Never null, so the client has one shape to walk.</summary>
    public IReadOnlyList<PreviewProjectile> Projectiles { get; init; } = Projectiles ?? [];

    /// <summary>Never null, so the client has one shape to walk.</summary>
    public IReadOnlyList<PreviewWeapon> Weapons { get; init; } = Weapons ?? [];

    /// <summary>Never null, so the client has one shape to walk.</summary>
    public IReadOnlyList<PreviewBreakoffProp> BreakoffProps { get; init; } = BreakoffProps ?? [];

    /// <summary>
    ///     The particle systems the models carry, each pinned to a bone.
    /// </summary>
    /// <remarks>
    ///     Never null, so the client has one shape to walk. See <see cref="PreviewParticleResolver" />
    ///     for how a hardpoint's <c>Damage_Particles</c> bone claims the smoke attached to it.
    /// </remarks>
    public IReadOnlyList<PreviewParticle> Particles { get; init; } = Particles ?? [];

    /// <summary>Never null, so the client has one shape to walk.</summary>
    public IReadOnlyList<string> Animations { get; init; } = Animations ?? [];

    /// <summary>The scene for a subject that does not resolve at all.</summary>
    public static PreviewScene NotFound(string subject, string message, GameAssetTiers tiers)
    {
        return new PreviewScene(PreviewSceneKind.Object, subject, [], [], [],
            [new PreviewProblem(DiagnosticIds.PreviewSubjectNotFound, "error", message)], tiers);
    }
}
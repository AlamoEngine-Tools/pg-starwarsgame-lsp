// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

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
public sealed record PreviewRgba(int R, int G, int B, int A);

/// <summary>
///     One model instance in the scene.
/// </summary>
/// <param name="ModelRef">
///     The model name as the XML writes it. Left unresolved on purpose - the client asks for the GLB
///     by this name, and the resolver decides which layer supplies it.
/// </param>
/// <param name="AttachToPartId">The part this hangs off, or null for the root.</param>
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

/// <summary>
///     A hardpoint's firing arc, for the cone gizmo.
/// </summary>
/// <remarks>
///     Off by default in the viewer. A capital ship's arcs reach 2000 units and would swamp the hull
///     they are drawn on, so this is data the client shows on demand rather than by default.
/// </remarks>
public sealed record PreviewFireArc(
    IReadOnlyList<string> Bones, float? ConeWidthDegrees, float? ConeHeightDegrees, float? Range);

/// <summary>A turret hardpoint's rest pose and how far it may swing.</summary>
public sealed record PreviewTurret(
    float? RestAngle,
    float? RotateExtentDegrees,
    float? ElevateExtentDegrees,
    string? TurretBone,
    string? BarrelBone);

/// <summary>
///     One mounted hardpoint, with everything needed to destroy and repair it in the preview.
/// </summary>
/// <remarks>
///     The damage wiring is the part worth understanding. Destroying a hardpoint hides its
///     <see cref="PreviewPart" />, shows the proxies parented to <paramref name="DamageParticlesBone" />
///     on the hull, shows the <paramref name="DamageDecalBone" /> mesh, plays
///     <paramref name="DeathExplosionParticles" /> once, and - when
///     <paramref name="EngineDeathHidesEngineParticles" /> is set - hides the engine glow. Every one of
///     those is read from the hardpoint's own tags rather than guessed.
/// </remarks>
public sealed record PreviewHardpoint(
    string Id,
    string? PartId,
    string? Type,
    string? AttachBone,
    bool IsDestroyable,
    float? Health,
    string? DamageParticlesBone,
    string? DamageDecalBone,
    string? CollisionMeshBone,
    string? EngineParticlesBone,
    string? DeathExplosionParticles,
    string? DeathBreakoffProp,
    bool EngineDeathHidesEngineParticles,
    string? TooltipText,
    PreviewTurret? Turret,
    PreviewFireArc? Fire);

/// <summary>A faction's colours, for the colourisation controls.</summary>
/// <param name="Color">Team tint, applied to <c>Colorization</c> and <c>FC_</c>-prefixed meshes.</param>
/// <param name="NoColorizationColor">
///     Blended into white pixels of the hull texture's alpha channel - a different mechanism from the
///     team tint, and the reason both travel.
/// </param>
public sealed record PreviewFaction(
    string Name, PreviewRgba? Color, PreviewRgba? NoColorizationColor, PreviewRgba? DisplayFontColor);

/// <summary>Something the author should see about this scene.</summary>
/// <param name="Severity"><c>error</c>, <c>warning</c> or <c>info</c>.</param>
public sealed record PreviewProblem(string Severity, string Message, string? HardpointId = null);

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
    /// <summary>
    ///     The animation files that belong to this subject's model, by filename.
    ///
    ///     Enumerated rather than requested: previewing a MODEL offered no clips at all, because
    ///     nothing could ask which existed - the client sent an empty list and the GLB was baked
    ///     with none. Each is still verified against the skeleton when it is loaded, since 50
    ///     shipped animations sit beside a model they were not authored against.
    /// </summary>
    IReadOnlyList<string>? Animations = null,
    /// <summary>
    ///     The cameras the subject's own model carries, in MODEL space.
    ///
    ///     Empty for the 87% of models that declare none, which the client reports rather than
    ///     hides - a control with nothing to act on is disabled, not absent.
    /// </summary>
    IReadOnlyList<PreviewCamera>? Cameras = null,
    /// <summary>
    ///     The subject's <c>Type</c> tag, or null when previewing a bare model.
    ///
    ///     Carried so a camera preset can be BOUND to what a subject is rather than to its name.
    /// </summary>
    string? ObjectType = null,
    /// <summary>
    ///     The subject's category tokens, split out of its <c>CategoryMask</c>.
    ///
    ///     Split here rather than sent whole because the mask is MULTI-VALUED -
    ///     <c>Vehicle | AntiInfantry | AntiVehicle</c> - which is exactly why it cannot be used as a
    ///     plain lookup key. One token per entry is the form a match rule can actually test.
    /// </summary>
    IReadOnlyList<string>? Categories = null)
{
    /// <summary>
    ///     The particle systems the models carry, each pinned to a bone.
    /// </summary>
    /// <remarks>
    ///     Never null, so the client has one shape to walk. See <see cref="PreviewParticleResolver" />
    ///     for how a hardpoint's <c>Damage_Particles</c> bone claims the smoke hanging under it.
    /// </remarks>
    public IReadOnlyList<PreviewParticle> Particles { get; init; } = Particles ?? [];

    /// <summary>Never null, so the client has one shape to walk.</summary>
    public IReadOnlyList<string> Animations { get; init; } = Animations ?? [];

    /// <summary>The scene for a subject that does not resolve at all.</summary>
    public static PreviewScene NotFound(string subject, string message, GameAssetTiers tiers)
    {
        return new PreviewScene(PreviewSceneKind.Object, subject, [], [], [],
            [new PreviewProblem("error", message)], tiers);
    }
}

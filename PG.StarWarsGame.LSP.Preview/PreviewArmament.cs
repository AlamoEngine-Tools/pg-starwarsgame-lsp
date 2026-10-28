// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using JetBrains.Annotations;
using Newtonsoft.Json;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Preview;

/// <summary>Where a weapon is declared.</summary>
public enum PreviewWeaponSource
{
    /// <summary>An attached <c>HardPoint</c> object, with its own model, arcs and cadence.</summary>
    Hardpoint,

    /// <summary>
    ///     A <c>WEAPON</c> behaviour on the unit itself - the fighter case, and the common one.
    /// </summary>
    Unit
}

/// <summary>How the engine chooses which fire point a shot leaves from.</summary>
public enum PreviewFirePointMode
{
    /// <summary>Each bone in turn, one per volley. What every shipped hardpoint does.</summary>
    CycleBones,

    /// <summary>
    ///     Any random point along the line between the fire bones, in their direction. Set by
    ///     <c>Randomize_Between_Fire_Bones</c>, which says No on all 8 shipped uses - so the preview
    ///     is the first place anyone will see this branch drawn.
    /// </summary>
    RandomAlongLine
}

/// <summary>
///     One weapon: everything needed to draw its arc and describe its cadence.
/// </summary>
/// <remarks>
///     <para>
///         One list for both cases. The arc used to live on <see cref="PreviewHardpoint" />, so a
///         fighter could never have one; a second list would instead give the client two code paths
///         drawing the same cone and two places to fix when one is wrong.
///     </para>
///     <para>
///         Every measurement is nullable and absent means <em>not declared</em>, which is not the
///         same as zero. Arcs are off by default in the viewer: a capital ship's reach 2000 units and
///         would swamp the hull they are drawn on.
///     </para>
/// </remarks>
/// <param name="Id"><c>hardpoint:&lt;id&gt;</c>, or <c>weapon:A</c> for a unit weapon.</param>
/// <param name="Label">The hardpoint's <c>Type</c>, or the weapon's name.</param>
/// <param name="FireBones">
///     Where the shot leaves from. NOTE that a fire bone aims along its local X, not its Y - see
///     <c>AlamoFireBone.AimDirection</c>, which anything drawing this must go through.
/// </param>
/// <param name="FireModes">
///     The <c>Fire_When_*</c> gates that are set. Carried rather than collapsed to a boolean so the
///     client does not draw an arc for a hardpoint that cannot fire in the state being shown.
/// </param>
public sealed record PreviewWeapon(
    string Id,
    PreviewWeaponSource Source,
    string? HardpointId,
    string Label,
    IReadOnlyList<string> FireBones,
    PreviewFirePointMode FirePointMode,
    bool FiresForward,
    string? ProjectileType,
    float? Damage,
    string? DamageType,
    float? Range,
    float? MinRange,
    float? ConeWidthDegrees,
    float? ConeHeightDegrees,
    // How far a shot may stray from the aim point, per TARGET CATEGORY. Empty when the weapon
    // declares none. See PreviewInaccuracy.
    IReadOnlyList<PreviewInaccuracy> Inaccuracy,
    int? PulseCount,
    float? PulseDelaySeconds,
    float? RechargeSeconds,
    IReadOnlyList<string> FireModes,
    PreviewTurret? Turret,
    string? FireSfxEvent,
    // The arc while DEPLOYED, as full angles, or null for a weapon whose unit is never deployed.
    // Only a unit with Deploys and the walk locomotor is ever deployed, and while it is the
    // engine reads Deployed_Turret_*_Extent_Degrees in place of the normal pair - defaults 360
    // and 180, so unwritten means unrestricted. Same conventions as ConeWidthDegrees.
    float? DeployedConeWidthDegrees = null,
    float? DeployedConeHeightDegrees = null);

/// <summary>
///     How far a shot at one target category may stray from the aim point.
/// </summary>
/// <remarks>
///     <c>&lt;Fire_Inaccuracy_Distance&gt; Fighter, 30.0 &lt;/...&gt;</c> - a REPEATED, per-category
///     row, and every one of the 1017 in the shipped tree carries exactly those two fields. It was
///     read as a single number, which cannot parse `Fighter, 30.0`, so it had always come back null:
///     the field was dead on the wire and dead in the client. Categories are the engine's own target
///     buckets - Fighter, Bomber, Transport, Corvette, Frigate, Capital, Super on the space side,
///     Infantry, Vehicle and Structure on the land side.
/// </remarks>
public sealed record PreviewInaccuracy(string Category, float Distance);

/// <summary>A turret hardpoint's rest pose and how far it may swing.</summary>
/// <param name="DeployedRotateExtentDegrees">
///     How far a unit turret swings while DEPLOYED, or null when the unit is never deployed.
///     <c>Adjust_Turret_Facing</c> reads the deployed pair in place of the normal one
///     whenever the is-deployed test, so the swing opens with the shot.
/// </param>
public sealed record PreviewTurret(
    float? RestAngle,
    float? RotateExtentDegrees,
    float? ElevateExtentDegrees,
    string? TurretBone,
    string? BarrelBone,
    float? DeployedRotateExtentDegrees = null,
    float? DeployedElevateExtentDegrees = null);

/// <summary>
///     One attached hardpoint, with everything needed to destroy and repair it in the preview.
/// </summary>
/// <remarks>
///     The damage wiring is the part worth understanding. Destroying a hardpoint hides its
///     <see cref="PreviewPart" />, shows the proxies parented to <paramref name="DamageParticlesBone" />
///     on the hull, shows the <paramref name="DamageDecalBone" /> mesh, plays
///     <paramref name="DeathExplosionParticles" /> once, and - unless
///     <paramref name="EngineDeathHidesEngineParticles" /> is explicitly denied - hides the engine
///     glow. Every one of those is read from the hardpoint's own tags rather than guessed.
///     <para>
///         That last one defaults ON: <c>Engine_Death_Hide_Engine_Particles</c> is in the engine's
///         parameter table and in none of the shipped files, so requiring it to be written down
///         meant no destroyed engine block anywhere ever went dark. See
///         <see cref="EngineBoolean.IsTrueUnlessDenied" />.
///     </para>
/// </remarks>
[PublicAPI]
public sealed record PreviewHardpoint(
    string Id,
    string? PartId,
    string? Type,
    string? AttachBone,
    bool IsDestroyable,
    // Whether the game lets a player target this hardpoint, from Is_Targetable.
    // Decides whether a reticle is drawn over it. Every shipped hardpoint states it - 258 Yes
    // and 97 No in foc, none omitted - so the absent case defaults to false like every other
    // engine boolean here, and is untested against real data.
    bool IsTargetable,
    float? Health,
    string? DamageParticlesBone,
    string? DamageDecalBone,
    string? CollisionMeshBone,
    string? EngineParticlesBone,
    string? DeathExplosionParticles,
    string? DeathBreakoffProp,
    bool EngineDeathHidesEngineParticles,
    // The Tooltip_Text tag verbatim, which is a localisation KEY. 422 objects write one.
    // Kept beside the resolved TooltipText rather than replaced by it: the
    // key is what the author wrote and what they would search their own files for, and the text
    // is what a player reads. The ability rows make the same split between GuiName and
    // Name.
    string? TooltipKey,
    // What TooltipKey resolves to, or null when it resolves to nothing.
    // Null rather than an echo of the key. A key with no row is a real authoring mistake, but
    // it is the localisation editor's to report - showing the key as though it were text would
    // hide it behind something that looks like an answer.
    string? TooltipText,
    PreviewTurret? Turret);

/// <summary>
///     What the game draws over a targetable hardpoint, ready for the client to billboard.
/// </summary>
/// <remarks>
///     <para>
///         <c>ByType</c> is keyed by the hardpoint's <c>Type</c> and names an ICON per state;
///         <c>Icons</c> maps those names to <c>data:image/png;base64,...</c> URIs. Two levels rather
///         than one because thirteen hardpoint types share five artwork families in the base game,
///         and inlining the PNG per type would send the same image up to four times.
///     </para>
///     <para>
///         Icons are absent when no icon catalog is available - a scene is perfectly usable without
///         reticles, so that is a quiet omission rather than a problem.
///     </para>
/// </remarks>
[PublicAPI]
public sealed record PreviewReticles(
    // Both keyed by game data - a hardpoint's Type, and an icon's name - which the camel-case naming
    // strategy would otherwise rewrite. See VerbatimKeyDictionaryConverter.
    [property: JsonConverter(typeof(VerbatimKeyDictionaryConverter))]
    IReadOnlyDictionary<string, PreviewReticleStates> ByType,
    [property: JsonConverter(typeof(VerbatimKeyDictionaryConverter))]
    IReadOnlyDictionary<string, string> Icons,
    float? EnemyScreenSize,
    float? FriendlyScreenSize);

/// <summary>How the engine puts a projectile on screen.</summary>
public enum PreviewProjectileRender
{
    /// <summary>Drawn from its <c>.alo</c>.</summary>
    Model,

    /// <summary>
    ///     Drawn by the engine as a textured quad, from <c>Projectile_Custom_Render</c>.
    /// </summary>
    /// <remarks>
    ///     NOT exclusive with naming a model: 40 of foc's 63 custom-rendered projectiles carry one
    ///     anyway. The flag decides how it draws; the model travels regardless.
    /// </remarks>
    CustomQuad
}

/// <summary>
///     One projectile a weapon on this subject fires.
/// </summary>
/// <remarks>
///     Resolved through <c>Variant_Of_Existing_Type</c>, which matters more here than almost
///     anywhere else: 54 of foc's 173 projectiles are variants, and a bolt typically declares only
///     its model, colour and damage while speed, reach and every damage switch come from the
///     generic template it varies.
/// </remarks>
/// <param name="DoesShieldDamage">
///     With its two siblings, decides what a hit touches. A projectile that does only hitpoint
///     damage BYPASSES the shield entirely rather than being stopped by it.
/// </param>
[PublicAPI]
public sealed record PreviewProjectile(
    string Id,
    PreviewProjectileRender Render,
    string? ModelFile,
    float? Width,
    float? Length,
    string? TextureSlot,
    string? LaserColor,
    float? Speed,
    float? MaxFlightDistance,
    float? MaxRateOfTurn,
    float? Damage,
    string? DamageType,
    string? Category,
    bool DoesShieldDamage,
    bool DoesEnergyDamage,
    bool DoesHitpointDamage,
    float? BlastAreaDamage,
    float? BlastAreaRange,
    // How many objects one blast may damage, where the projectile caps it.
    // Declared twice in the whole of foc, so an absent cap is the normal case.
    int? BlastAreaMaxVictims,
    // Whether blast damage falls off with distance, rather than being flat inside the range.
    // Ten projectiles in foc set it, always alongside
    // BlastAreaDropoffTiers of 3, 4 or 5. The other 53 with a blast area
    // apply their full damage anywhere inside the radius.
    bool BlastAreaDropoff,
    // How many concentric bands the falloff is quantised into.
    int? BlastAreaDropoffTiers,
    string? DetonationParticles,
    string? ShieldAbsorbParticles,
    string? DetonateSfxEvent);
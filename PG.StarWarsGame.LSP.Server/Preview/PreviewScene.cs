// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Newtonsoft.Json;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
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
    /// <summary>
    ///     How far a shot may stray from the aim point, per TARGET CATEGORY. Empty when the weapon
    ///     declares none. See <see cref="PreviewInaccuracy" />.
    /// </summary>
    IReadOnlyList<PreviewInaccuracy> Inaccuracy,
    int? PulseCount,
    float? PulseDelaySeconds,
    float? RechargeSeconds,
    IReadOnlyList<string> FireModes,
    PreviewTurret? Turret,
    string? FireSfxEvent);

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
public sealed record PreviewTurret(
    float? RestAngle,
    float? RotateExtentDegrees,
    float? ElevateExtentDegrees,
    string? TurretBone,
    string? BarrelBone);

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
public sealed record PreviewHardpoint(
    string Id,
    string? PartId,
    string? Type,
    string? AttachBone,
    bool IsDestroyable,
    /// <summary>
    ///     Whether the game lets a player target this hardpoint, from <c>Is_Targetable</c>.
    /// </summary>
    /// <remarks>
    ///     Decides whether a reticle is drawn over it. Every shipped hardpoint states it - 258 Yes
    ///     and 97 No in foc, none omitted - so the absent case defaults to false like every other
    ///     engine boolean here, and is untested against real data.
    /// </remarks>
    bool IsTargetable,
    float? Health,
    string? DamageParticlesBone,
    string? DamageDecalBone,
    string? CollisionMeshBone,
    string? EngineParticlesBone,
    string? DeathExplosionParticles,
    string? DeathBreakoffProp,
    bool EngineDeathHidesEngineParticles,
    /// <summary>
    ///     The <c>Tooltip_Text</c> tag verbatim, which is a localisation KEY. 422 objects write one.
    /// </summary>
    /// <remarks>
    ///     Kept beside the resolved <paramref name="TooltipText" /> rather than replaced by it: the
    ///     key is what the author wrote and what they would search their own files for, and the text
    ///     is what a player reads. The ability rows make the same split between <c>GuiName</c> and
    ///     <c>Name</c>.
    /// </remarks>
    string? TooltipKey,
    /// <summary>
    ///     What <paramref name="TooltipKey" /> resolves to, or null when it resolves to nothing.
    /// </summary>
    /// <remarks>
    ///     Null rather than an echo of the key. A key with no row is a real authoring mistake, but
    ///     it is the localisation editor's to report - showing the key as though it were text would
    ///     hide it behind something that looks like an answer.
    /// </remarks>
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
    /// <summary>
    ///     How many objects one blast may damage, where the projectile caps it.
    /// </summary>
    /// <remarks>Declared twice in the whole of foc, so an absent cap is the normal case.</remarks>
    int? BlastAreaMaxVictims,
    /// <summary>
    ///     Whether blast damage falls off with distance, rather than being flat inside the range.
    /// </summary>
    /// <remarks>
    ///     Ten projectiles in foc set it, always alongside
    ///     <paramref name="BlastAreaDropoffTiers" /> of 3, 4 or 5. The other 53 with a blast area
    ///     apply their full damage anywhere inside the radius.
    /// </remarks>
    bool BlastAreaDropoff,
    /// <summary>How many concentric bands the falloff is quantised into.</summary>
    int? BlastAreaDropoffTiers,
    string? DetonationParticles,
    string? ShieldAbsorbParticles,
    string? DetonateSfxEvent);

/// <summary>Three floats as the XML writes them, for a direction or an axis of spin.</summary>
public sealed record PreviewVector3(float X, float Y, float Z);

/// <summary>
///     The wreckage a hardpoint sheds when it is destroyed.
/// </summary>
/// <remarks>
///     <para>
///         A <c>Death_Breakoff_Prop</c> names an ordinary <c>SpaceProp</c> with its own model and a
///         <c>DEBRIS</c> behaviour, so the hardpoint does not simply vanish - it breaks off and tumbles
///         away burning. 167 of foc's 355 hardpoints name one, 147 of them distinct.
///     </para>
///     <para>
///         Carried once per scene rather than inline on the hardpoint: mirrored hardpoints share a prop,
///         and a copy each would have the client instantiate the same wreck twice.
///     </para>
/// </remarks>
/// <param name="Resolved">
///     Whether the named prop is defined. An unresolved one is still listed - a renamed prop with a
///     hardpoint left pointing at the old name is exactly the authoring mistake worth surfacing.
/// </param>
/// <param name="MovementVector">
///     The direction the debris drifts, from <c>Debris_Movement_Vector</c>. Model space.
/// </param>
/// <param name="FacingRotateVector">Its axis of tumble, from <c>Debris_Facing_Rotate_Vector</c>.</param>
/// <param name="Animations">
///     The clips of the PROP'S OWN model, by file name. See <see cref="PreviewDeathClone" /> for why
///     a passive subject has to carry its own.
/// </param>
/// <param name="Particles">
///     The proxies the prop's own model carries - a burning piece of debris trails its own fire.
///     See <see cref="PreviewDeathClone" /> for how the client reads their <c>PartId</c>.
/// </param>
public sealed record PreviewBreakoffProp(
    string Id,
    string? ModelRef,
    bool Resolved,
    PreviewVector3? MovementVector,
    PreviewVector3? FacingRotateVector,
    float? MinLifetimeSeconds,
    float? MaxLifetimeSeconds,
    string? AttachedParticle,
    string? DeathExplosions,
    bool RemoveUponDeath,
    IReadOnlyList<string>? Animations = null,
    IReadOnlyList<PreviewParticle>? Particles = null)
{
    /// <summary>Never null, so the client has one shape to walk.</summary>
    public IReadOnlyList<string> Animations { get; init; } = Animations ?? [];

    /// <summary>Never null, so the client has one shape to walk.</summary>
    public IReadOnlyList<PreviewParticle> Particles { get; init; } = Particles ?? [];
}

/// <summary>A faction's colours, for the colourisation controls.</summary>
/// <param name="Color">Team tint, applied to <c>Colorization</c> and <c>FC_</c>-prefixed meshes.</param>
/// <param name="NoColorizationColor">
///     Blended into white pixels of the hull texture's alpha channel - a different mechanism from the
///     team tint, and the reason both travel.
/// </param>
/// <summary>
///     What a hit on this subject has to get through.
/// </summary>
/// <remarks>
///     <para>
///         The previewed object is the TARGET. The attacker is a weapon the reader builds in the
///         panel, so everything defensive travels with the scene and everything offensive is theirs
///         to type.
///     </para>
///     <para>
///         The armor axis of <c>Damage_To_Armor_Mod</c> is FIXED by the target - one
///         <c>Armor_Type</c> and one <c>Shield_Armor_Type</c> - so only those two columns are sent
///         rather than all 2426 rows. The damage axis is not fixed, because the reader picks it,
///         which is why <paramref name="DamageTypes" /> carries the whole list.
///     </para>
///     <para>
///         A damage type absent from <paramref name="HullFactors" /> or
///         <paramref name="ShieldFactors" /> is <strong>1.0</strong>, not zero. The shipped table
///         names barely half of its 4293 possible pairs, so the gap is hit constantly.
///     </para>
/// </remarks>
/// <param name="IsShielded">
///     Whether any of the three behaviour lists names <c>SHIELDED</c>. Measured over foc:
///     <c>SpaceBehavior</c> 98 times, plain <c>Behavior</c> 32, <c>LandBehavior</c> 19 - so all
///     three have to be read. Without it the shield is not in play at all, whatever
///     <c>Shield_Points</c> says.
/// </param>
/// <summary>
///     One ability the subject declares, and what it drives on the model.
/// </summary>
/// <remarks>
///     <para>
///         68 ability types exist over the two trees and MOST DRIVE NOTHING on the model - SPREAD_OUT
///         and HUNT are orders, not effects. So an ability with no bone, no particle and no clip is
///         the common case and is reported plainly rather than hidden: what a unit can do is worth
///         reading even when the answer is "nothing you can see here".
///     </para>
///     <para>
///         <paramref name="ProxyNames" /> are the particle proxies BOUND to this ability by their
///         name prefix - see <c>AbilityProxyPrefix</c>. The prefix is a hint, so a proxy is bound
///         only when the object also declares the type it names.
///     </para>
/// </remarks>
/// <param name="DeployClip">
///     The clip this ability plays when it activates, when the model ships one. 25 models in foc
///     carry a <c>_deploy_00</c>; 23 carry the matching <c>_undeploy_00</c>, and the X-Wing is one of
///     the two that ship a deploy with NO undeploy - so the pair is genuinely optional on both sides.
/// </param>
/// <summary>
///     What this object leaves behind when it dies, and what killed it decides which one.
/// </summary>
/// <remarks>
///     <para>
///         <c>&lt;Death_Clone&gt; Damage_Type, Object_Id &lt;/...&gt;</c> - 345 rows in foc, 134 in
///         eaw, and 344 of the 345 carry exactly those two fields. The damage type is what ties this
///         to the attacker panel: the weapon a reader builds decides which clone the target would
///         leave.
///     </para>
///     <para>
///         The ONE tag in the schema with <c>variantMode: merge</c> plus <c>multipleAllowed</c>, so
///         the resolver keeps every occurrence and no <c>RepeatedTagReader</c> is needed - and the
///         merge is ADDITIVE across a variant chain, so a re-skinned variant keeps its base's clones
///         as well as its own.
///     </para>
/// </remarks>
/// <param name="PlaysIdle">
///     <c>Should_Death_Clone_Play_Idle</c>, which is a Boolean on the OBJECT rather than a field on
///     the row - so it reads the same on every clone this object names. 28 shipped uses, all
///     <c>true</c>. Worth knowing that it asks for an idle the clone models do not appear to carry:
///     of eaw's 44 clone objects with a model, 42 ship only a <c>_die</c> clip and NONE ships an
///     idle.
/// </param>
/// <summary>
///     One stat an ability multiplies while it is active.
/// </summary>
/// <remarks>
///     <c>&lt;Mod_Multiplier&gt; SPEED_MULTIPLIER, 0.8f &lt;/...&gt;</c> - 316 uses over 9 kinds in
///     foc, led by SPEED, WEAPON_DELAY, ENERGY_REGEN and SHIELD_REGEN. This is the whole of what a
///     stat-only ability does: DEFEND declares no proxy, no bone, no particle and no clip, so
///     without these its row is a switch with nothing behind it.
/// </remarks>
public sealed record PreviewAbilityModifier(string Stat, float Factor);

/// <param name="Animations">
///     The clips of the CLONE'S OWN model, by file name.
/// </param>
/// <remarks>
///     <para>
///         Carried here rather than left to the scene's list, which describes the subject. That the
///         clone ever played at all was an accident of naming: a clone's model is conventionally the
///         hull's name plus a suffix - <c>Ev_stardestroyer_d.alo</c> beside
///         <c>Ev_stardestroyer.alo</c> - so its <c>_die_00.ala</c> matched the hull's stem prefix
///         and rode along in the hull's list, to be dropped from the hull's own GLB when it failed
///         to bind to that skeleton. A clone named anything else got no clip whatsoever.
///     </para>
/// </remarks>
/// <param name="Particles">
///     The proxies the clone's own model carries - for the Star Destroyer's wreck, eight of them:
///     the explosions, the fire smoke and the debris trails that ARE the death.
///
///     Their <c>PartId</c> names the CLONE OBJECT, not a part in the scene, because the server
///     cannot know what the client will call the instance it loads. It is a descriptor of a model,
///     and the client rewrites both it and the ids when it puts one in the scene.
/// </param>
/// <summary>
///     The automated death clone: a unit that declares none keeps flying and comes apart.
/// </summary>
/// <remarks>
///     <para>
///         The user's account of the mechanic: with <c>Spin_Away_On_Death</c> set and NO
///         <c>Death_Clone</c> declared, the unit carries on along its current vector at its current
///         speed, corkscrewing, and explodes at the end.
///     </para>
///     <para>
///         Measured over both shipped trees: <b>34 objects declare it, all Yes, and not one of them
///         also declares a Death_Clone.</b> The rule holds in the data. The engine's own parameter
///         table names five tags in the family, so the time, the chance and the explosion are read
///         rather than invented; only the corkscrew itself has no number in any file.
///     </para>
/// </remarks>
/// <param name="TimeSeconds">
///     <c>Spin_Away_On_Death_Time</c>. 31 of the 34 write <c>2.0f</c> and 3 write <c>1.0f</c> - note
///     the <c>f</c> suffix, which is in the files and has to be parsed off.
/// </param>
/// <param name="Chance">
///     <c>Spin_Away_On_Death_Chance</c>, 0 to 1. Shipped values are 0.2 (21) and 0.4 (13), so most
///     deaths do NOT spin. The preview always spins and READS THIS OUT instead - a tool pressed to
///     see a thing has to show the thing, and a one-in-five roll looks broken four times out of five.
/// </param>
/// <param name="Explosion">
///     <c>Spin_Away_On_Death_Explosion</c> - its OWN explosion, fired at the end of the spin, and a
///     different tag from the object's <c>Death_Explosions</c>.
/// </param>
/// <param name="MaxSpeed">
///     <c>Max_Speed</c>, which all 34 declare. **Per FRAME, not per second** - the engine runs at
///     30Hz, so 4.5 here is 135 units a second. The client does that multiplication.
/// </param>
public sealed record PreviewSpinAway(
    float TimeSeconds,
    float Chance,
    string? Explosion,
    float MaxSpeed);

public sealed record PreviewDeathClone(
    string? DamageType,
    string ObjectId,
    string? ModelFile,
    bool PlaysIdle,
    IReadOnlyList<string>? Animations = null,
    IReadOnlyList<PreviewParticle>? Particles = null)
{
    /// <summary>Never null, so the client has one shape to walk.</summary>
    public IReadOnlyList<string> Animations { get; init; } = Animations ?? [];

    /// <summary>Never null, so the client has one shape to walk.</summary>
    public IReadOnlyList<PreviewParticle> Particles { get; init; } = Particles ?? [];
}

/// <param name="GuiName">
///     The ability's <c>GUI_Activated_Ability_Name</c>: NOT display text, but the name of a
///     <c>SpecialAbility</c> block defined elsewhere in the XML. Carried so the lens can offer a jump
///     to that definition; 39% of shipped abilities declare one, and an ability without it never
///     reaches the command bar at all.
/// </param>
/// <param name="Name">
///     What the command bar calls this ability, already localised. From
///     <c>Alternate_Name_Text</c> when the instance overrides it, otherwise the
///     <c>TEXT_TOOLTIP_ABILITY_&lt;TYPE&gt;_NAME</c> convention. Null when neither resolves, which
///     for a shipped ability means the mod has replaced the text without providing it.
/// </param>
/// <param name="Description">
///     The tooltip text, already localised, resolved the same way from
///     <c>Alternate_Description_Text</c> or <c>TEXT_TOOLTIP_ABILITY_&lt;TYPE&gt;_DESCRIPTION</c>.
/// </param>
/// <param name="IconDataUri">
///     The command-bar icon as a <c>data:image/png;base64,...</c> URI, or null when nothing could be
///     asserted about it. A declared-but-missing icon carries the missing-art placeholder here AND
///     raises a problem, because that is what the engine would draw. Same shape as the reticle icons
///     beside it. The atlas slot is 26x26 - draw it near native size rather than blown up, which is
///     how the game itself shows it.
/// </param>
public sealed record PreviewAbility(
    string Type,
    string? GuiName,
    string? OwnerAttachmentBone,
    string? ParticleEffect,
    float? RechargeSeconds,
    float? ExpirationSeconds,
    IReadOnlyList<string> ProxyNames,
    string? DeployClip,
    string? UndeployClip,
    IReadOnlyList<PreviewAbilityModifier> Modifiers,
    string? Name = null,
    string? Description = null,
    string? IconDataUri = null);

public sealed record PreviewTargetDefence(
    bool IsShielded,
    string? ArmorType,
    string? ShieldArmorType,
    float? ShieldPoints,
    float? TacticalHealth,
    float? EnergyCapacity,
    /// <summary>
    ///     The summed <c>Health</c> of every DESTRUCTIBLE hardpoint, or null where there are none.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A unit with hardpoints cannot be targeted itself - only its hardpoints can - and it dies
    ///         when the last of them does. Untargetable hardpoints count: the game warns about that
    ///         combination and at least two mods use it deliberately, so an untargetable hardpoint still
    ///         has to die before the unit does. Indestructible ones cannot contribute to a death and
    ///         are left out.
    ///     </para>
    ///     <para>
    ///         Sent ALONGSIDE <paramref name="TacticalHealth" /> rather than replacing it, because
    ///         the two are SEPARATE POOLS and disagree in the shipped data - the Star Destroyer
    ///         declares 2000 against 4075 of hardpoint health. How the engine reconciles them was
    ///         settled by decompiling the 2018 build: each pool is capped at the other's PERCENTAGE
    ///         plus <see cref="HullVsHardpointsConstraint" />, so the absolute totals never have to
    ///         agree. See <paramref name="DiesWithHardpoints" /> for the half that is gated.
    ///     </para>
    /// </remarks>
    float? HardpointHealthTotal,
    /// <summary>
    ///     <c>Should_Be_Destroyed_When_All_Hardpoints_Destroyed</c>, defaulting to true.
    /// </summary>
    /// <remarks>
    ///     Gates three links from the hardpoints back to the hull: dying when the last destroyable
    ///     hardpoint does, the per-tick pull of the hull down toward the hardpoints, and the health
    ///     bar's use of the hardpoint pool. With it off - <c>U_Ground_Palace</c> is the one vanilla
    ///     object that says so - hardpoints still absorb and still die, they just stop reaching the
    ///     hull by any route. Absent means true: exactly one shipped object writes the tag.
    /// </remarks>
    bool DiesWithHardpoints,
    /// <summary>
    ///     <c>Hull_Vs_Hard_Points_Health_Constraint</c> from GameConstants, shipped at 0.2.
    /// </summary>
    /// <remarks>
    ///     How far either pool may run ahead of the other, as a fraction. Read rather than assumed
    ///     because it is a global a mod can change, and the change is drastic: at 1 every cap clamps
    ///     to 100% and BOTH corrections stop running, leaving the pools fully independent. Drawing
    ///     vanilla's 0.2 for such a mod would show a leash their game does not have.
    /// </remarks>
    float HullVsHardpointsConstraint,
    // Keyed by game data - a damage type name - which the camel-case naming strategy would otherwise
    // rewrite. See VerbatimKeyDictionaryConverter.
    [property: JsonConverter(typeof(VerbatimKeyDictionaryConverter))]
    IReadOnlyDictionary<string, float> HullFactors,
    [property: JsonConverter(typeof(VerbatimKeyDictionaryConverter))]
    IReadOnlyDictionary<string, float> ShieldFactors,
    IReadOnlyList<string> DamageTypes);

public sealed record PreviewFaction(
    string Name, PreviewRgba? Color, PreviewRgba? NoColorizationColor, PreviewRgba? DisplayFontColor);

/// <summary>Something the author should see about this scene.</summary>
/// <param name="DiagnosticId">
///     Which kind of finding this is. Required rather than optional, and first rather than last, so
///     a new preview finding cannot be added without one - which is exactly how every one of these
///     came to have no id at all.
/// </param>
/// <param name="Severity"><c>error</c>, <c>warning</c> or <c>info</c>.</param>
/// <summary>
///     One row of the damage table: the health band's upper bound, and the stage shown inside it.
/// </summary>
/// <remarks>
///     <para>
///         The thresholds are the upper and lower BOUND of each stage, not a list of trip points.
///         <c>1, 0.66, 0.33, 0</c> against <c>0, 1, 2, 3</c> reads: 100% &gt; h &gt; 66% is ALT0,
///         66% &gt; h &gt; 33% is ALT1, 33% &gt; h &gt; 0% is ALT2, and 0 is ALT3. So a row's own
///         threshold is its ceiling and the NEXT row's is its floor; the last row runs to zero.
///     </para>
///     <para>
///         Sent as ordered PAIRS because <see cref="PreviewScene.DamageStages" /> deliberately sorts
///         and de-duplicates - the right shape for "which stages exist" and useless for "which stage
///         at what health". Measured over foc with XML comments stripped: 219 objects declare the
///         table, every one has thresholds, and the two columns never disagree in length.
///     </para>
/// </remarks>
public sealed record PreviewDamageBand(float Threshold, int Stage);

public sealed record PreviewProblem(
    DiagnosticId DiagnosticId, string Severity, string Message, string? HardpointId = null);

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
    IReadOnlyList<string>? Categories = null,
    /// <summary>
    ///     The wreckage this subject's hardpoints shed, one entry per distinct prop.
    /// </summary>
    IReadOnlyList<PreviewBreakoffProp>? BreakoffProps = null,
    /// <summary>
    ///     Every weapon on the subject, wherever it is declared. See <see cref="PreviewWeapon" />.
    /// </summary>
    IReadOnlyList<PreviewWeapon>? Weapons = null,
    /// <summary>
    ///     The targeting reticles for this subject's hardpoint types, or null for a bare model.
    /// </summary>
    PreviewReticles? Reticles = null,
    /// <summary>
    ///     The projectiles this subject's weapons name, one entry per distinct projectile.
    /// </summary>
    IReadOnlyList<PreviewProjectile>? Projectiles = null,
    /// <summary>
    ///     What a hit on this subject has to get through, or null for a bare model.
    /// </summary>
    PreviewTargetDefence? Defence = null,
    /// <summary>
    ///     The abilities the subject declares, in document order.
    /// </summary>
    IReadOnlyList<PreviewAbility>? Abilities = null,
    /// <summary>
    ///     What this subject leaves behind when it dies, one entry per declared damage type.
    /// </summary>
    IReadOnlyList<PreviewDeathClone>? DeathClones = null,
    /// <summary>
    ///     How the subject comes apart when it declares no death clone, or null where it does not.
    /// </summary>
    PreviewSpinAway? SpinAway = null,
    /// <summary>
    ///     The explosion the SUBJECT sets off when it dies, from its own <c>Death_Explosions</c>.
    /// </summary>
    /// <remarks>
    ///     Distinct from a hardpoint's and from a breakoff prop's, both of which already travel on
    ///     their own records. This is the one that goes off where the ship was, and without it on
    ///     the wire the client had nothing to play when the last hardpoint died.
    /// </remarks>
    string? DeathExplosions = null,
    /// <summary>
    ///     Every projectile the tree defines, BY NAME - the attacker panel picks from all of them,
    ///     because a weapon built to shoot AT the subject has nothing to do with its own armament.
    /// </summary>
    IReadOnlyList<string>? ProjectileCatalog = null,
    /// <summary>
    ///     The colour the subject wears when NO faction colour applies, or null when it declares
    ///     none.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The usual case, not the exception: faction colour is a SKIRMISH thing, so most of the
    ///         time a unit is wearing this. 25 shipped objects declare one and it is genuinely
    ///         per-object - a TIE Fighter is <c>75,75,75</c> whoever owns it, an indigenous Bantha is
    ///         <c>128,101,79</c>, and ten of the 25 write pure white, which is the identity for the
    ///         <c>Colorization</c> multiply and means "leave my texture alone".
    ///     </para>
    ///     <para>
    ///         Fed to the SAME <c>Colorization</c> parameter as the team tint - the effects declare
    ///         exactly one colourisation uniform, so this is a different VALUE rather than a second
    ///         mechanism. Null rather than white when absent, so the client can tell "declared
    ///         untinted" from "said nothing".
    ///     </para>
    /// </remarks>
    PreviewRgba? NoColorizationColor = null,
    /// <summary>
    ///     The faction this subject belongs to, or null when it names none.
    /// </summary>
    /// <remarks>
    ///     Which faction's <c>No_Colorization_Color</c> applies when no team tint does - the usual
    ///     case, since team colour is a skirmish thing. 772 shipped objects declare an
    ///     <c>Affiliation</c> and only 24 of those also declare a colour of their own, so the
    ///     faction's is what most units actually wear.
    ///
    ///     The FIRST of several: 29 of the 775 tags name more than one - <c>Neutral, Rebel,
    ///     Empire</c> on the capturable structures - and a unit cannot wear two fallback colours.
    /// </remarks>
    string? Affiliation = null,
    /// <summary>
    ///     The damage stages the object DECLARES, ascending, or empty where it declares none.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         From <c>Land_Damage_Alternates</c>, the middle column of a positional table it shares
    ///         with <c>Land_Damage_Thresholds</c> and <c>Land_Damage_SFX</c>: at this fraction of
    ///         health, show this <c>_ALT</c>, and play this sound. 222 shipped objects declare it.
    ///     </para>
    ///     <para>
    ///         Sent because the client CANNOT work this out. It derives its ALT levels by walking
    ///         the loaded geometry for <c>alamoAlt</c> tags, which only ever finds stages the model
    ///         draws something for - and a stage need not draw anything, being possibly no more
    ///         than an explosion and a sound. The shipped data proves the gap rather than merely
    ///         allowing for it: <b>35</b> objects declare the lone alternate <c>3</c>, and
    ///         <b>7</b> declare <c>1, 2, 3</c> with no stage zero at all.
    ///     </para>
    ///     <para>
    ///         Sorted and de-duplicated here, because as a SET of which stages exist the written
    ///         order is meaningless - it belongs to the thresholds standing beside it.
    ///     </para>
    /// </remarks>
    IReadOnlyList<int>? DamageStages = null,
    /// <summary>
    ///     The damage table in WRITTEN order, or empty when the object declares none usable.
    /// </summary>
    /// <remarks>
    ///     What drives the stage in Gameplay: the hull's remaining fraction picks a band. Empty
    ///     rather than guessed when the two columns disagree in length or the thresholds are
    ///     missing - the pairing is positional, so half a table is not a table, and inventing an
    ///     alignment would put the wrong mesh on screen at the wrong health.
    /// </remarks>
    IReadOnlyList<PreviewDamageBand>? DamageTable = null)
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

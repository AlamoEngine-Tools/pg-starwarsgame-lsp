// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The 3D model preview. Mirrors PG.StarWarsGame.LSP.Server/Preview/PreviewScene.cs and
// PreviewProtocol.cs.

// ── the scene ────────────────────────────────────────────────────────────────

/** What the scene was built from. Mirrors the C# `PreviewSceneKind`. */
export const PREVIEW_SCENE_KIND = {
    /** A single `.alo`, opened directly. */
    model: 'Model',
    /** A GameObject, assembled from its model and everything its XML mounts on it. */
    object: 'Object',
    /**
     * A particle system, opened directly.
     *
     * Shares the `.alo` extension with models, so the server classifies by root chunk and says which
     * it found. A particle scene takes `aet/getParticleSystem`, never `aet/getModelGlb`.
     */
    particle: 'Particle',
} as const;

/** What decides whether a particle system is playing. Mirrors the C# `PreviewParticleGate`. */
export const PREVIEW_PARTICLE_GATE = {
    /** Plays whenever the model is shown, subject to the proxy's own visibility flag. */
    always: 'Always',
    /** Damage smoke: off until its hardpoint is destroyed. */
    hardpointDestroyed: 'HardpointDestroyed',
    /** Engine glow: on until its hardpoint dies, for hardpoints whose death puts it out. */
    hardpointAlive: 'HardpointAlive',
} as const;

/** Why a part is in the scene. Mirrors the C# `PreviewPartOrigin`. */
export const PREVIEW_PART_ORIGIN = {
    /** The object's own tactical model. */
    hull: 'Hull',
    /** A hardpoint's `Model_To_Attach`. */
    hardpoint: 'Hardpoint',
} as const;

/** Four 0-255 channels, alpha last, exactly as the game writes them. */
export interface PreviewRgba {
    r: number;
    g: number;
    b: number;
    a: number;
}

/**
 * One model instance in the scene.
 *
 * `modelRef` is the name as the XML writes it, deliberately unresolved - ask for the GLB by this
 * name and the server decides which layer supplies it.
 *
 * `resolved` false means the file was not found. The part is still here on purpose: a hardpoint
 * whose model is missing is an authoring mistake worth drawing attention to, not one to hide.
 */
export interface PreviewPart {
    id: string;
    modelRef: string;
    /** The part this hangs off, or absent for the root. */
    attachToPartId?: string | null;
    /** The bone on that part, or absent to sit at its origin. */
    attachBone?: string | null;
    /** See {@link PREVIEW_PART_ORIGIN}. */
    origin: string;
    hardpointId?: string | null;
    resolved: boolean;
}

/** Where a weapon is declared. Mirrors the C# `PreviewWeaponSource`. */
export const PREVIEW_WEAPON_SOURCE = {
    hardpoint: 'Hardpoint',
    unit: 'Unit',
} as const;

/** How the engine picks which fire point a shot leaves from. Mirrors `PreviewFirePointMode`. */
export const PREVIEW_FIRE_POINT_MODE = {
    /** Each bone in turn, one per volley. What every shipped mount does. */
    cycleBones: 'CycleBones',
    /** Any random point along the line between the fire bones. */
    randomAlongLine: 'RandomAlongLine',
} as const;

/**
 * One weapon bank: everything needed to draw its arc and describe its cadence.
 *
 * One list for both cases, because the arc used to live on the hardpoint and a fighter - whose
 * armament sits on the unit itself - could therefore never have one.
 *
 * Every measurement is nullable and absent means *not declared*, which is NOT the same as zero. A
 * capital ship's arcs reach 2000 units, so these are drawn on demand rather than by default.
 *
 * NOTE: a fire bone aims along its local **X**, not the Y that reads as forward. Taking Y puts a
 * Star Destroyer's port guns astern and its starboard guns forward - mirrored hulls make that
 * mistake look right on one side. Go through the shared aim helper, never the raw bone matrix.
 */
export interface PreviewWeapon {
    /** `hardpoint:<id>`, or `bank:A` for a unit weapon. */
    id: string;
    /** See {@link PREVIEW_WEAPON_SOURCE}. */
    source: string;
    /** Set only when `source` is Hardpoint. */
    hardpointId?: string | null;
    label: string;
    fireBones: string[];
    /** See {@link PREVIEW_FIRE_POINT_MODE}. */
    firePointMode: string;
    /** Forces the shot straight down the bone rather than anywhere in the cone. */
    firesForward: boolean;
    projectileType?: string | null;
    damage?: number | null;
    damageType?: string | null;
    range?: number | null;
    minRange?: number | null;
    coneWidthDegrees?: number | null;
    coneHeightDegrees?: number | null;
    /**
     * How far a shot may stray from the aim point, per TARGET CATEGORY. Empty when none is declared.
     *
     * `<Fire_Inaccuracy_Distance> Fighter, 30.0 </...>` is a REPEATED, per-category row and every one
     * of the 1017 in the shipped tree carries exactly those two fields. It was a single `number`
     * here and read as one on the server, which cannot parse `Fighter, 30.0` - so it had never once
     * arrived non-null.
     */
    inaccuracy: PreviewInaccuracy[];
    pulseCount?: number | null;
    pulseDelaySeconds?: number | null;
    rechargeSeconds?: number | null;
    /** The `Fire_When_*` gates that are set; a mount cannot fire in a state absent from this. */
    fireModes: string[];
    turret?: PreviewTurret | null;
    fireSfxEvent?: string | null;
}

/** How the engine puts a projectile on screen. Mirrors `PreviewProjectileRender`. */
export const PREVIEW_PROJECTILE_RENDER = {
    /** Drawn from its `.alo`. */
    model: 'Model',
    /**
     * Drawn by the engine as a textured quad.
     *
     * NOT exclusive with naming a model - 40 of foc's 63 custom-rendered projectiles carry one
     * anyway - so the flag decides how it draws and `modelFile` travels regardless.
     */
    customQuad: 'CustomQuad',
} as const;

/**
 * One projectile a weapon on this subject fires.
 *
 * Resolved through `Variant_Of_Existing_Type`, which matters here more than almost anywhere else: a
 * bolt typically declares only its model, colour and damage, while speed, reach and every damage
 * switch come from the generic template it varies.
 *
 * The three `does*Damage` switches decide what a hit touches. One that does only hitpoint damage
 * BYPASSES the shield rather than being stopped by it.
 */
export interface PreviewProjectile {
    id: string;
    /** See {@link PREVIEW_PROJECTILE_RENDER}. */
    render: string;
    modelFile?: string | null;
    width?: number | null;
    length?: number | null;
    /** `n,m` into the bolt atlas. */
    textureSlot?: string | null;
    laserColor?: string | null;
    speed?: number | null;
    maxFlightDistance?: number | null;
    /** 0 for a straight bolt; above 0 it tracks. */
    maxRateOfTurn?: number | null;
    damage?: number | null;
    damageType?: string | null;
    category?: string | null;
    doesShieldDamage: boolean;
    doesEnergyDamage: boolean;
    doesHitpointDamage: boolean;
    blastAreaDamage?: number | null;
    blastAreaRange?: number | null;
    /** How many objects one blast may damage. Declared twice in the whole of foc. */
    blastAreaMaxVictims?: number | null;
    /**
     * Whether blast damage falls off with distance instead of being flat inside the range.
     *
     * Ten projectiles in foc set it, always with `blastAreaDropoffTiers` of 3, 4 or 5. The other 53
     * with a blast area apply their full damage anywhere inside the radius.
     */
    blastAreaDropoff: boolean;
    /** How many concentric bands the falloff is quantised into. */
    blastAreaDropoffTiers?: number | null;
    detonationParticles?: string | null;
    shieldAbsorbParticles?: string | null;
    detonateSfxEvent?: string | null;
}

/**
 * The icons one hardpoint type shows in each of its targeting states.
 *
 * Seven states, though on every shipped type the disabled pair reuses the plain and tracked art, so
 * a family resolves to three files rather than seven. A mod may give them separate art.
 */
export interface PreviewReticleStates {
    enemy?: string | null;
    enemyTracked?: string | null;
    friendly?: string | null;
    friendlyTracked?: string | null;
    friendlyRepairing?: string | null;
    friendlyDisabled?: string | null;
    friendlyDisabledTracked?: string | null;
}

/**
 * What the game draws over a targetable hardpoint.
 *
 * Two levels on purpose: `byType` names an ICON per state, and `icons` maps those names to data
 * URIs. Thirteen hardpoint types share five artwork families in the base game, so inlining the PNG
 * per type would send the same image up to four times.
 *
 * `screenSize` is `0.03` in both shipped trees. What it is a fraction OF is not settled - screen
 * height is the obvious reading.
 */
export interface PreviewReticles {
    byType: Record<string, PreviewReticleStates>;
    /** Icon name to `data:image/png;base64,...`. Empty when no icon catalog was available. */
    icons: Record<string, string>;
    enemyScreenSize?: number | null;
    friendlyScreenSize?: number | null;
}

/**
 * What this subject leaves behind when it dies, and what killed it decides which one.
 *
 * `<Death_Clone> Damage_Type, Object_Id </...>` - 345 rows in foc, 134 in eaw. The damage type is
 * what ties this to the attacker panel: the weapon you build decides which clone the target leaves.
 *
 * `playsIdle` is `Should_Death_Clone_Play_Idle`, a Boolean on the OBJECT rather than a field on the
 * row, so it reads the same on every clone. 28 shipped uses, all true - and worth knowing that it
 * asks for an idle the clone models do not appear to carry: of eaw's 44 clone objects with a model,
 * 42 ship only a `_die` clip and none ships an idle.
 */
export interface PreviewDeathClone {
    /** The damage type that produces this clone, or absent when the row named none. */
    damageType?: string | null;
    objectId: string;
    /** The clone's own tactical model, or absent when the clone is not defined. */
    modelFile?: string | null;
    playsIdle: boolean;
    /**
     * The clips of the CLONE'S OWN model, by file name.
     *
     * Its own, because the scene's list describes the SUBJECT. That a clone ever played at all was
     * an accident of naming - a clone's model is conventionally the hull's name plus a suffix, so
     * its `_die_00.ala` matched the hull's stem and rode along in the hull's list. A clone named
     * anything else got no clip whatsoever.
     */
    animations: string[];
    /**
     * The proxies the clone's own model carries - the explosions, the fire smoke and the debris
     * trails that ARE the death. Never reached the client before at all.
     *
     * Their `partId` names the CLONE OBJECT, not a part in the scene: the server cannot know what
     * the client will call the instance it loads, so this is a descriptor of a MODEL and the client
     * rewrites the ids when it puts one in the scene.
     */
    particles: PreviewParticle[];
}

/**
 * One stat an ability multiplies while it is active.
 *
 * `<Mod_Multiplier> SPEED_MULTIPLIER, 0.8f </...>` - 316 uses over 9 kinds in foc. This is the whole
 * of what a stat-only ability does: DEFEND declares no proxy, no bone, no particle and no clip.
 */
export interface PreviewAbilityModifier {
    stat: string;
    factor: number;
}

/**
 * One ability the subject declares, and what it drives on the model.
 *
 * 68 ability types exist over the two trees and MOST DRIVE NOTHING visible - SPREAD_OUT and HUNT
 * are orders, not effects - so an ability with no bone, no particle and no clip is the common case
 * and is reported plainly rather than hidden.
 *
 * `proxyNames` are the particle proxies bound to this ability by their name PREFIX (`PPTW_`,
 * `PTE_`, `PRS_`, `PAS_`, `PEM_`, `PGW_`). The prefix is a hint, not a rule: a proxy is bound only
 * when the object also declares the type it names, and the rest are reported as unbound effects at
 * `info` - normal authoring, not a problem.
 */
export interface PreviewAbility {
    type: string;
    guiName?: string | null;
    ownerAttachmentBone?: string | null;
    particleEffect?: string | null;
    rechargeSeconds?: number | null;
    expirationSeconds?: number | null;
    proxyNames: string[];
    /** The clip this plays on activation, when the model ships one. Both sides are optional. */
    deployClip?: string | null;
    undeployClip?: string | null;
    /** The stats it multiplies while active. For many abilities this is all they do. */
    modifiers: PreviewAbilityModifier[];
}

/**
 * What a hit on the previewed subject has to get through.
 *
 * The subject on stage is the TARGET. The attacker is a weapon the reader builds in the panel, so
 * everything defensive travels with the scene and everything offensive is theirs to type.
 *
 * The armor axis of `Damage_To_Armor_Mod` is fixed by the target - one `Armor_Type` and one
 * `Shield_Armor_Type` - so only those two columns are sent rather than all 2426 rows. The damage
 * axis is not, because the reader picks it, which is why `damageTypes` carries the whole list.
 *
 * A damage type ABSENT from either factor map is **1.0**, not zero. The shipped table names barely
 * half of its 4293 possible pairs.
 */
export interface PreviewTargetDefence {
    /**
     * Whether any behaviour list names `SHIELDED`. Without it the shield is not in play at all,
     * whatever `shieldPoints` says.
     */
    isShielded: boolean;
    armorType?: string | null;
    shieldArmorType?: string | null;
    shieldPoints?: number | null;
    tacticalHealth?: number | null;
    energyCapacity?: number | null;
    /**
     * The summed `Health` of every destructible hardpoint, or absent where there are none.
     *
     * A unit with hardpoints cannot be targeted itself and dies when its last mount does, so this
     * is the pool that actually drains. Sent alongside `tacticalHealth` rather than replacing it -
     * the two disagree in the shipped data (2000 against 4075 on the Star Destroyer) and nobody
     * knows how the engine reconciles them.
     */
    hardpointHealthTotal?: number | null;
    /** Damage type to factor, against the target's `Armor_Type`. Absent means 1.0. */
    hullFactors: Record<string, number>;
    /** Damage type to factor, against the target's `Shield_Armor_Type`. Absent means 1.0. */
    shieldFactors: Record<string, number>;
    /** Every damage type the tree declares - 81 in foc - for the attacker panel's picker. */
    damageTypes: string[];
}

/** Three floats as the XML writes them, for a direction or an axis of spin. */
export interface PreviewVector3 {
    x: number;
    y: number;
    z: number;
}

/**
 * The wreckage a hardpoint sheds when it is destroyed.
 *
 * A `Death_Breakoff_Prop` names a `SpaceProp` with its own model and a DEBRIS behaviour, so a
 * destroyed mount tumbles away burning rather than simply vanishing. Listed once per distinct prop:
 * mirrored mounts share one, and a copy each would instantiate the same wreck twice.
 */
export interface PreviewBreakoffProp {
    id: string;
    modelRef?: string | null;
    /** False when the prop is named but not defined - worth surfacing, so it is still listed. */
    resolved: boolean;
    movementVector?: PreviewVector3 | null;
    facingRotateVector?: PreviewVector3 | null;
    minLifetimeSeconds?: number | null;
    maxLifetimeSeconds?: number | null;
    attachedParticle?: string | null;
    deathExplosions?: string | null;
    removeUponDeath: boolean;
    /** The clips of the PROP'S OWN model. See {@link PreviewDeathClone.animations}. */
    animations: string[];
    /**
     * The proxies the prop's own model carries - a burning piece of debris trails its own fire, and
     * `attachedParticle` is a second, separate effect the XML names.
     * See {@link PreviewDeathClone.particles} for how to read their `partId`.
     */
    particles: PreviewParticle[];
}

/** How far a shot at one target category may stray from the aim point. */
export interface PreviewInaccuracy {
    /**
     * The engine's own target bucket - Fighter, Bomber, Transport, Corvette, Frigate, Capital and
     * Super on the space side; Infantry, Vehicle and Structure on the land side.
     */
    category: string;
    distance: number;
}

/** A turret hardpoint's rest pose and how far it may swing. Absent when it is not a turret. */
export interface PreviewTurret {
    restAngle?: number | null;
    rotateExtentDegrees?: number | null;
    elevateExtentDegrees?: number | null;
    turretBone?: string | null;
    barrelBone?: string | null;
}

/**
 * One mounted hardpoint, with everything needed to destroy and repair it locally.
 *
 * The damage chain, in order: hide {@link partId}, show the proxies parented to
 * {@link damageParticlesBone} on the hull, show the {@link damageDecalBone} mesh, play
 * {@link deathExplosionParticles} once, and hide the engine glow when
 * {@link engineDeathHidesEngineParticles} is set. Every one is read from the hardpoint's own tags.
 */
export interface PreviewHardpoint {
    id: string;
    /** The part this hardpoint attaches, or absent when it attaches no model. */
    partId?: string | null;
    type?: string | null;
    attachBone?: string | null;
    isDestroyable: boolean;
    /** Whether the game lets a player target this mount. Decides whether a reticle is drawn. */
    isTargetable: boolean;
    health?: number | null;
    damageParticlesBone?: string | null;
    damageDecalBone?: string | null;
    collisionMeshBone?: string | null;
    engineParticlesBone?: string | null;
    deathExplosionParticles?: string | null;
    deathBreakoffProp?: string | null;
    engineDeathHidesEngineParticles: boolean;
    tooltipText?: string | null;
    turret?: PreviewTurret | null;
}

/**
 * A faction's colours.
 *
 * `color` is the team tint - a SKIRMISH thing, and the one fed to the `Colorization` shader
 * parameter when a faction colour applies. `noColorizationColor` is the faction's fallback for when
 * none does, the same value in the same parameter rather than a second mechanism: the effects
 * declare exactly one colourisation uniform. An OBJECT may override it with its own - see
 * {@link PreviewScene.noColorizationColor}, which is what the preview reads.
 */
export interface PreviewFaction {
    name: string;
    color?: PreviewRgba | null;
    noColorizationColor?: PreviewRgba | null;
    displayFontColor?: PreviewRgba | null;
}

/** Something the author should see about this scene. */
/**
 * A camera a model carries in its own skeleton, in MODEL space.
 *
 * Model space, not the viewport's: the exporter turns Alamo's Z-up into glTF's Y-up with a rotation
 * on the root node. The client places the shot from the camera's own BONE rather than from these
 * numbers, so it reuses the transform the geometry already went through and cannot drift out of
 * step with it - these are what the reader is shown and what a script would use.
 */
export interface PreviewCamera {
    /** The bone's name as the file spells it. Quotable: `Get_Bone_Position` takes exactly this. */
    name: string;
    position: number[];
    target: number[];
}

export interface PreviewProblem {
    /** `error`, `warning` or `info`. */
    severity: string;
    message: string;
    hardpointId?: string | null;
}

/**
 * Which layers the server can resolve assets from, and why one is missing.
 *
 * `explanation` is written to be shown verbatim: the difference between "your model is missing" and
 * "you never told the extension where the game is" is the whole point of reporting this.
 */
export interface PreviewAssetTiers {
    workspaceRootCount: number;
    hasBaseGamePath: boolean;
    hasExpansionPath: boolean;
    archiveCount: number;
}

/**
 * Everything needed to draw a preview, with no binary payload.
 *
 * Walk it, fetch each distinct model once, and drive every stateful behaviour - ALT/LOD, hardpoint
 * destruction, faction tint, particle toggles - locally. Nothing round-trips when a slider moves.
 */
/**
 * One particle system hanging off a bone of a loaded part.
 *
 * A model carries its effects as proxies - a named particle system pinned to a bone. Nothing in the
 * XML names a proxy: the hardpoint names a HULL bone through `Damage_Particles`, and the smoke is
 * every proxy whose bone's PARENT is that bone. The server does that join and reports the result.
 */
export interface PreviewParticle {
    /** Unique within the scene. Proxy names repeat - a Star Destroyer has twenty of one name. */
    id: string;
    /** The system to fetch. Many entries share one, so fetch by name and instantiate per entry. */
    systemRef: string;
    partId: string;
    bone: string;
    /**
     * The bone's index in the model.
     *
     * Names repeat: Boba Fett has two `p_boba_jetpack` bones, and the client keys its own bones by
     * name, so with a name alone both jets' effects landed on whichever bone won that map - he
     * fired from one.
     */
    boneIndex: number;
    /** See {@link PREVIEW_PARTICLE_GATE}. */
    gate: string;
    hardpointId?: string | null;
    /** The proxy's own flag. Some effects ship switched off. */
    startsVisible: boolean;
    /**
     * Damage state this effect belongs to, or null when untagged and therefore always shown.
     *
     * Read off the proxy NAME's `_ALT<n>` suffix, same as meshes.
     */
    alt?: number | null;
    /** Detail level, same convention as {@link alt}. */
    lod?: number | null;
    /**
     * Keeps the effect hidden while the damage state is winding DOWN - the repair asymmetry, so fire
     * lit on the way to destruction does not flicker back on as a hardpoint is repaired.
     */
    altDecreaseStayHidden?: boolean;
}

export interface PreviewScene {
    /** See {@link PREVIEW_SCENE_KIND}. */
    kind: string;
    subject: string;
    parts: PreviewPart[];
    hardpoints: PreviewHardpoint[];
    /**
     * Every weapon on the subject, wherever it is declared - a mount or the unit itself.
     *
     * Never absent, may be empty: roughly half the hardpoints in the shipped trees are shield
     * generators, docking bays and the like, which carry no armament at all.
     */
    weapons: PreviewWeapon[];
    /** The wreckage the subject's hardpoints shed, one entry per distinct prop. */
    breakoffProps: PreviewBreakoffProp[];
    /** Targeting reticles for the hardpoint types this subject mounts. Absent for a bare model. */
    reticles?: PreviewReticles | null;
    /** The projectiles this subject's weapons name. Never absent, may be empty. */
    projectiles: PreviewProjectile[];
    /** What a hit on this subject has to get through. Absent for a bare model. */
    defence?: PreviewTargetDefence | null;
    /** The abilities this subject declares, in document order. Never absent, may be empty. */
    abilities: PreviewAbility[];
    /** What this subject leaves behind when it dies. Never absent, may be empty. */
    deathClones: PreviewDeathClone[];
    /**
     * The explosion the SUBJECT sets off when it dies - its own `Death_Explosions`.
     *
     * Not a hardpoint's and not a breakoff prop's; both of those travel on their own records. This
     * one goes off where the ship was.
     */
    deathExplosions?: string | null;
    /**
     * Every projectile the tree defines, by name.
     *
     * The attacker panel picks from ALL of them - you are building a weapon to fire AT the subject,
     * so its own armament is the wrong list. Names only: 212 in eaw, and resolving each through the
     * variant chain on every scene open would cost more than the list is worth.
     */
    projectileCatalog: string[];
    /**
     * The colour the subject wears when NO faction colour applies. Absent when it declares none.
     *
     * The usual case rather than the exception: faction colour is a SKIRMISH thing. 25 shipped
     * objects declare one and it is per-object - a TIE Fighter is `75,75,75` whoever owns it, an
     * indigenous Bantha is `128,101,79`, and ten write pure white, which is the identity for the
     * multiply and means "leave my texture alone".
     */
    noColorizationColor?: PreviewRgba | null;
    /**
     * The faction this subject belongs to, or absent when it names none.
     *
     * Whose `noColorizationColor` applies when the subject declares none of its own - which is 748
     * of the 772 shipped objects that name an affiliation. The FIRST of several: 29 tags name more
     * than one, and a unit cannot wear two fallback colours.
     */
    affiliation?: string | null;
    factions: PreviewFaction[];
    problems: PreviewProblem[];
    /** The cameras the subject's own model declares. Empty for the 87% that carry none. */
    cameras?: PreviewCamera[];

    /** The subject's `Type` tag, for binding a camera preset to what it IS. Absent for a model. */
    objectType?: string | null;

    /**
     * The subject's category tokens, already split out of its multi-valued `CategoryMask`.
     *
     * A list rather than the raw mask: `Vehicle | AntiInfantry | AntiVehicle` names three things at
     * once, so a rule tests membership rather than equality.
     */
    categories?: string[];
    tiers: PreviewAssetTiers;
    /** The particle systems the models carry, each pinned to a bone. Never absent, may be empty. */
    particles: PreviewParticle[];
    /**
     * The animation files that belong to this subject's model, by filename.
     *
     * Offered rather than verified: each is checked against the skeleton when it is loaded, and 50
     * of the shipped animations sit beside a model they were not authored against. The client asks
     * for these when it fetches the GLB - before this existed it asked for none, so a model preview
     * had no clips at all while opening the `.ala` itself played perfectly well.
     */
    animations: string[];
    /**
     * The model whose animation set applies, when it is not the hull's own.
     *
     * `Land_Model_Anim_Override_Name` and its space counterpart hand a unit another model's
     * animations, which the engine can only do because the two skeletons are identical. The clips are
     * therefore named after the OVERRIDE model - look for them under this name, not the hull's, or a
     * unit that animates in game will appear to have no animations at all.
     */
    animationSource?: string | null;
}

// ── aet/getPreviewScene ──────────────────────────────────────────────────────

/** Exactly one is expected; they are tried in the order declared here. */
export interface GetPreviewSceneParams {
    objectId?: string;
    /**
     * An `.ala` the user opened. The server finds the model it drives and returns that scene - an
     * animation names no model, so the pairing is recovered from the filename and then verified
     * against the skeleton, because a shipped animation can sit beside one it does not belong to.
     */
    animationReference?: string;
    /** A model reference as the XML writes it, or the URI of an `.alo` the user opened. */
    modelReference?: string;
}

export interface GetPreviewSceneResult {
    /** Never absent - a subject that did not resolve still yields a scene carrying the reason. */
    scene: PreviewScene;
}

// ── aet/getModelGlb ──────────────────────────────────────────────────────────

export interface GetModelGlbParams {
    modelReference: string;
    /**
     * Clips to bake in, by file name. Baked rather than fetched separately because glTF ties a clip
     * to the skeleton it drives, and splitting them would mean rebuilding that association here.
     */
    animations?: string[];
}

export interface GetModelGlbResult {
    /** Base64 GLB, or absent when the model did not resolve or would not parse. */
    glb?: string | null;
    /**
     * The clips actually baked in - shorter than requested when one did not match the skeleton,
     * which is ordinary: 50 shipped animations sit beside a model they were not authored against.
     */
    animations: string[];
    error?: string | null;
}

// ── aet/getModelDetail ───────────────────────────────────────────────────────
//
// Mirrors PG.StarWarsGame.LSP.Server/Preview/PreviewModelDetail.cs. Everything here is read off the
// FILE, in the file's own axes - which is the whole reason it cannot come from the glTF instead: the
// exporter rotates the model Z-up to Y-up and splits doubled-up bone/mesh nodes apart, so a matrix
// taken from the client's scene graph is one the author has never seen.

export interface GetModelDetailParams {
    modelReference: string;
}

export interface GetModelDetailResult {
    detail?: ModelDetail | null;
    error?: string | null;
}

export interface ModelDetail {
    /** Which model this describes, so a reply for a subject that has since changed can be dropped. */
    model: string;
    bones: BoneDetail[];
    meshes: MeshDetail[];
    proxies: ProxyDetail[];
    /**
     * How many lights the file carries, and nothing else about them.
     *
     * They are 3ds Max export residue - no shipped effect declares a point or spot uniform that
     * could consume one - so the count exists only so the panel can say they are ignored.
     */
    lightCount: number;
}

export interface BoneDetail {
    index: number;
    name: string;
    /** The parent's index, or -1 for a root. */
    parentIndex: number;
    visible: boolean;
    /** By name, never the ordinal. `Disable` for the overwhelming majority. */
    billboard: string;
    /** Sixteen floats, four rows of four, translation last. */
    relativeTransform: number[];
    absoluteTransform: number[];
}

export interface MeshDetail {
    /** Position in the file's mesh list - what `alamoMeshIndex` in the glTF extras joins to. */
    index: number;
    name: string;
    boneIndex: number;
    visible: boolean;
    collidable: boolean;
    /** Null when the mesh carries no tag, which is not the same as being pinned to level 0. */
    alt: number | null;
    lod: number | null;
    boundsMin: number[];
    boundsMax: number[];
    subMeshCount: number;
    vertexCount: number;
    triangleCount: number;
}

export interface ProxyDetail {
    name: string;
    boneIndex: number;
    visible: boolean;
    /** Whether it stays hidden when the damage state is REPAIRED rather than coming back. */
    altDecreaseStayHidden: boolean;
    alt: number | null;
    lod: number | null;
}

// ── aet/getSubMeshGeometry ───────────────────────────────────────────────────
//
// Mirrors PG.StarWarsGame.LSP.Server/Preview/PreviewSubMeshGeometry.cs. One page of one table:
// a single Star Destroyer sub-mesh is 3814 triangles, and nobody scrolls that - they look up a
// handful of rows, so a page with the total beside it answers the question without ever putting the
// whole buffer on the wire.

/** Which table a page is of. */
export type GeometryTable = 'vertices' | 'faces' | 'boneMapping';

export interface GetSubMeshGeometryParams {
    modelReference: string;
    meshIndex: number;
    subMeshIndex: number;
    table: GeometryTable;
    offset: number;
    /** Rows wanted. The server caps this, so asking for everything is not a way to get everything. */
    count: number;
}

export interface GetSubMeshGeometryResult {
    page?: SubMeshGeometryPage | null;
    error?: string | null;
}

export interface SubMeshGeometryPage {
    model: string;
    meshIndex: number;
    subMeshIndex: number;
    table: string;
    offset: number;
    totalVertices: number;
    totalFaces: number;
    /** Filled only when `table` is `vertices`; likewise for the other two. */
    vertices: VertexRow[];
    faces: FaceRow[];
    boneMapping: BoneMappingRow[];
}

export interface VertexRow {
    /** The vertex's real index, so a face row's numbers line up with these. */
    index: number;
    position: number[];
    normal: number[];
    texCoord0: number[];
    texCoord1: number[];
    /** Zero unless the vertex format binds them - which is the fact worth seeing, so it is not hidden. */
    tangent: number[];
    binormal: number[];
    color: number[];
    boneIndices: number[];
    boneWeights: number[];
}

export interface FaceRow {
    index: number;
    v0: number;
    v1: number;
    v2: number;
}

export interface BoneMappingRow {
    /** The local slot a vertex's bone index names. */
    slot: number;
    boneIndex: number;
    name: string;
}

// ── aet/getParticleSystem ────────────────────────────────────────────────────
//
// Mirrors PG.StarWarsGame.LSP.Assets/Models/AlamoParticleContent.cs. The emitter description travels
// whole and is simulated in the client; per-frame particle state over the wire is not a trade worth
// making for a preview.

/** Mirrors the C# `AlamoSpawnShape`. */
export type AlamoSpawnShape = 'Point' | 'Box' | 'Cube' | 'Sphere' | 'Cylinder';

/** Mirrors the C# `AlamoTrackInterpolation`. */
export type AlamoTrackInterpolation = 'Linear' | 'Smooth' | 'Step';

/** Mirrors the C# `AlamoTrackChannel`. */
export type AlamoTrackChannel =
    | 'Red' | 'Green' | 'Blue' | 'Alpha' | 'Scale' | 'TextureIndex' | 'RotationSpeed';

/** Mirrors the C# `AlamoParticleBlendMode`. */
export type AlamoParticleBlendMode =
    | 'None' | 'Additive' | 'Transparent' | 'Inverse'
    | 'DepthAdditive' | 'DepthTransparent' | 'DepthInverse' | 'DiffuseTransparent'
    | 'StencilDarken' | 'StencilDarkenBlur' | 'Heat' | 'Bump' | 'DecalBump' | 'Scanlines';

export type AlamoGroundBehavior = 'None' | 'Disappear' | 'Bounce' | 'Stick';
export type AlamoEmitFromMesh = 'Disabled' | 'RandomVertex' | 'RandomMesh' | 'EveryVertex';

export interface AlamoVector3 { x: number; y: number; z: number }
export interface AlamoVector4 { x: number; y: number; z: number; w: number }

/**
 * A randomised emitter property.
 *
 * One fixed record holds every shape's fields whatever the shape is, so only those the shape uses
 * mean anything - reading `sphereRadius` on a Box tells you nothing about the Box.
 */
export interface AlamoSpawnVolume {
    shape: AlamoSpawnShape;
    min: AlamoVector3;
    max: AlamoVector3;
    sideLength: number;
    sphereRadius: number;
    sphereEdgeOnly: boolean;
    cylinderRadius: number;
    cylinderEdgeOnly: boolean;
    cylinderHeight: number;
    exactValue: AlamoVector3;
}

export interface AlamoTrackKey {
    /** Normalised over the particle's life: 0 at birth, 1 at death. */
    time: number;
    value: number;
}

/** A curve over a particle's lifetime, endpoints already folded into the key list. */
export interface AlamoTrack {
    channel: AlamoTrackChannel;
    interpolation: AlamoTrackInterpolation;
    keys: AlamoTrackKey[];
}

/**
 * One emitter's scalar settings.
 *
 * Every value is present: the server fills the engine's own defaults for anything the file omitted,
 * because the exporter writes only what was changed and a zero lifetime draws nothing.
 */
export interface AlamoEmitterProperties {
    blendMode: AlamoParticleBlendMode;
    triangleCount: number;
    useBursts: boolean;
    linkToSystem: boolean;
    inwardSpeed: number;
    acceleration: AlamoVector3;
    inwardAcceleration: number;
    gravity: number;
    lifetime: number;
    /** The atlas FRAME's edge in pixels, not the sheet's. */
    textureSize: number;
    randomScalePercent: number;
    randomLifetimePercent: number;
    randomRotationVariance: number;
    randomRotationDirection: boolean;
    initialDelay: number;
    burstDelay: number;
    particlesPerBurst: number;
    /** Zero means bursts forever. */
    burstCount: number;
    parentLinkStrength: number;
    particlesPerSecond: number;
    randomColors: AlamoVector4;
    colorAddGrayscale: boolean;
    worldOriented: boolean;
    groundBehavior: AlamoGroundBehavior;
    bounciness: number;
    affectedByWind: boolean;
    freezeTime: number;
    skipTime: number;
    emitFromMesh: AlamoEmitFromMesh;
    objectSpaceAcceleration: boolean;
    isHeatParticle: boolean;
    emitFromMeshOffset: number;
    isWeatherParticle: boolean;
    weatherCubeSize: number;
    weatherFadeoutDistance: number;
    hasTail: boolean;
    tailSize: number;
    noDepthTest: boolean;
    weatherCubeDistance: number;
    randomRotation: boolean;
    /** Derived from the rotation track when `randomRotation` is set. */
    randomRotationAverage: number;
}

/**
 * One emitter.
 *
 * `spawnOnDeath` and `spawnDuringLife` index other emitters of the same system, or are -1. They are
 * how a chained effect works - an explosion starting its own smoke.
 */
export interface AlamoEmitter {
    name: string;
    colorTexture: string;
    normalTexture?: string | null;
    speed: AlamoSpawnVolume;
    lifetime: AlamoSpawnVolume;
    position: AlamoSpawnVolume;
    tracks: AlamoTrack[];
    spawnOnDeath: number;
    spawnDuringLife: number;
    properties: AlamoEmitterProperties;
}

export interface AlamoParticleContent {
    name: string;
    leaveParticles: boolean;
    emitters: AlamoEmitter[];
}

export interface GetParticleSystemParams {
    /** The system name as a model proxy writes it; the extension is optional. */
    name: string;
}

export interface GetParticleSystemResult {
    system?: AlamoParticleContent | null;
    /**
     * The owning object's `Scale_Factor`, a uniform render scale. Absent or 1 for a bare asset name.
     *
     * `DatabaseMapExport.xml` lists it on the base GameObjectType beside `Mass` and `LOD_Bias`, and
     * the reference applies it as a uniform scale on the object's world matrix - so it scales where
     * a particle spawns as well as how big it draws. Six shipped particle objects declare one: 20.0
     * on the four hero powerup effects, 2.0 on the two bombing-run explosions.
     */
    scaleFactor?: number | null;
    error?: string | null;
}

// ── aet/getModelTexture ──────────────────────────────────────────────────────

// ── aet/getShaderSource ──────────────────────────────────────────────────────

export interface GetShaderSourceParams {
    /** The shader's bare file name, e.g. `MeshBump.fx`. The server refuses anything with a path. */
    name: string;
}

export interface GetShaderSourceResult {
    /** The shader text, or absent when no tier has it - a normal answer, not a failure. */
    source?: string | null;
    /** Whether a managed copy of the base shaders is reachable, so the UI can explain a plain look. */
    hasManagedShaders: boolean;
    error?: string | null;
}

export interface GetModelTextureParams {
    name: string;
}

/**
 * A texture, undecoded.
 *
 * `format` is the ACTUAL extension found, which is frequently not the one asked for - the engine
 * treats `.tga` and `.dds` as one asset and most shipped models reference a `.tga` name for a file
 * that only ever ships as `.dds`. Pick the decoder from this, never from the requested name.
 */
export interface GetModelTextureResult {
    /** `dds` or `tga`, without the dot. */
    format?: string | null;
    /** Base64 file bytes, or absent when the texture did not resolve. */
    data?: string | null;
    error?: string | null;
}

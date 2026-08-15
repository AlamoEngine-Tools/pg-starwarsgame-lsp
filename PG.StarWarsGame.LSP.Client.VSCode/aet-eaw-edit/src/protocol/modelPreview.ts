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

/**
 * A hardpoint's firing arc.
 *
 * Every measurement is nullable and absent means *not declared* - which is NOT the same as zero. A
 * capital ship's arcs reach 2000 units, so the client draws these on demand rather than by default.
 */
export interface PreviewFireArc {
    bones: string[];
    coneWidthDegrees?: number | null;
    coneHeightDegrees?: number | null;
    range?: number | null;
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
    fire?: PreviewFireArc | null;
}

/**
 * A faction's colours.
 *
 * `color` is the team tint, applied to the `Colorization` shader parameter and `FC_`-prefixed
 * meshes. `noColorizationColor` is a different mechanism - it blends into white pixels of the hull
 * texture's alpha channel - which is why both travel.
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

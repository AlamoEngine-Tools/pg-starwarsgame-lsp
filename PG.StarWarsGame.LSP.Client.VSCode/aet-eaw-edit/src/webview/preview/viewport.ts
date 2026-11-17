// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The three.js side of the preview, kept imperative and apart from React.
//
// Mixing a render loop into a component means every state change risks tearing down a GPU context,
// and the two lifecycles do not line up: React re-renders on a checkbox, the scene should not. The
// component owns a canvas and calls into this; nothing here knows React exists.

import * as THREE from "three";
import { GLTFLoader } from "three/examples/jsm/loaders/GLTFLoader.js";
import { OrbitControls } from "three/examples/jsm/controls/OrbitControls.js";
import * as l10n from "@vscode/l10n";

import {
    type BoundingSphere,
    clipPlanes,
    DEFAULT_AZIMUTH_DEGREES,
    DEFAULT_ELEVATION_DEGREES,
    frameSphere,
} from "./framing";
import type {
    AlamoParticleContent,
    PreviewBreakoffProp,
    PreviewSpinAway,
    PreviewTurret,
} from "../../protocol/modelPreview";
import { spinAwayPose } from "./spinAway";
import type { NormalisedColour } from "./colour";
import { attachmentProblem, type AttachmentRequest } from "./attachments";
import {
    drawnConeRange,
    fireConeOutline,
    fireConeSurface,
    hullFrameBasis,
} from "./fireArc";
import type { FxMaterialState } from "./fx/renderState";
import {
    costByLevel,
    definedLevels,
    type DefinedLevels,
    type LevelCost,
    type LevelTagged,
    proxyVisibleAt,
} from "./levels";
import { AlamoMaterial } from "./alamoMaterial";
import type { TranslatedEffect } from "./fx/effect";
import {
    type AlamoFrame,
    engineLight,
    type ProbeLight,
    sphericalHarmonics,
} from "./fx/uniforms";
import {
    castsShadowMap,
    debugColour,
    isShieldMesh,
    isStealthMesh,
    isVisibleAt,
    isVisibleAtLevel,
    type MaterialExtras,
    type MaterialSpec,
    resolveMaterial,
    shieldMeshOffShader,
} from "./materials";
import {
    DEFAULT_LIGHT_AZIMUTH_DEGREES,
    DEFAULT_LIGHT_ELEVATION_DEGREES,
    lightDirection,
    shadowFrustum,
    type Vector3 as Vector3Like,
} from "./lighting";
import {
    cancelRootCorrection,
    outermostRoots,
    splitGeometryFromBone,
    stampTreeKeys,
    treeKeyOf,
} from "./boneNodes";
import { captureFrame } from "./capture";
import { meshDrawn, setMeshDrawn } from "./meshVisibility";
import {
    boneRowId,
    effectPlacement,
    meshPlacement,
    type TreeItem,
} from "./previewTree";
import { healthColour, type ReticleMark, reticleSizePx } from "./reticles";
import {
    type BreakoffAnchor,
    breakoffLifetime,
    breakoffPose,
} from "./breakoff";
import { sweepAngles } from "./deathClone";
import {
    dragToAim,
    TURRET_AT_REST,
    type TurretAim,
    type TurretAxis,
    type TurretAxisRange,
    type TurretHandle,
} from "./turretHandles";
import {
    knobPoint,
    stopTicks,
    trackRadius,
    trackSegments,
} from "./turretTrack";
import { type Inspection } from "./inspector";
import { emissionMeshFor } from "./emissionSource";
import {
    billboardLocalRotation,
    billboardRotation,
    billboardTypeOf,
} from "./billboards";
import {
    hiddenAt,
    type VisibilityTrack,
    visibilityTracks,
} from "./boneVisibility";
import { skinHiddenByClip } from "./skinVisibility";
import { drawnBounds } from "./modelBounds";
import { HeatPass } from "./heatPass";
import { BloomPass } from "./bloomPass";
import { ShadowVolumePass } from "./shadowVolumePass";
import {
    applyBaseMapColourSpace,
    applyBlend,
    applyColourScale,
    applyDepth,
    shadowCatcherOpacity,
} from "./materialState";
import { type BoneId, resolveBoneId } from "./boneIds";
import { type AbilityOwnership } from "./abilityRows";
import { listedInModelTree, type ParticleOrigin } from "./particleScene";
import {
    damageMeshFacts,
    deathClip,
    effectFacts as effectRowFacts,
    type OverrideChange,
    type Resolution,
    resolveRow,
    restingClip,
    type RowFacts,
    type RowOverride,
    subtreeFacts,
} from "./visibility";
import { boxRoots, boxTargetFor, rowsInBox } from "./selectionBox";
import { skyGradient, starPositions } from "./backdrop";
import { concreteNoise, GROUND_TEXTURE_SIZE } from "./groundTexture";
import {
    type BackgroundKind,
    DEFAULT_LIGHTS,
    DEFAULT_VIEWER_SETTINGS,
    type LightRig,
    type Wind,
} from "../../shared/viewerSettings";
import {
    type EmitterProbe,
    HEAT_LAYER,
    ParticleSystemInstance,
} from "./particleRenderer";
import {
    alamoBoneName,
    alamoNodeName,
    type BoneAttachment,
    type FlatBone,
    labelCandidates,
    type LabelMode,
} from "./skeleton";
import { OverlayLayer } from "./overlayLayer";
import { ACTIVE_SUBJECT, type LoadedPart } from "./loadedPart";
import { type ParticleEntry } from "./particleEntry";

export { ACTIVE_SUBJECT };
import { VIEWPORT_BACKGROUND } from "./backdrop";
export { VIEWPORT_BACKGROUND };
import { TurretRig } from "./turretRig";
import { ParticleHost } from "./particleHost";
import { MaterialBinder } from "./materialBinder";
import { EnvironmentRig } from "./environmentRig";

/**
 * Where a system being loaded goes, and what it is.
 *
 * One object rather than six trailing positional parameters. Two of them were already being passed
 * as `undefined, undefined` at half the call sites to reach the one after, which is the point at
 * which a signature stops saying anything - and `origin`, the one field a caller must not get
 * wrong, would have been the eighth.
 */
export interface ParticlePlacement {
    /** Whether the MODEL declares this system, or the Gameplay lens asked for it by name. */
    origin: ParticleOrigin;
    attachToPartId?: string;
    attachBone?: string;
    /** The bone's index, which is what tells two same-named bones apart. */
    attachBoneIndex?: number;
    /** The ALT/LOD tags on the proxy. Untagged - every level - when omitted. */
    levels?: LevelTagged;
    /**
     * The owning object's `Scale_Factor`, a uniform render scale on the whole system.
     *
     * Only a `<Particle>` GAME OBJECT can carry one; a model's proxy names the asset directly and
     * gets 1. Six shipped objects declare one - 20.0 on the four hero powerup effects, 2.0 on the
     * two bombing-run explosions - and nothing read it, so all six drew far too small.
     */
    scaleFactor?: number;

    /**
     * Where to put it, in model space, for a system that names no part to hang on.
     *
     * A part-less system otherwise sits at the MODEL ROOT, which is right for a ship's own death
     * blast - it goes off where the ship was - and wrong for a wreck that flew 270 units away first.
     * Attaching it to the wreck instead is not the answer either: the wreck is hidden in the same
     * breath and three prunes a hidden subtree, effects included.
     */
    at?: { x: number; y: number; z: number };
}

/**
 * The scene node an effect is described by: the bone it is anchored on.
 *
 * NOT the system's own root, which is attached to the simulation space and so is owned by no bone at
 * all - see `ParticleSystemInstance.attachTo`. Falls back to the root for a system with no anchor,
 * which is a particle file opened on its own.
 */
function anchorNodeOf(entry: ParticleEntry): THREE.Object3D {
    return entry.instance.anchorNode ?? entry.instance.root;
}

/** Vertical field of view, in degrees. Narrow enough to keep perspective distortion off a long hull. */
const FIELD_OF_VIEW = 50;

/**
 * How many triangles one geometry draws.
 *
 * An indexed geometry draws its INDEX buffer, not its vertices - reading the vertex count would
 * under-report every shipped mesh, all of which are indexed.
 */
function triangleCount(geometry: THREE.BufferGeometry): number {
    const vertices = geometry.getAttribute("position")?.count ?? 0;

    return Math.floor((geometry.getIndex()?.count ?? vertices) / 3);
}

/** What the stats readout shows. */
export interface ViewportStats {
    parts: number;
    meshes: number;
    triangles: number;
    bones: number;
    animations: string[];
}

/** A named camera angle the toolbar can jump to. */
export type PresetView = "front" | "side" | "top" | "threeQuarter";

const PRESETS: Record<PresetView, { azimuth: number; elevation: number }> = {
    front: { azimuth: 0, elevation: 0 },
    side: { azimuth: 90, elevation: 0 },
    top: { azimuth: 0, elevation: 89.9 },
    threeQuarter: {
        azimuth: DEFAULT_AZIMUTH_DEGREES,
        elevation: DEFAULT_ELEVATION_DEGREES,
    },
};

/** Anything about a loaded model beyond the bone it is attached to. */
export interface AddPartOptions {
    /** Which subject the geometry belongs to. The active one when not given. */
    subjectId?: string;
    /**
     * A world pose to drop the model at, instead of parenting it to a bone.
     *
     * Wreckage stops being attached to the ship the moment it breaks off, which is the whole point
     * of it - so it captures where its hardpoint WAS and is placed there under the model root.
     */
    pose?: { position: THREE.Vector3; quaternion: THREE.Quaternion };
    /**
     * The model's OWN render scale, for a part that is its own object - a death clone.
     *
     * Such a part is placed beside the subject's scaled geometry, not inside it, and carries this
     * instead: it is spawned as a separate object, so the engine draws it at its own
     * `Scale_Factor` and nothing relates that to the ship's.
     */
    scale?: number;
}

/** A replacement subject's own death clip, and what it says about visibility. */
/**
 * One subject's animation, kept entirely apart from every other subject's.
 *
 * Nothing here is scene-wide, and that is the point: a name is unique WITHIN a model - the exporter
 * suffixes every bone with its index - and unique nowhere else. A death clone is the same model as
 * the ship it replaces, so a scene-wide map of any of these is a map in which the hull holds every
 * key and the clone holds none.
 */
interface SubjectAnimation {
    /**
     * Rooted at the first of this subject's parts to bring clips.
     *
     * A clip binds to nodes BY NAME from its mixer's root, so a mixer rooted at the hull will drive
     * the hull whatever clip is fed to it.
     */
    mixer: THREE.AnimationMixer | null;
    clips: THREE.AnimationClip[];
    /**
     * Which bones each of this subject's clips hides, and on which frames.
     *
     * Filled from the glTF extras as parts arrive. Alamo animations switch bones off per frame and
     * half the shipped corpus does it - mostly to time a particle effect to the motion - so without
     * this every effect on a model fires from frame zero of every clip.
     */
    visibility: Map<string, VisibilityTrack>;
    /**
     * This subject's nodes by their glTF name, which is how a visibility track addresses a bone.
     *
     * First wins, which is safe now that it is scoped: a bone name is unique inside one model
     * because the exporter suffixes it with the bone index. Scene-wide it was not, and the hull
     * won every key.
     */
    nodeByName: Map<string, THREE.Object3D>;
    /**
     * Where every node sat when its part loaded - the pose the model file describes.
     *
     * Captured per part as it arrives, so a hardpoint that loads late is recorded too, and never
     * overwritten: the first sighting of a node is the only one taken while an animation may have
     * moved it since.
     */
    restPose: Map<
        THREE.Object3D,
        {
            position: THREE.Vector3;
            quaternion: THREE.Quaternion;
            scale: THREE.Vector3;
        }
    >;
    /** The clip running on this subject right now. */
    action: THREE.AnimationAction | null;
    /** Meshes the FILE hides. A clip never brings one of these back. */
    fileHidden: Set<THREE.Object3D>;
}

/** Every mesh a model's own materials say is not drawn - collision hulls, shadow volumes. */
function hiddenMeshes(root: THREE.Object3D): Set<THREE.Object3D> {
    const hidden = new Set<THREE.Object3D>();

    root.traverse((node) => {
        if (
            node instanceof THREE.Mesh &&
            resolveMaterial((node.userData.alamo ?? {}) as MaterialExtras)
                .hidden
        ) {
            hidden.add(node);
        }
    });

    return hidden;
}

/** One piece of wreckage in the air. */
interface BreakoffInstance {
    key: string;
    /** The loaded part carrying its geometry - a passive subject of its own. */
    partId: string;
    root: THREE.Object3D;
    prop: PreviewBreakoffProp;
    /** Where the hardpoint was when it broke off. The debris does not follow the ship. */
    origin: THREE.Vector3;
    facing: THREE.Quaternion;
    age: number;
    lifetime: number;
    /**
     * Whether its time is up and its geometry has gone.
     *
     * The empty root stays in the scene: `Death_Explosions` fires where the piece FINISHED, not
     * where it broke off, and an empty node is the only thing that still knows where that was.
     */
    expired: boolean;
}

/**
 * The `partId` prefix a piece of wreckage is loaded and addressed under.
 *
 * Both its part id and its subject id, because a wreck is one model and one subject. Kept here
 * rather than shared with the client's own `breakoff:` request prefix on purpose: that one names a
 * pending GLB request, this one names geometry already in the scene, and the two happening to read
 * alike is not a reason to tie them together.
 */
export const BREAKOFF_ATTACHMENT = "wreck:";

/** A three vector as the plain shape the wire and the arithmetic use. */
function vectorOf(at: THREE.Vector3): { x: number; y: number; z: number } {
    return { x: at.x, y: at.y, z: at.z };
}

/** How close to a joint a click must land, in pixels, to count as selecting it. */
const JOINT_PICK_RADIUS = 10;

/** Scratch for the bone-palette maths, so a per-frame loop allocates nothing. */
const SKIN_WORK = new THREE.Matrix4();

const BILLBOARD_AT = new THREE.Vector3();
const BILLBOARD_LIGHT = new THREE.Vector3();
const BILLBOARD_BONE = new THREE.Quaternion();

export class PreviewViewport {
    /** Raised when a targeting mark is clicked, so the dock can follow the viewport. */
    onReticleClicked: ((hardpointId: string) => void) | null = null;
    /** Raised when a turret is dragged, so the row can read out where it now points. */
    onTurretAimed: ((id: string, aim: TurretAim) => void) | null = null;
    /** Raised when a joint is clicked, so the tree can follow the viewport. */
    onBoneSelected: ((index: number | null) => void) | null = null;
    /**
     * Raised the moment the reader drags the camera, so the preset readout can clear.
     *
     * A segmented control that keeps "Front" highlighted after you have orbited away from the front
     * is stating something false. The presets are a place to jump TO, not a mode the camera stays
     * in, and the control has to admit that as soon as the camera moves.
     */
    onCameraMoved: (() => void) | null = null;
    /**
     * Told when a piece of wreckage reaches the end of its declared lifetime.
     *
     * A callback rather than something polled: it is an EVENT, like a hardpoint's own death, and
     * the explosion it triggers has to be fetched by the client - the viewport has no way to ask
     * for a particle system itself.
     */
    onBreakoffExpired:
        ((key: string, prop: PreviewBreakoffProp) => void) | null = null;
    private readonly renderer: THREE.WebGLRenderer;
    private readonly scene = new THREE.Scene();
    private readonly camera: THREE.PerspectiveCamera;
    private readonly controls: OrbitControls;
    private readonly loader = new GLTFLoader();
    private readonly clock = new THREE.Clock();
    /** The room and the light: lights, sky and stars, grid, floor and shadows, and the pass chain that draws a frame. See `EnvironmentRig`. */
    private readonly environment: EnvironmentRig;
    /** The particle systems: what is attached where, what is fading out, and the step that runs them. See `ParticleHost`. */
    private readonly particles: ParticleHost;
    private readonly parts = new Map<string, LoadedPart>();
    private readonly modelRoot = new THREE.Group();
    /**
     * The subject's GEOMETRY, carrying the object's `Scale_Factor`.
     *
     * Between `modelRoot` and the parts rather than on `modelRoot` itself, and the split is the
     * engine's own. the engine's transform update puts no scale in the object's world matrix
     * and then calls `Model->the model-scale setter`, so the scale belongs to the MODEL: geometry and bones are
     * model units and reach the world multiplied by it, while positions - and every range the XML
     * declares - are already world units.
     *
     * What that buys is that the things which must NOT scale stay on `modelRoot` and need no
     * correction at all:
     *
     * - **particle systems**, whose own scale is the `<Particle>` object's, not the ship's. The
     *   engine divides the parent's out explicitly - `Attach_To_Other_Game_Object` builds
     *   `Scale(child / parent) * Translate(offset)` - so an attached effect ends at its own scale
     *   while its POSITION still rides the scaled bone. A root-level scale would also have been
     *   unfixable here: `frameOf` overwrites the instance root's matrix every frame.
     * - **breakoff wrecks**, posed at a world position they captured themselves, whose drift is a
     *   per-frame world velocity.
     * - **hull-frame fire arcs**, which take their position from a bone and their length from a
     *   world range.
     *
     * Only what is parented INSIDE this node and must stay a world length needs undoing: the
     * bone-frame arcs and the turret handles, both of which counter-scale by `1 / scale`.
     */
    private readonly scaledRoot = new THREE.Group();
    /** The subject's `Scale_Factor`, or 1. Read by everything that counter-scales. */
    private modelScale = 1;
    /** The materials: what every mesh is drawn with, the textures and effects bound to them, and the team tint. See `MaterialBinder`. */
    private readonly materials: MaterialBinder;
    /**
     * What one tree row controls.
     *
     * Explicit, because the alternative was inferring it from the row's id - and a merged row is
     * anchored on its BONE's id while standing for a mesh as well. Reading the mesh's state and
     * writing the bone's is what made a hidden mesh impossible to show again.
     */
    private readonly rowTargets = new Map<
        string,
        {
            boneIndex?: number;
            mesh?: THREE.Mesh;
            particleId?: string;
        }
    >();
    /**
     * Every bone a skinned mesh is weighted to, worked out once.
     *
     * Weak, so a mesh dropped when the subject changes takes its entry with it - this map is never
     * cleared by hand and must not be the thing that keeps a discarded model alive.
     */
    private readonly skinBoneIds = new WeakMap<THREE.Mesh, readonly BoneId[]>();
    /** The clip map {@link restingClipName} last answered for, and its answer. */
    private restingClipFor: Map<string, VisibilityTrack> | null = null;
    private restingClipCached: string | undefined;
    /** Each row's parent, so a hidden ancestor can veto without walking the scene graph. */
    private readonly rowParents = new Map<string, string | null>();
    /** Each row's display name, so `because` can say WHICH ancestor hid it. */
    private readonly rowNames = new Map<string, string>();
    /** The reader's word, one per ROW. The only thing a tick writes. */
    private readonly rowOverrides = new Map<string, RowOverride>();
    /** The last resolution per row, so the tree can report why a row looks as it does. */
    private rowResolutions = new Map<string, Resolution>();
    /** The same, for what each row does to everything BENEATH it - see `subtreeFacts`. */
    private rowSubtrees = new Map<string, Resolution>();
    /** Seconds since the viewport opened, for the effects that read the clock. */
    private elapsed = 0;
    /** Reused per frame so building the bone palette does not allocate on every draw. */
    private skinScratch = new Float32Array(0);
    /**
     * Every subject's animation state, keyed by subject id.
     *
     * These five things used to be one scene-wide set of maps, and every one of them was
     * "first wins" - which the hull always was. A death clone is the SAME MODEL as the ship it
     * replaces, so every node name, every clip name and every track name collided with the hull's:
     * a clone's `_die` played on the shared mixer drove the HULL's bones, and its visibility track
     * registered under the shared map hid the HULL's meshes. That collision is the whole reason a
     * second, parallel clip path existed. Per subject, the names are unique again and there is one
     * path.
     */
    private readonly subjects = new Map<string, SubjectAnimation>();
    // panel and then picking a different clip used to silently drop it.
    private animationSpeed = 1;
    private animationLoop = true;
    private animationPaused = false;
    /** Whether a clip is currently driving bone visibility, so the release happens exactly once. */
    private overridingVisibility = false;
    private frameHandle = 0;
    private disposed = false;
    /** Bone nodes whose geometry turns to face the camera, the light or the wind. */
    private billboards: THREE.Object3D[] = [];
    /** How far the camera stands off, kept so the depth range can be redone without reframing. */
    private fitDistance = 1;
    /** The reader's own multiplier on the fitted far plane. */
    private drawDistance = 1;
    private alt = 0;
    private lod = 0;
    /** Labels, reticles, selection boxes, the skeleton and the bone axes. See `OverlayLayer`. */
    private readonly overlay: OverlayLayer;
    /** The firing arcs, the turret handles, the sweeps and the hand-set aims. See `TurretRig`. */
    private readonly turret: TurretRig;
    /**
     * What each tree row stands for, so the inspector can describe it.
     *
     * `boneIndex` means the row IS that bone; `ownerBone` means the row is a mesh that rides one.
     * A merged row carries both a bone and a mesh, and gets a panel describing each.
     */
    private readonly inspectSources = new Map<
        string,
        {
            mesh?: THREE.Mesh;
            boneIndex?: number;
            ownerBone?: number;
            systemId?: string;
        }
    >();
    /**
     * Whether the last ALT change was downwards.
     *
     * A property of the transition, not of the state - the engine recomputes it on every SetALT and
     * passes false for LOD changes.
     */
    private altDescending = false;
    /**
     * Damage decals, lowercased.
     *
     * `knownDecals` is every mesh some hardpoint names through `Damage_Decal`; `shownDecals` is the
     * subset whose hardpoint is destroyed. Without the first, a level change would re-show a decal
     * the model ships visible and every capital ship would open pre-scorched.
     */
    private knownDecals: ReadonlySet<string> = new Set();
    private shownDecals: ReadonlySet<string> = new Set();
    /**
     * Meshes a hardpoint claims as its collision hull.
     *
     * Kept beside the decals because they are the same kind of fact - hardpoint XML deciding what a
     * mesh IS - and settled in the same pass of the chain. Unlike a decal there is no "shown" half:
     * the engine never draws one, whatever the damage state.
     */
    private collisionMeshes: ReadonlySet<string> = new Set();
    /**
     * Whether an active ability is holding the SHIELD MESH visible.
     *
     * The mesh ships hidden - `MeshShield.fx` carries `alamoHidden: true` - so this is the model
     * being told it does draw it after all, which is why it flips `inFile` rather than adding a
     * gate. The reader's own tick still sits above it.
     */
    private shieldRevealed = false;
    /**
     * Whether a cloak ability is engaged, in which case the STEALTH SHELL is the only thing drawn.
     *
     * A swap, not a reveal, and that is what makes it different from the shield above. The shell
     * ships VISIBLE on all nine models that carry one, so with nothing to hide it the preview drew
     * the cloak over the hull permanently - the see-through Vengeance. And when the cloak IS on,
     * the hull, its effects and its shadow all go, because there is nothing else to see.
     *
     * False is the permanent answer for a unit that declares no cloak, so this one flag is also
     * what keeps the shell off the models whose object never declares the ability.
     */
    private stealthed = false;
    /** See {@link modelSpan}. Cleared with the scene. */
    private cachedSpan: number | null = null;
    /** Wreckage currently in the air, each on its own clock. */
    private readonly breakoffs: BreakoffInstance[] = [];
    /** Every named attachment a caller has asked for, so {@link unresolvedAttachments} can check. */
    private readonly attachRequests = new Map<string, AttachmentRequest>();
    /** Parts broken off by a destroyed hardpoint. */
    private readonly hiddenParts = new Set<string>();
    /**
     * The unit flying off and coming apart, for one that declares no death clone.
     *
     * Driven HERE rather than from the panel because it is a per-frame thing and this class already
     * owns the frame. The panel says start and is told when it is over.
     */
    private spinAway: {
        partId: string;
        spin: PreviewSpinAway;
        elapsed: number;
        onDone: () => void;
    } | null = null;
    /**
     * Where the flying part sat before it was pushed, so putting it back is exact.
     *
     * Held SEPARATELY from `spinAway` and outlives it. The spin ends the moment the wreck explodes,
     * but the part is still sitting out there where it finished - and clearing both together left
     * `clearSpinAway` with nothing to restore, so Repair brought the ship back visible at the far
     * end of its own death run, 270 units off the grid.
     */
    private spinRestore: {
        partId: string;
        position: THREE.Vector3;
        quaternion: THREE.Quaternion;
    } | null = null;

    constructor(
        private readonly canvas: HTMLCanvasElement,
        /** Absolutely-positioned container the bone labels are written into. */
        private readonly labelLayer: HTMLElement,
    ) {
        // `stencil` is NOT on by default in this three - it defaults to false, and without a
        // stencil buffer every stencil op is a silent no-op, so the shadow-volume count never
        // happens and the darken covers the entire frame instead of the shadow.
        this.renderer = new THREE.WebGLRenderer({
            canvas,
            antialias: true,
            alpha: true,
            stencil: true,
        });
        this.renderer.setPixelRatio(
            Math.min(globalThis.devicePixelRatio ?? 1, 2),
        );

        // The engine casts shadows with stencil volumes (Crow, 1977) and ships the volume geometry
        // in the model; we cast with a shadow map instead. Reproducing the stencil technique would
        // buy nothing here - the volumes are authored for a renderer we are not, and a depth map
        // gives a truer picture of the actual hull, which is what someone checking a model wants.
        this.renderer.shadowMap.enabled = true;
        this.renderer.shadowMap.type = THREE.PCFSoftShadowMap;

        this.camera = new THREE.PerspectiveCamera(FIELD_OF_VIEW, 1, 0.1, 10000);
        this.controls = new OrbitControls(this.camera, canvas);
        this.controls.enableDamping = true;
        this.controls.screenSpacePanning = true;
        // `start`, not `change`: `change` also fires for the framing this class does itself, which
        // would clear the preset the same frame it was applied.
        this.controls.addEventListener("start", () => this.onCameraMoved?.());

        this.scene.background = new THREE.Color(VIEWPORT_BACKGROUND);
        this.modelRoot.add(this.scaledRoot);
        this.scene.add(this.modelRoot);

        // Live getters, not copies: the parts, the rows and the hull change with every subject, and
        // the layer reads them at the moment it draws.
        const viewport = this;
        this.environment = new EnvironmentRig({
            get alt() {
                return viewport.alt;
            },
            get camera() {
                return viewport.camera;
            },
            get canvas() {
                return viewport.canvas;
            },
            get elapsed() {
                return viewport.elapsed;
            },
            get lod() {
                return viewport.lod;
            },
            get particles() {
                return viewport.particles;
            },
            get renderer() {
                return viewport.renderer;
            },
            get scene() {
                return viewport.scene;
            },
            get stealthed() {
                return viewport.stealthed;
            },
            boundingSphere: (includeEffects) =>
                viewport.boundingSphere(includeEffects),
            forEachDrawnMesh: (visit) => viewport.forEachDrawnMesh(visit),
            skinPalette: (mesh) => viewport.skinPalette(mesh),
        });

        this.overlay = new OverlayLayer(
            {
                get camera() {
                    return viewport.camera;
                },
                get canvas() {
                    return viewport.canvas;
                },
                get labelLayer() {
                    return viewport.labelLayer;
                },
                get parts() {
                    return viewport.parts;
                },
                get rowParents() {
                    return viewport.rowParents;
                },
                get rowTargets() {
                    return viewport.rowTargets;
                },
                get onReticleClicked() {
                    return viewport.onReticleClicked;
                },
                bonePosition: (partId, bone) =>
                    viewport.bonePosition(partId, bone),
                boundingSphere: (includeEffects) =>
                    viewport.boundingSphere(includeEffects),
                hull: () => viewport.hull(),
                modelSpan: () => viewport.modelSpan(),
                skeleton: () => viewport.skeleton(),
            },
            this.scene,
        );
        this.turret = new TurretRig({
            get camera() {
                return viewport.camera;
            },
            get canvas() {
                return viewport.canvas;
            },
            get modelRoot() {
                return viewport.modelRoot;
            },
            get modelScale() {
                return viewport.modelScale;
            },
            get parts() {
                return viewport.parts;
            },
            get onTurretAimed() {
                return viewport.onTurretAimed;
            },
            fireBoneAttachment: (partId, bone) =>
                viewport.fireBoneAttachment(partId, bone),
            modelSpan: () => viewport.modelSpan(),
        });
        this.materials = new MaterialBinder({
            get camera() {
                return viewport.camera;
            },
            get modelRoot() {
                return viewport.modelRoot;
            },
            get particles() {
                return viewport.particles;
            },
            get stencilShadowing() {
                return viewport.environment.stencilShadowing;
            },
            applyMeshVisibility: (mesh) => viewport.applyMeshVisibility(mesh),
            forEachDrawnMesh: (visit) => viewport.forEachDrawnMesh(visit),
            forEachPartMesh: (visit) => viewport.forEachPartMesh(visit),
            frameFor: (mesh, view, projection, viewProjection, eyeWorld) =>
                viewport.environment.frameFor(
                    mesh,
                    view,
                    projection,
                    viewProjection,
                    eyeWorld,
                ),
        });
        this.particles = new ParticleHost({
            get alt() {
                return viewport.alt;
            },
            get altDescending() {
                return viewport.altDescending;
            },
            get lod() {
                return viewport.lod;
            },
            get fitDistance() {
                return viewport.fitDistance;
            },
            get cameraPosition() {
                return viewport.cameraPosition;
            },
            get modelRoot() {
                return viewport.modelRoot;
            },
            get parts() {
                return viewport.parts;
            },
            get hiddenParts() {
                return viewport.hiddenParts;
            },
            get shaderStates() {
                return viewport.materials.shaderStates;
            },
            get translated() {
                return viewport.materials.translated;
            },
            get translatedShaders() {
                return viewport.materials.translatedShaders;
            },
            applyClipPlanes: (sphere, distance) =>
                viewport.applyClipPlanes(sphere, distance),
            attachmentFor: (partId, bone, boneIndex) =>
                viewport.attachmentFor(partId, bone, boneIndex),
            boundingSphere: (includeEffects) =>
                viewport.boundingSphere(includeEffects),
            partRootOf: (partId) => viewport.partRootOf(partId),
            refreshParticleVisibility: (entry) =>
                viewport.refreshParticleVisibility(entry),
            refreshRows: () => viewport.refreshRows(),
        });

        // CAPTURE phase, so a grabbed turret knob is claimed before `OrbitControls` - registered on
        // this canvas above - can start an orbit. See `TurretRig.grabKnob`.
        canvas.addEventListener("pointerdown", this.onPointerDown, {
            capture: true,
        });
        canvas.addEventListener("pointermove", this.turret.onPointerMove);
        // On the WINDOW, not the canvas: a drag that leaves the canvas must still end, and a
        // release outside it is the ordinary way a fast drag finishes.
        window.addEventListener("pointerup", this.turret.onPointerUp);
        window.addEventListener("pointercancel", this.turret.onPointerUp);

        this.resize();
        this.loop();
    }

    /** Where the camera stands now, for saving the current shot as a preset. */
    get cameraPosition(): Vector3Like {
        return {
            x: this.camera.position.x,
            y: this.camera.position.y,
            z: this.camera.position.z,
        };
    }

    /**
     * Every running particle system's emitters as their last frame left them, and where the camera
     * stands. For the UI tests, through the page's debug handle: whether a field is full, drifts,
     * stays frozen or is drawn back to front is a question about the simulation, not the pixels.
     */
    particleProbe(): {
        camera: Vector3Like;
        systems: { id: string; emitters: EmitterProbe[] }[];
    } {
        return this.particles.particleProbe();
    }

    /**
     * Renders one frame and counts the canvas pixels that differ from its dominant colour, for the UI
     * tests. With the heat Debug view on, the dominant colour is the neutral field and the rest is the
     * heat the camera sees - whether a hull in front of a haze hides it is a depth test on the GPU, so
     * the pixels are the only place to read it. Read in the same task as the render, so the drawing
     * buffer still holds the frame.
     */
    canvasCoverage(): { lit: number; total: number } {
        this.environment.renderFrame();
        const gl = this.renderer.getContext();
        const width = gl.drawingBufferWidth;
        const height = gl.drawingBufferHeight;
        const pixels = new Uint8Array(width * height * 4);
        gl.readPixels(0, 0, width, height, gl.RGBA, gl.UNSIGNED_BYTE, pixels);
        const counts = new Map<number, number>();
        for (let i = 0; i < pixels.length; i += 4) {
            const key =
                (pixels[i] << 16) | (pixels[i + 1] << 8) | pixels[i + 2];
            counts.set(key, (counts.get(key) ?? 0) + 1);
        }
        let dominant = 0;
        let most = -1;
        for (const [key, count] of counts) {
            if (count > most) {
                most = count;
                dominant = key;
            }
        }
        const [r, g, b] = [
            (dominant >> 16) & 255,
            (dominant >> 8) & 255,
            dominant & 255,
        ];
        let lit = 0;
        for (let i = 0; i < pixels.length; i += 4) {
            const departure =
                Math.abs(pixels[i] - r) +
                Math.abs(pixels[i + 1] - g) +
                Math.abs(pixels[i + 2] - b);
            if (departure > 3) {
                lit++;
            }
        }
        return { lit, total: width * height };
    }

    /** The bounding sphere a preset is resolved against, so the caller sizes the shot correctly. */
    get subjectSphere(): { center: Vector3Like; radius: number } {
        return this.boundingSphere();
    }

    get animationNames(): string[] {
        return this.active.clips.map((c) => c.name);
    }

    // ── content ───────────────────────────────────────────────────────────────

    /**
     * The subject the panel speaks for.
     *
     * Every animation control - the picker, the speed, the scrub bar, the loop switch - describes
     * the model that was opened, and only that one. A passive subject's clip is not something the
     * reader chose; it is what dying looks like, and it runs on its own terms.
     */
    private get active(): SubjectAnimation {
        return this.subjectAnimation(ACTIVE_SUBJECT);
    }

    /**
     * Moves the key light, in the same azimuth/elevation terms as the camera presets.
     *
     * The light drives the translated shaders too - they read it as DIR_LIGHT_VEC_0 - so this is one
     * dial for the shading and the shadow both, rather than a viewer-only conceit.
     */
    setLightAngles(azimuthDegrees: number, elevationDegrees: number): void {
        this.environment.setLightAngles(azimuthDegrees, elevationDegrees);
    }

    /**
     * Takes the whole rig: three directionals with their own colours, plus the global terms.
     *
     * The same shape AloViewer's settings dialog offers, because it is the engine's own - a sun and
     * two fills, each with a heading, a tilt, a colour and a brightness. Colour and brightness are
     * kept apart so a light can be dimmed without desaturating it.
     */
    setLightRig(rig: LightRig): void {
        this.environment.setLightRig(rig);
    }

    /**
     * Chooses what is behind the model.
     *
     * A flat grey is honest but makes a hull hard to judge; a starfield gives a space model
     * something to sit against and a sky does the same for a ground one. Both are BACKDROPS: never
     * lit, never in the depth buffer, and they follow the camera so they read as infinitely far.
     */
    setBackground(kind: BackgroundKind): void {
        this.environment.setBackground(kind);
    }

    /**
     * Sets the one wind the scene has.
     *
     * Read by the foliage shaders through their own semantics and by the 484 emitters that set
     * `affectedByWind`: an ordinary one gains half of it as speed every frame, a weather field
     * drifts with five times it - see `stepParticle` and the weather step.
     */
    setWind(headingDegrees: number, speed: number): void {
        this.particles.setWind(headingDegrees, speed);
    }

    /**
     * Sets the weather phase's intensity.
     *
     * Measured: a phase sets it as the energy of the weather system's group, so it reaches the
     * systems that hold a weather field and no other effect, which stays at 1.0.
     */
    setWeatherIntensity(intensity: number): void {
        this.particles.setWeatherIntensity(intensity);
    }

    /** Whether the scene holds a weather field, for the control that only reaches one. */
    hasWeather(): boolean {
        return this.particles.hasWeather();
    }

    /**
     * Adds a model, attached to a bone of an already-loaded part when asked.
     *
     * Attaching to the bone's Object3D rather than copying its transform is what makes an attached
     * turret follow the hull's animation for free.
     *
     * THE one loader. Every model in the scene comes through here - the hull, its hardpoint
     * models, a death clone, a piece of wreckage - because the alternative was measured and it is
     * a parallel pipeline that converges on this one a bug at a time. `addBreakoff` had hand
     * copies of six of the steps below, four of them added on a single day off user reports, and
     * was still missing nine others; the wreck cast no stencil shadow, had no tree keys, ignored
     * the detail levels and could not carry a clip. What differs between one model and the next is
     * which SUBJECT it belongs to, which is {@link AddPartOptions}, not which loader it uses.
     *
     * Hands back the root it built, for a caller that has its own state to keep beside it.
     */
    async addPart(
        id: string,
        glbBase64: string,
        attachToPartId?: string,
        attachBone?: string,
        options: AddPartOptions = {},
    ): Promise<THREE.Object3D> {
        const subjectId = options.subjectId ?? ACTIVE_SUBJECT;

        // Its own file's word decides how it is drawn, because the row chain speaks only for the
        // active subject and will never speak for this one.
        const passive = subjectId !== ACTIVE_SUBJECT;

        const gltf = await this.loader.parseAsync(decodeBase64(glbBase64), "");
        const root = gltf.scene;

        const bones = new Map<string, THREE.Object3D>();
        const bonesByIndex = new Map<number, THREE.Object3D>();

        // Pass one: who owns each bone index.
        //
        // `<name>#<n>` is TWO namespaces sharing one syntax: on a bone node `n` is the bone index,
        // on a mesh node of its own it is the SUB-MESH index. Neither exclusion works - a rigid
        // mesh is attached to the bone's own node, so that node really is both, while a skinned or
        // collision mesh gets a node of its own that is not a bone at all.
        //
        // So it is settled by PRECEDENCE: a node without geometry always wins the index, and one
        // with geometry only takes it if nothing better claims it. Letting a mesh overwrite a bone
        // made `Ei_trooper` report its root as `Storm_Trooper_LOD2` when bone 0 is `Root`, and
        // every bone count, parent and attachment was then read off the wrong object.
        root.traverse((node) => {
            const parsed = alamoBoneName(alamoNodeName(node));
            if (parsed === null) {
                return;
            }

            const held = bonesByIndex.get(parsed.index);
            const better =
                held === undefined ||
                (held instanceof THREE.Mesh && !(node instanceof THREE.Mesh));

            if (better) {
                bonesByIndex.set(parsed.index, node);

                const key = parsed.name.toLowerCase();
                if (!bones.has(key) || bones.get(key) === held) {
                    bones.set(key, node);
                }
            }
        });

        // Pass two: no bone node carries geometry from here on.
        //
        // Only the nodes that actually WON an index are split, which is why this waits for the
        // precedence pass - a skinned mesh's name parses as a bone name too (`Storm_Trooper_LOD1#0`
        // claims bone 0) and splitting one of those would manufacture a second node competing for
        // an index the real `Root` already holds. See `boneNodes.ts` for what the split buys.
        for (const [index, node] of [...bonesByIndex]) {
            if (!(node instanceof THREE.Mesh)) {
                continue;
            }

            // Off the MATERIAL, which is where the exporter puts them and where
            // `MaterialBinder.applyMaterial` reads them from - that has not run yet, so
            // `node.userData.alamo` is still empty.
            const source = Array.isArray(node.material)
                ? node.material[0]
                : node.material;
            const extras = (node.userData.alamo ??
                source?.userData ??
                {}) as MaterialExtras;
            const bone = splitGeometryFromBone(node, extras.alamoMesh);

            bonesByIndex.set(index, bone);
            for (const [key, held] of bones) {
                if (held === node) {
                    bones.set(key, bone);
                }
            }
        }

        // Pass three: materials, over the graph as it now stands.
        root.traverse((node) => {
            if (node instanceof THREE.Mesh) {
                this.materials.applyMaterial(node);
            }
        });

        // A posed part is placed under the model root at a world transform it captured itself -
        // and stays OUTSIDE the scale, because a wreck is its own object with its own scale and
        // its drift is a world velocity. An unattached part of the ACTIVE subject is the hull,
        // which is the geometry the scale is for; an attached one inherits it through the bone it
        // hangs on, which is right because a hardpoint declares no scale of its own - 0 shipped
        // hardpoints do.
        //
        // An unattached PASSIVE part - a death clone - is neither. It is spawned as its own object,
        // so it goes beside the scaled geometry and carries its OWN scale. It used to fall through
        // to the hull's branch and take the SHIP's, which is wrong for 59 of 265 shipped pairs.
        const parent =
            options.pose !== undefined
                ? this.modelRoot
                : attachToPartId !== undefined
                  ? this.attachmentFor(attachToPartId, attachBone)
                  : passive
                    ? this.modelRoot
                    : this.scaledRoot;

        if (
            options.scale !== undefined &&
            Number.isFinite(options.scale) &&
            options.scale > 0
        ) {
            root.scale.setScalar(options.scale);
        }

        // A part attached to a BONE of another part is already inside that part's Z-up-to-Y-up
        // correction, so its own copy would apply the rotation twice - see `cancelRootCorrection`.
        // The hull goes under the scaled geometry node, which - like the model root above it - is
        // uncorrected, and keeps its own.
        //
        // A posed part is in the same position for the same reason: the world transform it copies
        // ALREADY contains the correction of the part it was taken from, so its own copy is the
        // identical mirrored 90-degree roll an attached hardpoint used to have.
        if (attachToPartId !== undefined || options.pose !== undefined) {
            cancelRootCorrection(root);
        }

        parent.add(root);

        if (options.pose !== undefined) {
            root.position.copy(options.pose.position);
            root.quaternion.copy(options.pose.quaternion);
        }

        // A key per mesh that survives the file being opened again, which the three.js uuid it
        // replaces did not - see `stampTreeKeys`.
        stampTreeKeys(root, bonesByIndex, id);

        this.parts.set(id, { id, root, bones, bonesByIndex, subjectId });

        // Its own file's word, because the row chain will never speak for it - see
        // `LoadedPart.subjectId`. Debris needs exactly this too: outside the chain, nothing else
        // hides its collision hull and its shadow volume, and it dropped showing both. Applied
        // again on every level change, from `applyLevels`.
        if (passive) {
            this.applyOwnFileVisibility(subjectId, root);

            // The host answers a texture name ONCE, and a wreck is the same ship - so every map it
            // needs was already sent for the hull and its own request comes back silent.
            this.bindLoadedTextures(root);
        }

        // AFTER the part is registered, never before: `applyWireframe` sweeps `this.parts`, so
        // running it a few lines earlier - where the materials are built - swept everything except
        // the part that had just been given them, and a turret arriving late came in solid while
        // the hull beside it was edges.
        this.materials.applyWireframe();

        // And the render state every already-translated effect declares. A material built now gets
        // three's DEFAULTS, not the effect's - and `RSkinBumpColorize.fx` declares its cull mode
        // there, so a part arriving after its shader had was drawn with every triangle facing away
        // and rendered NOTHING. That is what made a death clone invisible with shaders switched on
        // and perfect with them off. `addBreakoff` has always done this; `addPart` never did.
        for (const [shader, state] of this.materials.shaderStates) {
            this.materials.applyStoredShaderState(shader, state);
        }

        if (this.materials.colorization !== null) {
            this.setColorization(this.materials.colorization);
        }
        this.environment.collectShadowVolumes(root);

        // The handful of bones that turn to face something. Collected once per part rather than
        // searched for each frame: across the shipped models 268 bones out of 22866 declare a mode
        // at all, so a per-frame traversal would be almost entirely wasted.
        root.traverse((node) => {
            if (billboardTypeOf(node) !== null) {
                this.billboards.push(node);
            }
        });

        // Geometry can arrive after its hardpoint was destroyed; a part loading late must not
        // reappear intact.
        root.visible = !this.hiddenParts.has(id);

        // Everything the animation needs goes to THIS PART'S SUBJECT, never to the scene. One path
        // for the hull, an attached turret, a death clone and a piece of wreckage alike - what used
        // to differ between them was only which of these maps they were allowed into.
        const subject = this.subjectAnimation(subjectId);

        // Before any clip can touch it. Recorded for the whole part, not only for the bones this
        // GLB's own clips drive - a later part of the same subject can reach in here too.
        root.traverse((node) => {
            if (!subject.restPose.has(node)) {
                subject.restPose.set(node, {
                    position: node.position.clone(),
                    quaternion: node.quaternion.clone(),
                    scale: node.scale.clone(),
                });
            }

            // First wins. A visibility track names a bone, and a bone name is unique within one
            // model because the exporter suffixes it with the bone index. Keyed by the name the
            // exporter wrote, which is the one a track names - three's sanitised copy is not.
            const name = alamoNodeName(node);
            if (name !== "" && !subject.nodeByName.has(name)) {
                subject.nodeByName.set(name, node);
            }
        });

        // What its own file says is not drawn - a collision hull, a shadow volume. Held so a clip
        // can never bring one back: a shadow volume is not geometry that happens to be switched off
        // this frame.
        for (const mesh of hiddenMeshes(root)) {
            subject.fileHidden.add(mesh);
        }

        if (gltf.animations.length > 0) {
            // Rooted at the first of this subject's parts to bring clips. A clip binds to nodes BY
            // NAME from the root it was given, so a mixer rooted anywhere else drives the wrong
            // model - which is exactly what a clone's `_die` did on the scene-wide mixer.
            subject.mixer ??= new THREE.AnimationMixer(root);
            subject.clips = [...subject.clips, ...gltf.animations];

            // Off the raw glTF JSON: three's loader builds AnimationClips and does not carry an
            // animation's extras onto them, so the parser's own document is the only source.
            for (const [name, track] of visibilityTracks(gltf.parser.json)) {
                subject.visibility.set(name, track);
            }
        }

        this.applyLevels();

        return root;
    }

    /**
     * Every named attachment that still does not resolve, in words.
     *
     * RECOMPUTED rather than logged at the moment of the miss, because most misses are temporary:
     * geometry arrives asynchronously and a hardpoint routinely asks for its bone before the hull
     * carrying it has landed. A request that resolves later simply stops being reported, so this
     * can be read at any time without a stale backlog.
     */
    unresolvedAttachments(): string[] {
        const problems: string[] = [];

        for (const request of this.attachRequests.values()) {
            const parent = this.parts.get(request.partId);
            const found = {
                part: parent !== undefined,
                bone:
                    request.bone === undefined || parent === undefined
                        ? false
                        : (request.boneIndex !== undefined &&
                              parent.bonesByIndex.has(request.boneIndex)) ||
                          parent.bones.has(request.bone.toLowerCase()),
            };

            const problem = attachmentProblem(request, found);
            if (problem !== null && !problems.includes(problem)) {
                problems.push(problem);
            }
        }

        return problems;
    }

    /**
     * Applies a shader's declared render state to every material using it.
     *
     * This is the fidelity that needs no shader translation: the effects spell their blend, depth
     * and cull state out declaratively. A `custom` blend is left alone deliberately - the archetype
     * guess is better than a wrong one.
     */
    applyShaderState(shaderName: string, state: FxMaterialState): void {
        this.materials.applyShaderState(shaderName, state);
    }

    /** Every distinct shader name the loaded geometry names, so each is fetched once. */
    shaderNames(): string[] {
        return this.materials.shaderNames();
    }

    /** Sets the team tint, or clears it back to the model's own colours. */
    setColorization(colour: NormalisedColour | null): void {
        this.materials.setColorization(colour);
    }

    /** Which distinct sub-mesh names take the team colour, for the dock to report. */
    colorizedMeshes(): string[] {
        return this.materials.colorizedMeshes();
    }

    /**
     * Draws with a translated effect instead of its archetype, wherever that effect is used.
     *
     * Kept alongside the archetype rather than replacing it: a mod-authored `.fx` will never
     * translate, the eleven fixed-function effects have no shader at all, and the archetype is what
     * both fall back to. `setTranslatedShaders(false)` puts everything back on that path, which is
     * the only way to see what the translation is actually changing.
     */
    applyTranslatedEffect(shaderName: string, effect: TranslatedEffect): void {
        this.materials.applyTranslatedEffect(shaderName, effect);
    }

    /** Whether to draw with translated shaders where one is available. */
    setTranslatedShaders(enabled: boolean): void {
        this.materials.setTranslatedShaders(enabled);
    }

    /**
     * How many loaded sub-meshes and emitters a translated effect actually reaches, out of how many
     * there are. The emitters count because a particle file has no sub-meshes at all.
     */
    translatedCoverage(): { translated: number; total: number } {
        return this.materials.translatedCoverage();
    }

    /**
     * Samplers on translated materials that are reading nothing, in words.
     *
     * A sampler with no texture reads black, and the shader then draws something plausible and
     * wrong - the sort of thing that looks like a translation bug and is actually a missing file.
     */
    shaderTextureProblems(): string[] {
        return this.materials.shaderTextureProblems();
    }

    /** Applies a decoded texture wherever a material asked for it by name. */
    setTexture(name: string, texture: THREE.Texture): void {
        this.materials.setTexture(name, texture);
    }

    /**
     * Binds every texture already decoded into a subtree that has only just been added.
     *
     * `setTexture` binds at the moment the texture ARRIVES, so geometry added afterwards gets
     * nothing - and asking the host again does not help, because it remembers what it has already
     * sent and answers a repeat request with silence. Wreckage is the case that exposed this: a
     * breakoff prop is usually a piece of the hull it fell off, so its textures are always ones
     * already in hand, and it dropped as a black shape every time.
     */
    bindLoadedTextures(root: THREE.Object3D): void {
        this.materials.bindLoadedTextures(root);
    }

    /** Removes everything, so a new subject starts from an empty scene. */
    clear(): void {
        for (const entry of this.particles.particleSystems.values()) {
            entry.instance.dispose();
        }
        this.particles.particleSystems.clear();
        // A new subject is one of the two things allowed to cut a fading system short.
        this.particles.clearFadingParticles();

        // BEFORE the sweep below, not after: a wreck is one of those parts now, and taking it
        // apart a second time would dispose a tree that has already gone.
        this.clearBreakoffs();

        // BEFORE the parts are disposed. The pass holds its counting meshes by their SOURCE mesh,
        // and those sources are inside the trees about to go - so letting them be disposed first
        // leaves the map keyed by objects nobody else can reach.
        //
        // Nothing else takes them: `removePart` unregisters through `dropRegistrations`, but this
        // sweep does not go through `removePart`, so every subject switch left its volumes behind.
        // Measured: the palace alone registers 4 sources and 8 counting meshes; opened after the
        // Executor it reported 8 and 16, half of them belonging to a model no longer in the scene.
        // The stencil pass then counts geometry that is not there, which is why a handful of models
        // rendered differently depending on what had been looked at before them.
        this.environment.shadowVolumes.clear();

        for (const part of this.parts.values()) {
            part.root.removeFromParent();
            disposeTree(part.root);
        }

        this.turret.clearFireArcs();
        this.turret.clearTurretHandles();

        // The scale belongs to the SUBJECT. Left standing, the next one would be drawn at the last
        // one's - and a 0.5 ship followed by a 2.0 one is a four-fold error that looks like a
        // broken model rather than stale state.
        this.modelScale = 1;
        this.scaledRoot.scale.setScalar(1);

        // A new subject asks for its own bones. Keeping the old subject's requests would report a
        // Star Destroyer's missing hardpoint against a Mon Calamari Cruiser for the rest of the session.
        this.attachRequests.clear();
        this.cachedSpan = null;
        // The marks name parts that are about to stop existing; leaving them would draw a reticle
        // over the next model's geometry at whatever the last one's bounds happened to be.
        this.overlay.dropReticles();
        this.parts.clear();
        this.billboards = [];
        this.hiddenParts.clear();
        this.particles.transient.clear();
        this.knownDecals = new Set();
        this.shownDecals = new Set();
        this.collisionMeshes = new Set();
        this.shieldRevealed = false;
        // Every subject's clips, mixer, tracks, rest pose and node names at once. They hold nodes
        // of a model nobody is looking at any more, and keeping them would leak the whole graph.
        this.subjects.clear();
        this.overridingVisibility = false;
    }

    /**
     * Which decal meshes exist, and which of them are showing.
     *
     * Both together, because a decal that is not showing has to be actively hidden: the model ships
     * these meshes VISIBLE - measured on Ev_stardestroyer.alo, where HP_F-L_Blast is both a bone and
     * a mesh - so the engine hides them until the hardpoint is destroyed, and so must this.
     */
    setDecals(known: ReadonlySet<string>, shown: ReadonlySet<string>): void {
        this.knownDecals = known;
        this.shownDecals = shown;
        this.applyLevels();
    }

    /**
     * Which meshes the hardpoints claim as collision hulls.
     *
     * Gated OFF rather than hidden outright, so the reader can still tick one on in the tree and
     * look at it - which is exactly what someone opens a hull for. The material rules already catch
     * most of these by name; this is the author's own word, for the ones they do not.
     */
    setCollisionMeshes(names: ReadonlySet<string>): void {
        this.collisionMeshes = names;
        this.applyLevels();
    }

    /**
     * Shield meshes the model names but does not draw with a shield shader.
     *
     * Reported to the reader rather than corrected: the NAME is what decides which mesh a shield
     * ability reveals, so the author's word stands - but a bubble on a hull shader will read as
     * solid geometry rather than a field, and that is worth saying once.
     */
    shieldMeshesOffShader(): string[] {
        return this.materials.shieldMeshesOffShader();
    }

    /** Shows or hides the shield mesh, for the abilities that reveal it. */
    setShieldRevealed(revealed: boolean): void {
        this.shieldRevealed = revealed;
        this.applyLevels();
    }

    /**
     * Cloaks the unit, or uncloaks it.
     *
     * Through `applyLevels` like the shield, because the row chain is the one writer of drawn state
     * and this has to go through it rather than around it. Setting `visible` on the meshes here
     * would be a second writer, and the chain would put them back on its next run.
     */
    setStealthed(on: boolean): void {
        this.stealthed = on;
        this.applyLevels();
    }

    /** Whether a cloak is engaged, which every other visibility rule defers to. */
    isStealthed(): boolean {
        return this.stealthed;
    }

    /**
     * Puts the subject's geometry at the size the game draws it.
     *
     * See {@link scaledRoot} for why this is one node down rather than on the model root. Anything
     * already drawn is rebuilt, because the two gizmos that counter-scale were built against the
     * old value.
     */
    setModelScale(scale: number): void {
        const next = Number.isFinite(scale) && scale > 0 ? scale : 1;

        if (next === this.modelScale) {
            return;
        }

        this.modelScale = next;
        this.scaledRoot.scale.setScalar(next);

        // The span is measured off world matrices, so it changes with the scale - and everything
        // sized against the subject reads it.
        this.cachedSpan = null;

        this.turret.applyModelScale();

        this.applyClipPlanes(this.boundingSphere(), this.fitDistance);
    }

    /**
     * Draws the firing arcs, replacing whatever was there.
     *
     * Off by default, and for good reason: a Star Destroyer's arcs reach 2000 units on a hull about
     * 600 long, so leaving them on hides the ship inside its own cones.
     */
    setFireArcs(
        arcs: readonly {
            partId: string;
            bone: string;
            frame: "bone" | "hull";
            kind: "fire" | "rotation";
            widthDegrees: number;
            heightDegrees: number;
            range: number;
        }[],
    ): void {
        this.turret.setFireArcs(arcs);
    }

    setFireArcsVisible(visible: boolean): void {
        this.turret.setFireArcsVisible(visible);
    }

    /**
     * Poses turrets at their rest angle.
     *
     * COMPOSED on the pose the model authored, through the same `aimBone` the sweep and the
     * handles use: the game applies a turret's turn on top of the bone it computed from the file,
     * so a rest angle is a turn off the authored pose and never a replacement for it. Writing the
     * angle into the bone's Z rotation outright threw the authored yaw away and turned every turret
     * whose bone the exporter had oriented - a quarter turn on most (#389).
     *
     * Traverse is about the bone's local Z. Alamo is Z-up, and the exporter's Z-up-to-Y-up rotation
     * sits on the ROOT node, so a bone's own axes still follow the file's convention - the same one
     * the fire bones showed: X forward, Y lateral, Z up.
     */
    setTurretRestAngles(
        poses: readonly {
            partId: string;
            turretBone: string;
            barrelBone: string | null;
            rest: TurretAim;
        }[],
    ): void {
        this.turret.setTurretRestAngles(poses);
    }

    /**
     * Sweeps the turrets through the traverse their XML declares.
     *
     * The pose is recomputed per frame from `sweepAngles`, so what is on screen is always the
     * declared extent rather than an accumulated one - an incremental turn drifts, and a turret that
     * drifts past its own end stops is exactly the thing this exists to disprove.
     *
     * Traverse is about the bone's local Z and elevation about its X. Alamo is Z-up and the
     * exporter's Z-up-to-Y-up rotation sits on the ROOT node, so a bone's own axes still follow the
     * file's convention - the same one the fire bones showed.
     */
    setTurretSweep(
        sweeps: readonly {
            partId: string;
            turretBone: string;
            barrelBone: string | null;
            turret: PreviewTurret;
        }[],
    ): void {
        this.turret.setTurretSweep(sweeps);
    }

    /**
     * Puts drag handles on the turrets whose arcs are shown, and takes them off the rest.
     *
     * Two axes and no third: the engine has no roll on a turret -
     * the turret-angle calculation returns `Vector3(0.0, pitch, yaw)` with X a literal zero.
     *
     * Each track spans only what the engine allows, so the stop is visible before it is reached.
     * Which turrets these are is {@link turretHandles}' decision, off the arc toggle, and not this
     * class's.
     */
    setTurretHandles(handles: readonly TurretHandle[]): void {
        this.turret.setTurretHandles(handles);
    }

    /** Where the reader has pointed each turret, for the shell to read back out. */
    turretAim(id: string): TurretAim {
        return this.turret.turretAim(id);
    }

    // ── damage ────────────────────────────────────────────────────────────────

    /**
     * Puts one turret back where the model authored it.
     *
     * Needed because releasing a drag deliberately does NOT spring back - a handle that reset
     * itself would be a momentary readout rather than a control - so there has to be a way home.
     */
    resetTurretAim(id: string): void {
        this.turret.resetTurretAim(id);
    }

    /**
     * Drops a piece of wreckage where a hardpoint used to be.
     *
     * A PASSIVE SUBJECT loaded by the one loader, which is all that is left here that is specific
     * to debris: where the hardpoint was, and the clock it runs on. It used to load itself, and the
     * hand copy drifted - four of its six duplicated steps were added one bug report at a time,
     * and nine more were simply missing.
     *
     * Its own subject rather than another piece of the ship: debris is transient, it is removed on
     * its own clock, it is drawn by its own file's rules, and re-parenting the hardpoint's own model
     * would fight the destruction rules that hid it.
     */
    async addBreakoff(
        key: string,
        glbBase64: string,
        at: BreakoffAnchor,
        prop: PreviewBreakoffProp,
    ): Promise<void> {
        // Where the hardpoint was, in world space, captured once - the debris does not follow the ship.
        // WHICH node that is, is `breakoffAnchor`'s decision and not this one's: a hardpoint model can
        // carry a copy of the hull's own bones, and resolving the attachment bone by name inside it
        // applied the offset twice.
        const anchor = this.attachmentFor(at.partId, at.bone, at.boneIndex);
        const origin = anchor.getWorldPosition(new THREE.Vector3());
        const facing = anchor.getWorldQuaternion(new THREE.Quaternion());

        // The wreck's part id and its subject id are the same string on purpose: it is one model
        // and one subject, and the effects it trails already address it by this name.
        const wreckId = `${BREAKOFF_ATTACHMENT}${key}`;

        const root = await this.addPart(
            wreckId,
            glbBase64,
            undefined,
            undefined,
            {
                subjectId: wreckId,
                pose: { position: origin, quaternion: facing },
            },
        );

        this.breakoffs.push({
            key,
            partId: wreckId,
            root,
            prop,
            origin,
            facing,
            age: 0,
            lifetime: breakoffLifetime(prop),
            expired: false,
        });
    }

    /**
     * Clears the wreckage ONE hardpoint shed.
     *
     * Repairing a single hardpoint from its own card has to take its debris with it, exactly as
     * repairing the whole target does - otherwise the hardpoint is back on the hull and its wreck
     * is still tumbling away beside it.
     */
    clearBreakoff(key: string): void {
        // Spliced in place rather than reassigned: the array is readonly on the field, and the
        // wholesale clear beside this one empties it the same way for the same reason.
        for (let at = this.breakoffs.length - 1; at >= 0; at--) {
            if (this.breakoffs[at].key !== key) {
                continue;
            }

            this.removePart(this.breakoffs[at].partId);
            this.breakoffs.splice(at, 1);
        }
    }

    /** Clears every piece of wreckage - a repair puts the hardpoints back, so the debris goes. */
    clearBreakoffs(): void {
        for (const debris of this.breakoffs) {
            this.removePart(debris.partId);
        }

        this.breakoffs.length = 0;
    }

    /** Whether a piece of wreckage for this key is already in the air. */
    hasBreakoff(key: string): boolean {
        return this.breakoffs.some((debris) => debris.key === key);
    }

    /** Starts the wreck on its way. `onDone` fires once, at the end of the declared time. */
    startSpinAway(
        partId: string,
        spin: PreviewSpinAway,
        onDone: () => void,
    ): void {
        const part = this.parts.get(partId);
        if (part === undefined) {
            // Nothing to fly. Report it over at once rather than leaving the caller waiting on a
            // callback that can never come.
            onDone();
            return;
        }

        this.clearSpinAway();

        this.spinRestore = {
            partId,
            position: part.root.position.clone(),
            quaternion: part.root.quaternion.clone(),
        };

        this.spinAway = { partId, spin, elapsed: 0, onDone };
    }

    /** Puts the wreck back where it started, whether it is still flying or already exploded. */
    clearSpinAway(): void {
        this.spinAway = null;

        const home = this.spinRestore;
        this.spinRestore = null;

        if (home === null) {
            return;
        }

        const part = this.parts.get(home.partId);
        if (part !== undefined) {
            part.root.position.copy(home.position);
            part.root.quaternion.copy(home.quaternion);
        }
    }

    /** Breaks an attached part off, or puts it back. */
    setPartHidden(partId: string, hidden: boolean): void {
        if (hidden) {
            this.hiddenParts.add(partId);
        } else {
            this.hiddenParts.delete(partId);
        }

        const part = this.parts.get(partId);
        if (part !== undefined) {
            part.root.visible = !hidden;
        }

        this.particles.holdEffectsOf(partId);
    }

    /**
     * Removes one loaded part and frees its geometry.
     *
     * For parts that come and go with a STATE rather than with the subject - a death clone appears
     * when the ship dies and goes again when it is repaired. A scene part is never removed this
     * way; it lives as long as the subject does and is hidden instead.
     */
    removePart(partId: string): void {
        const part = this.parts.get(partId);
        if (part === undefined) {
            return;
        }

        // Its effects first, and through the ordinary route so `leaveParticles` still has its say.
        // They no longer live inside the part's subtree, so nothing else would ever take them:
        // before, disposing the part quietly freed their buffers underneath them, which left every
        // entry in `particleSystems` still being stepped against disposed geometry.
        for (const [id, entry] of [...this.particles.particleSystems]) {
            if (entry.attachToPartId === partId) {
                this.removeParticleSystem(id);
            }
        }

        this.dropRegistrations(part.subjectId, part.root);
        part.root.removeFromParent();
        disposeTree(part.root);
        this.parts.delete(partId);
        this.hiddenParts.delete(partId);

        // The subject goes with its last part. A repair removes the wreck, and a mixer left behind
        // would keep ticking a clip whose nodes have been disposed. The ACTIVE subject stays
        // whatever happens - it is the scene, and only `clear` ends it.
        if (
            part.subjectId !== ACTIVE_SUBJECT &&
            this.rootPartOf(part.subjectId) === null
        ) {
            this.subjects.delete(part.subjectId);
        }
    }

    /**
     * Plays a system once and forgets it.
     *
     * A death explosion is an event, not a state: it fires as the hardpoint is destroyed and must not
     * come back when the effect list is toggled or the levels change.
     */
    playOnce(
        id: string,
        system: AlamoParticleContent,
        attachToPartId?: string,
        attachBone?: string,
        /** The owning object's uniform render scale. See {@link ParticlePlacement}. */
        scaleFactor = 1,
        /** Where to put it when it hangs on no part. See {@link ParticlePlacement.at}. */
        at?: { x: number; y: number; z: number },
    ): void {
        this.particles.playOnce(
            id,
            system,
            attachToPartId,
            attachBone,
            scaleFactor,
            at,
        );
    }

    /**
     * Sets the damage state and detail level, the way the engine gates geometry.
     *
     * The DIRECTION matters, not just the value: the engine computes `altdesc = alt < m_alt` inside
     * `SetALT` and hands it to `CheckAltLod`, where a proxy marked `altDecreaseStayHidden` is held
     * back. A LOD change passes false, because winding detail down is not repairing anything.
     */
    setLevels(alt: number, lod: number): void {
        this.altDescending = alt < this.alt;
        this.alt = alt;
        this.lod = lod;

        // The reader's word SURVIVES a level change. It used to be cleared, on the grounds that a
        // different damage state is a different set of geometry - but viewport visibility is now
        // deliberately independent of what the model draws, and the row that outlives the level is
        // marked as one the model does not draw, so nothing is claimed that is not true.
        this.applyLevels();
    }

    /** The ALT and LOD levels the loaded parts and effects actually define. */
    definedLevels(): DefinedLevels {
        const tagged: LevelTagged[] = [];

        this.modelRoot.traverse((node) => {
            if (!(node instanceof THREE.Mesh)) {
                return;
            }

            const extras = node.userData.alamo as MaterialExtras | undefined;
            if (extras !== undefined) {
                tagged.push({
                    alt: extras.alamoAlt ?? null,
                    lod: extras.alamoLod ?? null,
                    altDecreaseStayHidden: false,
                });
            }
        });

        for (const entry of this.particles.particleSystems.values()) {
            tagged.push(entry.levels);
        }

        return definedLevels(tagged);
    }

    /**
     * What each detail level draws, in meshes and triangles, at the current damage stage.
     *
     * Every mesh the model holds rather than only the ones on screen: the question is what a level
     * COSTS, which is a property of the file and not of where the slider happens to sit. The rule
     * that decides which meshes belong to a level is `costByLevel`'s, which is the viewport's own
     * gate.
     */
    costByLod(levels: readonly number[]): Map<number, LevelCost> {
        const meshes: { extras: MaterialExtras; triangles: number }[] = [];

        this.modelRoot.traverse((node) => {
            if (!(node instanceof THREE.Mesh)) {
                return;
            }

            const extras = node.userData.alamo as MaterialExtras | undefined;
            if (extras !== undefined) {
                meshes.push({
                    extras,
                    triangles: triangleCount(node.geometry),
                });
            }
        });

        return costByLevel(meshes, this.alt, levels);
    }

    /**
     * Everything the model is made of, as flat items for ONE tree: bones, the meshes attached to
     * them, and the particle emitters attached to them.
     *
     * A mesh's origin IS a bone and an emitter is attached to one, so the skeleton is already the
     * structure all three live in. Ids are prefixed by kind so they cannot collide.
     */
    /** For the harness: drives controls that have no UI yet. Costs nothing when unused. */
    expose(): void {
        (globalThis as unknown as { __aetViewport?: unknown }).__aetViewport =
            this;
    }

    treeItems(): TreeItem[] {
        const items: TreeItem[] = [];

        // Rebuilt with the rows, so the inspector can never describe something the tree no longer
        // shows. Deriving it separately would mean two places deciding what a row IS - and the
        // merge rule is exactly the sort of thing that drifts when it lives twice.
        this.inspectSources.clear();

        const hull = this.hull();
        if (hull === null) {
            return items;
        }

        const boneId = boneRowId;

        // Which bone owns each object. A mesh is normally parented to the bone that is its origin;
        // a SKINNED mesh is parented at the skeleton root instead, because it spans many bones, and
        // then lists under its own first joint.
        const boneOf = new Map<THREE.Object3D, number>();
        for (const [index, node] of hull.bonesByIndex) {
            boneOf.set(node, index);
        }

        const rootBone =
            [...hull.bonesByIndex.keys()].sort((a, b) => a - b)[0] ?? 0;

        // From the object ITSELF, not from its parent: an effect names its owning bone directly
        // rather than being attached to it, so the answer is usually the very node it was handed.
        const ownerOf = (object: THREE.Object3D): number => {
            for (
                let at: THREE.Object3D | null = object;
                at !== null;
                at = at.parent
            ) {
                const index = boneOf.get(at);
                if (index !== undefined) {
                    return index;
                }
            }

            if (object instanceof THREE.SkinnedMesh) {
                const first = object.skeleton.bones[0];
                const index =
                    first === undefined ? undefined : boneOf.get(first);

                if (index !== undefined) {
                    return index;
                }
            }

            return rootBone;
        };

        const meshByBone = new Map<number, THREE.Mesh>();
        const extraMeshes: { mesh: THREE.Mesh; owner: number }[] = [];

        // The effect a proxy bone IS, rather than one attached to it - see `effectPlacement`. Held
        // by bone so the bone loop below can claim both halves as one row, the way it already does
        // for a mesh of the same name.
        const effectByBone = new Map<number, string>();

        // Rebuilt from scratch: a row that no longer exists must not keep answering for a scene
        // object that has been unloaded.
        this.rowTargets.clear();
        this.rowParents.clear();
        this.rowNames.clear();

        const bones = this.skeleton();
        const roots = new Set(
            bones.filter((bone) => bone.parent < 0).map((bone) => bone.index),
        );

        const boneName = (index: number): string => {
            const node = hull.bonesByIndex.get(index);
            return node === undefined
                ? ""
                : (alamoBoneName(alamoNodeName(node))?.name ??
                      alamoNodeName(node));
        };

        this.forEachPartMesh((mesh) => {
            // The loader guarantees a mesh is never a bone node itself, so its owner is simply the
            // nearest bone above it. `meshPlacement` decides on the two NAMES from there.
            const owner = ownerOf(mesh);
            const extras = (mesh.userData.alamo ?? {}) as MaterialExtras;

            const placement = meshPlacement(owner, roots, {
                bone: boneName(owner),
                mesh: extras.alamoMesh ?? alamoNodeName(mesh),
            });

            // A bone carrying two meshes keeps the first as its own row; the second stays a child
            // rather than being dropped.
            if ("merge" in placement && !meshByBone.has(placement.merge)) {
                meshByBone.set(placement.merge, mesh);
            } else {
                extraMeshes.push({
                    mesh,
                    owner:
                        "merge" in placement
                            ? placement.merge
                            : placement.childOf,
                });
            }
        });

        // The same question for the effects, before any row is claimed: a system whose proxy bone
        // carries its name IS that row. A bone with two of them keeps the first, exactly as it
        // keeps the first of two meshes.
        for (const [systemId, entry] of this.particles.particleSystems) {
            if (!listedInModelTree(entry.origin)) {
                continue;
            }

            const owner = ownerOf(anchorNodeOf(entry));
            const placement = effectPlacement(owner, roots, {
                bone: boneName(owner),
                effect: entry.system.name,
            });

            if ("merge" in placement && !effectByBone.has(placement.merge)) {
                effectByBone.set(placement.merge, systemId);
            }
        }

        const meshItem = (
            mesh: THREE.Mesh,
        ): { name: string; visible: boolean; gatedOff: boolean } => {
            const extras = (mesh.userData.alamo ?? {}) as MaterialExtras;

            return {
                // The ALO's own mesh name, not the glTF node name.
                name: extras.alamoMesh ?? alamoNodeName(mesh),
                visible: meshDrawn(mesh),
                gatedOff:
                    resolveMaterial(extras).hidden ||
                    !isVisibleAt(extras, this.alt, this.lod),
            };
        };

        for (const bone of bones) {
            const mesh = meshByBone.get(bone.index);
            const effect = effectByBone.get(bone.index);
            const parentId = bone.parent < 0 ? null : boneId(bone.parent);

            // Every half this row stands for. A particle proxy is all three at once - the bone, the
            // marker mesh the file hides, and the effect itself - and the panel describes each.
            this.inspectSources.set(boneId(bone.index), {
                boneIndex: bone.index,
                ...(mesh === undefined ? {} : { mesh }),
                ...(effect === undefined ? {} : { systemId: effect }),
            });

            // Merged: ONE row for a bone, the mesh that shares its name and the effect that shares
            // it, anchored on the BONE's id. That is what the bone's children already name and what
            // survives a reload, so the row needs no alias - and the alias is what cost three
            // defects, from a hull labelled COLLISION to checkboxes that did nothing.
            //
            // EVERY half is claimed, so the row writes everything it reports on. The old code
            // anchored this row on the bone's id, reported the MESH's state and wrote the BONE's -
            // which is why a hidden mesh could never be shown again.
            const details = mesh === undefined ? undefined : meshItem(mesh);
            const name =
                effect === undefined
                    ? (details?.name ?? bone.name)
                    : this.effectRowName(effect);

            this.claimRow(boneId(bone.index), name, parentId, {
                boneIndex: bone.index,
                ...(mesh === undefined ? {} : { mesh }),
                ...(effect === undefined ? {} : { particleId: effect }),
            });

            items.push({
                id: boneId(bone.index),
                // The effect is what a reader acts on, so a proxy row reads as one - the bone and
                // its marker mesh are the plumbing that carries it.
                kind:
                    effect !== undefined
                        ? "particle"
                        : mesh !== undefined
                          ? "mesh"
                          : "bone",
                name,
                parentId,
                visible: details?.visible ?? bone.visible,
                gatedOff: details?.gatedOff ?? false,
                ...(effect === undefined ? {} : { systemId: effect }),
            });
        }

        for (const { mesh, owner } of extraMeshes) {
            const details = meshItem(mesh);
            // `ownerBone`, not `boneIndex`: this row is the MESH. The bone it rides has a row of its
            // own, and describing it here too would say it twice.
            this.inspectSources.set(`mesh:${treeKeyOf(mesh)}`, {
                mesh,
                ownerBone: owner,
            });
            this.claimRow(
                `mesh:${treeKeyOf(mesh)}`,
                details.name,
                boneId(owner),
                { mesh },
            );

            items.push({
                id: `mesh:${treeKeyOf(mesh)}`,
                kind: "mesh",
                name: details.name,
                parentId: boneId(owner),
                visible: details.visible,
                gatedOff: details.gatedOff,
            });
        }

        // One row per particle SYSTEM, not per emitter. In a model preview the switchable thing is
        // the whole effect - a proxy is either playing on this hull or it is not - and the emitters
        // a system is built from are the particle editor's business, not this one's. Listing them
        // here put twenty dead rows under a Star Destroyer's damage bones.
        for (const [systemId, entry] of this.particles.particleSystems) {
            // The Gameplay lens's own. A death explosion and a wreck's trailing fire are asked for
            // by NAME at the moment they are needed, so they are in no proxy list and hang off no
            // bone - `ownerOf` falls back to the root and filed them there, giving the reader a
            // `p_explosion_big00` under Root whose tick did nothing. See `listedInModelTree`.
            if (!listedInModelTree(entry.origin)) {
                continue;
            }

            const owner = ownerOf(anchorNodeOf(entry));

            // Already folded into its proxy bone's row above. Listing it again would put the same
            // effect on screen twice, with two checkboxes writing one thing.
            if (effectByBone.get(owner) === systemId) {
                continue;
            }

            this.inspectSources.set(`particle:${systemId}`, { systemId });
            this.claimRow(
                `particle:${systemId}`,
                entry.system.name,
                boneId(owner),
                { particleId: systemId },
            );

            items.push({
                id: `particle:${systemId}`,
                kind: "particle",
                name: this.effectRowName(systemId),
                parentId: boneId(owner),
                visible: entry.gateVisible && entry.levelVisible,
                gatedOff: !entry.levelVisible,
                systemId,
            });
        }

        // Every row is known now, so the chain can run - parents are already ahead of children,
        // because the skeleton is walked parent-first and the meshes are attached to it.
        this.resolveRows();

        return items.map((item) => {
            const resolved = this.rowResolutions.get(item.id);

            // `gatedOff` comes from the chain as well, because it is the same question asked with
            // the reader taken out. Leaving each row to work it out for itself is what let a row
            // report one thing and draw another.
            return resolved === undefined
                ? item
                : {
                      ...item,
                      visible: resolved.visible,
                      because: resolved.because,
                      decidedBy: resolved.decidedBy,
                      gatedOff: !resolved.authored,
                  };
        });
    }

    /**
     * The row that owns one effect.
     *
     * A merged proxy row is anchored on its bone, so an effect's row id is not derivable from its
     * system id. Asked of the row table, which is the only thing that knows.
     */
    rowForSystem(systemId: string): string {
        for (const [rowId, target] of this.rowTargets) {
            if (target.particleId === systemId) {
                return rowId;
            }
        }

        return `particle:${systemId}`;
    }

    /**
     * What a tree row actually is, for the inspector.
     *
     * A list because a merged row is two things at once. Empty for a row the tree no longer has -
     * a selection can outlive a reload or a level change.
     */
    inspectionOf(id: string): Inspection[] {
        const source = this.inspectSources.get(id);
        if (source === undefined) {
            return [];
        }

        const found: Inspection[] = [];
        const bones = this.skeleton();
        const nameOf = (index: number): string =>
            bones.find((bone) => bone.index === index)?.name ?? "";

        if (source.mesh !== undefined) {
            const extras = (source.mesh.userData.alamo ?? {}) as MaterialExtras;
            const geometry = source.mesh.geometry;
            const vertices = geometry.getAttribute("position")?.count ?? 0;

            found.push({
                kind: "mesh",
                name: extras.alamoMesh ?? alamoNodeName(source.mesh),
                boneName: nameOf(source.ownerBone ?? source.boneIndex ?? -1),
                vertexCount: vertices,
                triangleCount: triangleCount(geometry),
                drawn: meshDrawn(source.mesh),
                extras,
                meshIndex:
                    typeof extras.alamoMeshIndex === "number"
                        ? extras.alamoMeshIndex
                        : undefined,
                subMeshIndex:
                    typeof extras.alamoSubMeshIndex === "number"
                        ? extras.alamoSubMeshIndex
                        : undefined,
            });
        }

        if (source.boneIndex !== undefined) {
            const bone = bones.find(
                (entry) => entry.index === source.boneIndex,
            );
            const node = this.hull()?.bonesByIndex.get(source.boneIndex);

            if (bone !== undefined) {
                found.push({
                    kind: "bone",
                    index: bone.index,
                    name: bone.name,
                    parentIndex: bone.parent,
                    parentName: bone.parent < 0 ? "" : nameOf(bone.parent),
                    visible: bone.visible,
                    billboard:
                        node === undefined ? null : billboardTypeOf(node),
                });
            }
        }

        if (source.systemId !== undefined) {
            const entry = this.particles.particleSystems.get(source.systemId);

            if (entry !== undefined) {
                found.push({
                    kind: "particle",
                    name: entry.system.name,
                    boneName:
                        entry.attachBoneIndex === undefined
                            ? (entry.attachBone ?? "")
                            : nameOf(entry.attachBoneIndex),
                    emitterCount: entry.system.emitters.length,
                    playing:
                        this.rowResolutions.get(
                            this.rowForSystem(source.systemId),
                        )?.visible ??
                        (entry.gateVisible && entry.levelVisible),
                });
            }
        }

        return found;
    }

    /**
     * The rows the reader has switched off by hand.
     *
     * The OVERRIDES, not what happens to be invisible: a mesh gated off by the current damage level
     * is not something anyone chose, and writing it down would turn a level's own rule into an
     * explicit hide that outlives the level. Restorable now that a mesh key survives a reload.
     */
    hiddenItemIds(): string[] {
        return [...this.rowOverrides]
            .filter(([, override]) => override === "hidden")
            .map(([row]) => row);
    }

    /**
     * The rows the reader has switched ON by hand.
     *
     * Both directions are carried now. A mesh is usually drawn until someone hides it, but a
     * shadow volume or a collision hull is gated off until someone asks for it - and asking for it
     * is exactly the deliberate act worth surviving a reopen.
     */
    shownItemIds(): string[] {
        return [...this.rowOverrides]
            .filter(([, override]) => override === "shown")
            .map(([row]) => row);
    }

    /**
     * Shows or hides any mix of bones, meshes and particle systems at once.
     *
     * One entry point, because the tree selects across kinds and a multi-select toggle has to move
     * all of them together. A bone carries what is attached to it, so switching one off takes its
     * subtree with it - which is what the reader is asking for when they untick a limb.
     */
    setItemsVisible(ids: readonly string[], visible: boolean): void {
        this.applyRowOverrides(
            ids.map((row) => ({
                row,
                override: visible ? ("shown" as const) : ("hidden" as const),
            })),
        );
    }

    /**
     * Records the reader's word on a set of rows and re-runs the chain.
     *
     * `null` gives a row back to the model. One entry point and one map, because the old code kept
     * three - meshes by tree key, bones by index, systems by id - and a row anchored on a bone's id
     * while standing for a mesh wrote one and reported the other.
     */
    applyRowOverrides(changes: readonly OverrideChange[]): void {
        for (const { row, override } of changes) {
            if (override === null) {
                this.rowOverrides.delete(row);
            } else {
                this.rowOverrides.set(row, override);
            }
        }

        this.resolveRows();
    }

    /** What the reader has said about each row, for carrying across a reopen. */
    rowOverrideEntries(): { row: string; override: RowOverride }[] {
        return [...this.rowOverrides].map(([row, override]) => ({
            row,
            override,
        }));
    }

    /**
     * Hands every row back to the model.
     *
     * Called when a clip starts: watching an animation is checking whether the animation works, and
     * that can only be answered against the model as authored. The clip already resets the pose
     * each time it runs; the visibilities are part of that pose. Master toggles are untouched - see
     * `clearedForPlayback`.
     */
    clearRowOverrides(): void {
        this.rowOverrides.clear();
        this.resolveRows();
    }

    /**
     * Hands the rows ONE ability speaks for back to the model.
     *
     * Every channel an ability drives - a proxy's `gateVisible`, the shield mesh's and the shell's
     * `inFile` - enters the chain at the `file` link, four below the reader. So one tick on such a
     * row outranked the ability for good: the switch fired, the chain ignored it, and nothing but a
     * whole-model Reset could undo it. Switching the ability now takes its own rows back, which is
     * the same rule a starting clip already follows.
     *
     * Scoped, unlike {@link clearRowOverrides}. A clip may be global because it resets the entire
     * pose; an ability is one statement about a handful of rows, and a mesh the reader hid for an
     * unrelated reason has to survive it.
     */
    releaseAbilityRows(ownership: AbilityOwnership): void {
        const released: string[] = ownership.systemIds.map((id) =>
            this.rowForSystem(id),
        );

        if (ownership.shieldMesh || ownership.stealthShell) {
            for (const [rowId, target] of this.rowTargets) {
                if (target.mesh === undefined) {
                    continue;
                }

                const extras = (target.mesh.userData.alamo ??
                    {}) as MaterialExtras;

                if (
                    (ownership.shieldMesh && isShieldMesh(extras)) ||
                    (ownership.stealthShell && isStealthMesh(extras))
                ) {
                    released.push(rowId);
                }
            }
        }

        // Only the rows that actually carry one. Every ability click would otherwise re-resolve the
        // whole skeleton to delete nothing, and most abilities own no row the reader has touched.
        const carrying = released.filter((row) => this.rowOverrides.has(row));

        if (carrying.length === 0) {
            return;
        }

        // One re-resolve at the end, not one per row.
        this.applyRowOverrides(
            carrying.map((row) => ({ row, override: null })),
        );
    }

    /** Frames whatever is currently loaded. */
    /**
     * Puts the camera where the model's own authored camera stands, looking where it looks.
     *
     * Placed from the camera's own BONE NODES rather than from the coordinates the server sent.
     * Both describe the same point, but the bone is already in the scene and has been through
     * exactly the transform the geometry went through - including the root's Z-up to Y-up rotation,
     * which is the one thing certain to be got wrong if it is applied a second time by hand. A shot
     * a quarter turn out looks like a broken camera rather than a broken conversion.
     *
     * @returns false when the model does not carry that bone pair, so the caller can say so.
     */
    applyModelCamera(name: string): boolean {
        const eye = this.boneNodeFor(name);
        const at = this.boneNodeFor(`${name}.Target`);

        if (eye === undefined || at === undefined) {
            return false;
        }

        eye.updateWorldMatrix(true, false);
        at.updateWorldMatrix(true, false);

        this.applyCameraPose(
            new THREE.Vector3().setFromMatrixPosition(eye.matrixWorld),
            new THREE.Vector3().setFromMatrixPosition(at.matrixWorld),
        );

        return true;
    }

    /**
     * Points the camera at a place, from a place.
     *
     * The one path every framing goes through - the presets, the model's own camera and the
     * reader's saved shots - so the clip planes are refitted the same way for all of them. Without
     * that a camera parked 653 units out, which `Eb_icc`'s own camera is, lands outside the range
     * the previous framing chose and the model disappears.
     */
    applyCameraPose(position: Vector3Like, target: Vector3Like): void {
        this.fitDistance = Math.hypot(
            position.x - target.x,
            position.y - target.y,
            position.z - target.z,
        );
        this.applyClipPlanes(this.boundingSphere(true), this.fitDistance);

        this.camera.position.set(position.x, position.y, position.z);
        this.controls.target.set(target.x, target.y, target.z);
        this.controls.update();
    }

    frameAll(preset: PresetView = "threeQuarter"): void {
        const sphere = this.boundingSphere();
        const { azimuth, elevation } = PRESETS[preset];
        const aspect = this.camera.aspect;

        const pose = frameSphere(
            sphere,
            FIELD_OF_VIEW,
            aspect,
            azimuth,
            elevation,
        );
        const distance = Math.hypot(
            pose.position.x - sphere.center.x,
            pose.position.y - sphere.center.y,
            pose.position.z - sphere.center.z,
        );

        this.fitDistance = distance;
        this.applyClipPlanes(this.boundingSphere(true), distance);

        this.camera.position.set(
            pose.position.x,
            pose.position.y,
            pose.position.z,
        );
        this.controls.target.set(pose.target.x, pose.target.y, pose.target.z);
        this.controls.update();

        // The grid is a sense of scale, so it has to grow with the subject: a fixed 100-unit grid is
        // invisible under a capital ship and swallows a fighter.
        this.environment.rebuildGrid(sphere);
    }

    setGridVisible(visible: boolean): void {
        this.environment.setGridVisible(visible);
    }

    /** Shows or hides the ground plane. Independent of the grid: either is useful without it. */
    setFloorVisible(visible: boolean): void {
        this.environment.setFloorVisible(visible);
    }

    /**
     * Moves the ground, and the grid measuring it, to a height in model units.
     *
     * Both together, always. The grid is the ruler for the ground plane, and leaving it at zero
     * while the floor moved would draw a measurement of somewhere the floor is not. Not every model
     * is authored standing on the origin - `Eb_icc_landingpad` and the props dug into terrain are
     * not - so a floor pinned to zero cuts through them or floats beneath them.
     */
    setFloorLevel(level: number): void {
        this.environment.setFloorLevel(level);
    }

    /**
     * Draws every part mesh as its edges.
     *
     * Reaches the translated effects as well as the archetype materials: `wireframe` is declared on
     * `THREE.Material`, and a `RawShaderMaterial` inherits it through `ShaderMaterial`, so the flag
     * is honoured whichever renderer the reader has chosen. Particle systems are pruned by
     * `forEachPartMesh` and stay as they are - a wireframe billboard is a pair of triangles and
     * says nothing about the model.
     */
    setWireframe(on: boolean): void {
        this.materials.setWireframe(on);
    }

    /**
     * Whether the engine's stencil shadows are being cast right now.
     *
     * Public because the shadow COLOUR control has to know. The tint reaches the hull through this
     * pass with no ground involved, so gating that control on the floor alone disabled it while it
     * was working. Both halves matter here for the same reason they do inside: Game mode with no
     * authored volume casts nothing, and 63% of models author none.
     */
    castsStencilShadows(): boolean {
        return this.environment.castsStencilShadows();
    }

    /** Whether the engine's stencil shadows are drawn at all. */
    /** Draws the extruded volumes themselves, for diagnosing what the stencil is counting. */
    setShadowVolumeDebug(on: boolean): void {
        this.environment.setShadowVolumeDebug(on);
    }

    setShadowVolumes(on: boolean): void {
        this.environment.setShadowVolumes(on);
    }

    /**
     * Tints the shadow, wherever it can be seen.
     *
     * TWO sinks, which the control above has to know about. Under three's shadow mapping there is
     * no colour to set at all, so a dedicated catcher plane over the floor is the only place the
     * tint can be expressed - that one needs the ground. The stencil darken really does multiply,
     * the way Alamo's own does, and it reaches the MODEL as well: a hull self-shadows in its tint
     * with no ground under it whatsoever. See `shadowTintReach`, which is the rule both halves and
     * the control are read from.
     */
    setShadowColour(colour: THREE.ColorRepresentation): void {
        this.environment.setShadowColour(colour);
    }

    /**
     * Starts a passive subject's own death clip, once.
     *
     * ONCE, and clamped: the user's word is that a death clone is spawned at the exact position of
     * the model it replaces and plays through a single time. There is no picker for it and no loop
     * - it is not something the reader chose, it is what dying looks like.
     *
     * Separate from LOADING it so the geometry can be fetched early and held: asking for a capital
     * ship's three-megabyte clone at the moment of death left a visible gap between the ship going
     * and the wreck arriving. Idempotent - a second call does not restart a clip already running,
     * because it is the same wreck.
     *
     * This is `play` for a subject nobody is driving. It is the same machinery: the subject's own
     * mixer, the subject's own clips, the subject's own tracks. It used to be a second
     * implementation of all three, because the scene-wide maps it would have had to use were
     * already full of the hull's answers.
     */
    startDeathClip(subjectId: string): void {
        const subject = this.subjects.get(subjectId);

        if (
            subject === undefined ||
            subject.mixer === null ||
            subject.action !== null
        ) {
            return;
        }

        const name = deathClip(subject.clips.map((c) => c.name));
        const clip = subject.clips.find((c) => c.name === name);

        if (clip === undefined) {
            return;
        }

        subject.action = subject.mixer.clipAction(clip);
        subject.action.setLoop(THREE.LoopOnce, 1);
        // Without this the last pose is gone the frame it arrives.
        subject.action.clampWhenFinished = true;
        subject.action.reset();
        subject.action.play();
    }

    /**
     * Plays a clip by name, or stops playback when given null.
     *
     * Timing comes from the file. The exporter writes each keyframe at `frame / fps` using the rate
     * the `.ala` itself declares - 30 fps for 1270 of the 1363 shipped animations, but 1, 5, 10 or
     * 15 for the other 93 - so 1.0x means "this clip's own declared rate", whatever that is: a
     * 15 fps clip plays at 15 frames a second. There is no rate for the panel to impose.
     *
     * The rest pose is restored before anything starts, so a clip never plays on top of the pose
     * another one left behind.
     */
    /**
     * Starts a clip by name, answering whether one actually started.
     *
     * The answer matters and used to be thrown away. A name no loaded clip carries returned here in
     * silence while the panel went on showing that clip as the one playing - which is exactly what
     * an ability's deploy did on every model, since the XML names an `.ala` FILE and the clips are
     * held under their stems. Silence is right for the viewport, which cannot know whether a miss is
     * a fault; reporting it lets the caller stop claiming otherwise.
     */
    play(name: string | null): boolean {
        this.active.action?.stop();
        this.active.action = null;
        this.resetPose();

        // The pose is not the only thing a clip resets. Watching an animation means asking whether
        // the animation works, and that can only be answered against the model as its author left
        // it - so every row goes back to what the model says. The master toggles are untouched:
        // effects switched off because they obstruct the view are the one thing worth keeping off
        // while a clip runs. See `clearedForPlayback`.
        this.clearRowOverrides();

        if (name === null || this.active.mixer === null) {
            return false;
        }

        const clip = this.active.clips.find((c) => c.name === name);
        if (clip === undefined) {
            return false;
        }

        this.active.action = this.active.mixer.clipAction(clip);
        this.active.action.setLoop(
            this.animationLoop ? THREE.LoopRepeat : THREE.LoopOnce,
            Infinity,
        );
        // Without this a non-looping action snaps back to frame 0 the instant it ends, so the last
        // pose - often the whole point of a death or a deploy - is never on screen.
        this.active.action.clampWhenFinished = true;
        this.active.action.timeScale = this.animationSpeed;
        this.active.action.paused = this.animationPaused;
        this.active.action.play();

        return true;
    }

    // ── spinning away ─────────────────────────────────────────────────────────

    setPaused(paused: boolean): void {
        this.animationPaused = paused;

        if (this.active.action !== null) {
            this.active.action.paused = paused;
        }
    }

    /**
     * Puts every animated node back where the model file had it.
     *
     * three.js does restore a binding's "original state" when an action is deactivated - but the
     * original it restores is whatever the node held when that binding was FIRST created, and a
     * binding for a clip you start mid-way through another clip captures the other clip's pose. So
     * stopping left the model in a pose no file describes, and switching clips carried the previous
     * one's residue into every bone the new clip does not drive.
     *
     * Snapshotting at load and writing it back explicitly sidesteps all of that: the rest pose is
     * the file's pose, by definition, and it cannot drift.
     */
    resetPose(): void {
        for (const [node, rest] of this.active.restPose) {
            node.position.copy(rest.position);
            node.quaternion.copy(rest.quaternion);
            node.scale.copy(rest.scale);
        }

        // Transforms are the part three.js would have restored on its own; visibility is not, and a
        // bone left switched off by the clip that just stopped is the residue that actually shows.
        // Asking the level and override rules again is what puts it back - they, not this, know
        // what the model looked like before the clip took over.
        if (this.overridingVisibility) {
            this.overridingVisibility = false;
            this.applyLevels();
        }

        // Only the CLIP's hold: the clip has stopped having an opinion, which says nothing about a
        // part that is hidden or a system that has been released to finish.
        for (const entry of this.particles.particleSystems.values()) {
            entry.instance.hold("clip", false);
        }
    }

    /** How fast the clip runs, as a multiple of the rate its file declares. */
    setAnimationSpeed(speed: number): void {
        this.animationSpeed = speed;

        if (this.active.action !== null) {
            this.active.action.timeScale = speed;
        }
    }

    setAnimationLoop(loop: boolean): void {
        this.animationLoop = loop;

        if (this.active.action !== null) {
            this.active.action.setLoop(
                loop ? THREE.LoopRepeat : THREE.LoopOnce,
                Infinity,
            );
            this.active.action.clampWhenFinished = true;
        }
    }

    /**
     * Moves the playhead, in seconds.
     *
     * `paused` is left alone: scrubbing a running clip should keep it running from where you
     * dropped it, and scrubbing a held one should leave it held on the frame you chose.
     */
    seekAnimation(seconds: number): void {
        if (this.active.action === null) {
            return;
        }

        this.active.action.time = Math.max(
            0,
            Math.min(seconds, this.active.action.getClip().duration),
        );
        // One update with no time step, so the pose on screen matches the playhead immediately
        // rather than at the next frame - which is what makes a scrubber feel attached to the model.
        this.active.mixer?.update(0);
    }

    /** Where the playhead is and how long the clip runs, for the scrubber. Zeroes when idle. */
    animationProgress(): { time: number; duration: number; running: boolean } {
        if (this.active.action === null) {
            return { time: 0, duration: 0, running: false };
        }

        return {
            time: this.active.action.time,
            duration: this.active.action.getClip().duration,
            running:
                this.active.action.isRunning() && !this.active.action.paused,
        };
    }

    stats(): ViewportStats {
        let meshes = 0;
        let triangles = 0;

        this.modelRoot.traverse((node) => {
            if (node instanceof THREE.Mesh && this.drawn(node)) {
                meshes++;
                triangles += triangleCount(node.geometry);
            }
        });

        return {
            // The ACTIVE subject's, not the scene's. These stats describe the model being
            // previewed, and a wreck it shed or the clone it became is not another part of it.
            parts: this.activeSubjectRoots().length,
            meshes,
            triangles: Math.round(triangles),
            // From the same source as the tree. Counting THREE.Bone instead reported ZERO for any
            // model with no skinned mesh - the loader only creates Bone objects for skin joints, so
            // the Star Destroyer's 71 bones read as none while the tree beside it listed all of them.
            bones: this.skeleton().length,
            animations: this.animationNames,
        };
    }

    /**
     * Where a bone sits in world space, or null when it is not loaded.
     *
     * Thin on purpose: the blast arithmetic is pure and lives in `blast.ts`, and all it needs from
     * a scene graph is a point per hardpoint. Anything more here would be geometry the tests cannot
     * reach.
     */
    bonePosition(
        partId?: string,
        bone?: string,
    ): { x: number; y: number; z: number } | null {
        if (partId === undefined) {
            return vectorOf(
                this.modelRoot.getWorldPosition(new THREE.Vector3()),
            );
        }

        // Recorded even though this refuses to place anything: the request was made, and it is the
        // REQUEST that gets reported. The early return below used to happen before `attachmentFor`
        // was ever reached, so a hardpoint that had not loaded left no trace anywhere.
        this.noteAttachment(partId, bone);

        const part = this.parts.get(partId);
        if (part === undefined) {
            return null;
        }

        if (bone === undefined) {
            return vectorOf(part.root.getWorldPosition(new THREE.Vector3()));
        }

        // NULL for a bone that is not there, rather than the part's origin. `attachmentFor` answers
        // the root because it has to return something to parent to; this returns a POSITION, and
        // the model's origin is not this bone's position. Handing one back put every unresolved
        // hardpoint at the same spot, which is a blast radius that catches all of them.
        const node = part.bones.get(bone.toLowerCase());

        return node === undefined
            ? null
            : vectorOf(node.getWorldPosition(new THREE.Vector3()));
    }

    /**
     * The Alamo extras of every loaded material.
     *
     * Read back off the scene rather than tracked alongside it, so the texture request list can
     * never fall out of step with what was actually loaded.
     */
    materialExtras(): MaterialExtras[] {
        return this.materials.materialExtras();
    }

    /**
     * Starts a particle system, optionally attached to a bone of a loaded part.
     *
     * Parented rather than positioned: a damaged hardpoint's smoke has to follow the hull as it
     * animates, and parenting gets that for nothing.
     */
    addParticleSystem(
        id: string,
        system: AlamoParticleContent,
        placement: ParticlePlacement,
    ): void {
        this.particles.addParticleSystem(id, system, placement);
    }

    // ── levels ────────────────────────────────────────────────────────────────

    /**
     * Takes a system out of the scene.
     *
     * What it does with the particles ALREADY IN THE AIR is the file's word: see
     * the fading list in `ParticleHost`. `immediate` overrides it, and only two things may - replacing a
     * system under the same id, and a wholesale reset of the scene or the renderer.
     */
    removeParticleSystem(id: string, immediate = false): void {
        this.particles.removeParticleSystem(id, immediate);
    }

    /** Whether a part's geometry has arrived, so something can be attached to its bones. */
    hasPart(partId: string): boolean {
        return this.parts.has(partId);
    }

    /** Names of the running systems, for the emitter list in the dock. */
    particleSystemIds(): string[] {
        return this.particles.particleSystemIds();
    }

    /** Every texture the running systems want but may not have yet. */
    particleTextureNames(): string[] {
        return this.particles.particleTextureNames();
    }

    /** Hands a fetched texture to whichever emitters named it, now and after a restart. */
    setParticleTexture(name: string, texture: THREE.Texture): void {
        this.particles.setParticleTexture(name, texture);
    }

    /**
     * Says a texture will never arrive, so every emitter naming it shows the marker.
     *
     * Remembered, not just applied: a system can be built after the answer came back - a hardpoint
     * blowing up spawns one - and it has to show the marker too rather than sitting on a blank quad
     * because it happened to be late.
     */
    setParticleTextureMissing(name: string): void {
        this.particles.setParticleTextureMissing(name);
    }

    setParticlesVisible(visible: boolean): void {
        this.particles.setParticlesVisible(visible);
    }

    /**
     * What the game's rules say about a system: the opening rule and the current damage state.
     *
     * The RULES path, called on attach and again whenever a hardpoint is destroyed or repaired -
     * never in response to a click. A hand-set override survives this, so switching one effect on
     * does not get undone by an unrelated hardpoint blowing up.
     *
     * What the rules say is `inFile` for this row - the model's own word - and the chain weighs it
     * against everything else. It does NOT clear a hand-set override: the reader's word is taken
     * back only by the reader or by a clip starting, and a row standing against the rules is marked
     * as one the model does not draw, so nothing here is claimed that is not true.
     */
    setParticleSystemVisible(id: string, visible: boolean): void {
        this.particles.setParticleSystemVisible(id, visible);
    }

    /** The emitters of one system, in file order. */
    emitterNames(systemId: string): string[] {
        return this.particles.emitterNames(systemId);
    }

    /** Shows or hides one emitter of one system, by its position in that system's file. */
    setEmitterVisible(systemId: string, index: number, visible: boolean): void {
        this.particles.setEmitterVisible(systemId, index, visible);
    }

    setParticlesPaused(paused: boolean): void {
        this.particles.setParticlesPaused(paused);
    }

    /** Multiplier on particle time. 1 is real time. */
    setParticleSpeed(speed: number): void {
        this.particles.setParticleSpeed(speed);
    }

    /**
     * Replays every system from nothing.
     *
     * Rebuilt rather than rewound: a burst emitter's whole point is the first half second, and there
     * is no way back to it from the middle of a run short of discarding the state.
     */
    restartParticles(): void {
        this.particles.restartParticles();
    }

    /**
     * The hull's bones, flattened.
     *
     * Read off the loaded scene rather than tracked separately, so the tree can never describe a
     * skeleton that is not the one on screen. Only the FIRST part's bones: an attached turret carries
     * its own skeleton, and merging them would produce a tree whose indices match no single model.
     */
    skeleton(): FlatBone[] {
        const hull = this.hull();
        if (hull === null) {
            return [];
        }

        const indexOf = new Map<THREE.Object3D, number>();
        for (const [index, node] of hull.bonesByIndex) {
            indexOf.set(node, index);
        }

        // The nearest ANCESTOR that is a bone, not just the immediate parent. The exporter puts a
        // synthetic node between the skeleton and the scene, and a skinned model can have others in
        // between too - so checking only `node.parent` reported a bone as parentless whenever
        // anything sat in the gap, and the tree came out with a handful of spurious roots. On the
        // rancor that made `Shadow_Feet` a root of the model, which it plainly is not.
        const boneAncestor = (node: THREE.Object3D): number => {
            for (let at = node.parent; at !== null; at = at.parent) {
                const index = indexOf.get(at);
                if (index !== undefined) {
                    return index;
                }
            }

            return -1;
        };

        return [...hull.bonesByIndex.entries()]
            .map(([index, node]) => ({
                index,
                name:
                    alamoBoneName(alamoNodeName(node))?.name ??
                    alamoNodeName(node),
                parent: boneAncestor(node),
                visible: node.visible,
            }))
            .sort((a, b) => a.index - b.index);
    }

    /** What is attached to each bone of the hull, keyed by bone index. */
    attachmentsByBone(): Map<number, BoneAttachment[]> {
        const attachments = new Map<number, BoneAttachment[]>();
        const hull = this.hull();
        if (hull === null) {
            return attachments;
        }

        // Which bone each object belongs to, so a mesh can be placed even when it is not a direct
        // child of one. A SKINNED mesh is parented to the skeleton root rather than attached to any single bone -
        // it spans many - and listing only direct children left most of a skinned model with no row
        // at all, which is no use for a control whose whole point is reaching every mesh.
        const boneOf = new Map<THREE.Object3D, number>();
        for (const [index, node] of hull.bonesByIndex) {
            boneOf.set(node, index);
        }

        const rootBone =
            [...hull.bonesByIndex.keys()].sort((a, b) => a - b)[0] ?? 0;
        const meshesByBone = new Map<number, THREE.Mesh[]>();

        hull.root.traverse((object) => {
            if (!(object instanceof THREE.Mesh) || object.name === "") {
                return;
            }

            // The nearest ancestor that is a bone; the root otherwise.
            let owner = rootBone;
            for (let at = object.parent; at !== null; at = at.parent) {
                const index = boneOf.get(at);
                if (index !== undefined) {
                    owner = index;
                    break;
                }
            }

            meshesByBone.set(owner, [
                ...(meshesByBone.get(owner) ?? []),
                object,
            ]);
        });

        for (const [index, node] of hull.bonesByIndex) {
            const found: BoneAttachment[] = [];

            for (const mesh of meshesByBone.get(index) ?? []) {
                found.push({ kind: "mesh", label: mesh.name });
            }

            // A attached part is a separate root parented onto the bone, so it reads as a hardpoint.
            for (const part of this.parts.values()) {
                if (part.root.parent === node) {
                    found.push({ kind: "hardpoint", label: part.id });
                }
            }

            if (found.length > 0) {
                attachments.set(index, found);
            }
        }

        return attachments;
    }

    setSkeletonVisible(visible: boolean): void {
        this.overlay.setSkeletonVisible(visible);
    }

    setLabelMode(mode: LabelMode): void {
        this.overlay.setLabelMode(mode);
    }

    /**
     * Which bones a weapon fires from, so the triad can mark the aim axis.
     *
     * By NAME, because that is what a weapon declares. The axis itself is local X and that is
     * measured rather than assumed - see `AlamoFireBone.AimDirection`.
     */
    setFireBones(names: ReadonlySet<string>): void {
        this.overlay.setFireBones(names);
    }

    setSelectedBones(indices: ReadonlySet<number>): void {
        this.overlay.setSelectedBones(indices);
    }

    /**
     * Draws a box round what the tree has selected.
     *
     * Selecting a bone boxes its whole SUBTREE, not its origin: a bone is where something is
     * attached, so a box around the point itself would be a dot in the middle of the thing being
     * pointed at. A row whose ancestor is selected too gets no box of its own - one box around the
     * arm beats a second one nested inside it around the hand.
     */
    setSelectedRows(ids: readonly string[]): void {
        this.overlay.setSelectedRows(ids);
    }

    /**
     * Parts to outline whole, beside whatever rows are boxed.
     *
     * A hardpoint that attaches a model is one of these: its geometry is its own part, and boxing
     * the hull bone it is attached to gathers that bone's whole subtree instead - the turret's meshes and
     * every empty fire-point bone under it, which on the Executor stretched the box to 2211 units
     * on a 5094 hull. See `boxTargetFor`.
     */
    setSelectedHardpoint(partId: string | null, boneRow: string | null): void {
        this.overlay.setSelectedHardpoint(partId, boneRow);
    }

    /**
     * The targeting marks to draw, and how big.
     *
     * Replaced wholesale rather than diffed: the list is one per targetable hardpoint - ten on the Star
     * Destroyer, and never more than a few hundred - and it changes only when the scene, the state
     * or the damage does.
     */
    setReticles(
        marks: readonly ReticleMark[],
        screenSize: number | null | undefined,
    ): void {
        this.overlay.setReticles(marks, screenSize);
    }

    /**
     * Renders one frame at a chosen size and hands back the PNG bytes.
     *
     * The renderer is resized, drawn and put back inside ONE synchronous block. Without
     * `preserveDrawingBuffer` the drawing buffer is only guaranteed to hold its contents until the
     * browser composites, which cannot happen while this is running - and asking for that flag
     * instead would cost every ordinary frame for the sake of the rare capture.
     *
     * What it hides is passed in rather than read from the panel's own toggles: an icon usually
     * wants no grid, no floor and no hardpoint markers even while the reader is working with all
     * three switched on, and a capture that quietly changed what they were looking at would be
     * worse than one that asked.
     */
    capture(options: {
        width: number;
        height: number;
        background: string | null;
        grid: boolean;
        floor: boolean;
        particles: boolean;
        /** The axes, the skeleton and the firing arcs: diagnostics, not part of the subject. */
        annotations: boolean;
    }): string {
        const before = {
            width: this.canvas.width,
            height: this.canvas.height,
            aspect: this.camera.aspect,
            pixelRatio: this.renderer.getPixelRatio(),
            background: this.scene.background,
            grid: this.environment.grid.visible,
            floor: this.environment.floor.visible,
            catcher: this.environment.shadowCatcher.visible,
            particles: this.particles.particlesVisible,
            axes: this.environment.axes.visible,
            joints: this.overlay.joints.visible,
            jointLines: this.overlay.jointLines.visible,
            selection: this.overlay.selectionBoxes.visible,
            arcs: this.turret.arcsShown,
        };

        try {
            // One device pixel per requested pixel: a capture is measured in pixels, not in the
            // reader's display scaling, or the same request would give a different file on a
            // different monitor.
            this.renderer.setPixelRatio(1);
            this.renderer.setSize(options.width, options.height, false);
            // The frame drawn over the viewport, exactly: the largest rectangle of the capture's
            // shape that fits the viewport, centred (captureFrame). Rendered as a view offset on the
            // viewport's own projection, so a capture wider than the viewport shows what the frame
            // shows - not more to either side, which is what widening the aspect used to do.
            const frame = captureFrame(
                before.width,
                before.height,
                options.width,
                options.height,
            );
            this.camera.setViewOffset(
                before.width,
                before.height,
                frame.x,
                frame.y,
                frame.width,
                frame.height,
            );
            this.camera.updateProjectionMatrix();

            // Null is TRANSPARENT, which is what an icon wants: the alpha the renderer was created
            // with is what makes it possible at all.
            this.scene.background =
                options.background === null
                    ? null
                    : new THREE.Color(options.background);

            this.environment.grid.visible = options.grid;
            this.environment.floor.visible = options.floor;
            this.environment.shadowCatcher.visible =
                options.floor && before.catcher;
            this.setParticlesVisible(options.particles);

            // The axes, the skeleton and the firing arcs are things drawn to help someone READ the
            // model. They are not part of it, and an icon with a coloured cross through it is not
            // an icon - which is exactly what the first capture came out as.
            this.environment.axes.visible = options.annotations && before.axes;
            this.overlay.joints.visible = options.annotations && before.joints;
            this.overlay.jointLines.visible =
                options.annotations && before.jointLines;

            // A selection box is a thing drawn to help someone READ the model, exactly like the
            // axes and the skeleton, and an icon with an amber cage around it is not an icon.
            this.overlay.selectionBoxes.visible =
                options.annotations && before.selection;

            if (!options.annotations) {
                this.setFireArcsVisible(false);
            }

            this.prepareForCamera();

            // A sprite is oriented against the VIEW matrix - a streak's length is a screen-space
            // measurement - so it has to be re-aimed for this camera. A zero step re-aims without
            // advancing the simulation: a capture must not move the effects on.
            if (this.particles.particlesVisible) {
                for (const entry of this.particles.particleSystems.values()) {
                    entry.instance.update(
                        0,
                        this.camera.matrixWorldInverse,
                        this.particles.windVector,
                    );
                }
            }

            this.environment.renderFrame();

            return this.canvas.toDataURL("image/png");
        } finally {
            // Always put the room back, including when the render throws: a failed capture must not
            // leave the reader looking at a 64-pixel viewport with the grid switched off.
            this.renderer.setPixelRatio(before.pixelRatio);
            this.scene.background = before.background;
            this.environment.grid.visible = before.grid;
            this.environment.floor.visible = before.floor;
            this.environment.shadowCatcher.visible = before.catcher;
            this.setParticlesVisible(before.particles);
            this.environment.axes.visible = before.axes;
            this.overlay.joints.visible = before.joints;
            this.overlay.jointLines.visible = before.jointLines;
            this.overlay.selectionBoxes.visible = before.selection;
            this.setFireArcsVisible(before.arcs);
            this.camera.clearViewOffset();
            this.camera.aspect = before.aspect;
            this.camera.updateProjectionMatrix();
            this.resize();
            this.prepareForCamera();
            this.environment.renderFrame();
        }
    }

    resize(): void {
        const width = this.canvas.clientWidth || 1;
        const height = this.canvas.clientHeight || 1;

        this.renderer.setSize(width, height, false);
        this.camera.aspect = width / height;
        this.camera.updateProjectionMatrix();
    }

    /** Whether heat sprites bend the frame. */
    setHeat(on: boolean): void {
        this.environment.setHeat(on);
    }

    /**
     * Draws the heat buffer rather than the bent picture.
     *
     * Forces the heat path on while it is on, even with nothing distorting: a debug view that shows
     * an ordinary frame until something happens to be burning is a control the reader cannot tell
     * is working. It costs the two heat buffers for as long as it is switched on, which is the
     * bargain a debug view is.
     */
    setHeatDebug(on: boolean): void {
        this.environment.setHeatDebug(on);
    }

    setBloom(on: boolean): void {
        this.environment.setBloom(on);
    }

    /**
     * The reader's draw-distance multiplier.
     *
     * Applied on the next reframe as well as at once, so a change is visible without having to
     * touch the camera.
     */
    setDrawDistance(multiplier: number): void {
        this.drawDistance = multiplier;
        this.applyClipPlanes(this.boundingSphere(), this.fitDistance);
    }

    dispose(): void {
        this.disposed = true;
        // The capture flag has to match, or this silently removes nothing.
        this.canvas.removeEventListener("pointerdown", this.onPointerDown, {
            capture: true,
        });
        this.canvas.removeEventListener(
            "pointermove",
            this.turret.onPointerMove,
        );
        window.removeEventListener("pointerup", this.turret.onPointerUp);
        window.removeEventListener("pointercancel", this.turret.onPointerUp);
        cancelAnimationFrame(this.frameHandle);
        this.clear();
        this.controls.dispose();
        this.environment.heat?.dispose();
        this.environment.bloom?.dispose();
        this.environment.shadowVolumes.dispose();
        this.environment.frameCopy?.dispose();
        this.renderer.dispose();
    }

    /** The object a part is attached to: a named bone of another part, or the scene root. */
    private attachmentFor(
        partId?: string,
        bone?: string,
        boneIndex?: number,
    ): THREE.Object3D {
        if (partId === undefined) {
            return this.modelRoot;
        }

        this.noteAttachment(partId, bone, boneIndex);

        const parent = this.parts.get(partId);
        if (parent === undefined) {
            return this.modelRoot;
        }

        if (bone === undefined) {
            return parent.root;
        }

        // By INDEX where the caller has one, because bone names repeat: Boba Fett's two jetpack
        // jets are two bones both called `p_boba_jetpack`, and a name-keyed map holds one of them -
        // so both effects were attached to the same jet and he fired from one.
        const byIndex =
            boneIndex === undefined
                ? undefined
                : parent.bonesByIndex.get(boneIndex);

        // A hardpoint naming a bone the hull does not have is already reported as a problem by the
        // server; here it just means the part sits at the hull's origin rather than vanishing.
        const anchor =
            byIndex ?? parent.bones.get(bone.toLowerCase()) ?? parent.root;

        return this.mountable(anchor);
    }

    /**
     * An anchor a mount cannot be hidden BY.
     *
     * Reported: two of the Gargantuan's turrets never drew, and the model tree showed why - the
     * whole attached model, collision mesh and all, sat as CHILDREN of a hull node that was itself a
     * hidden mesh. Turning that row on brought the turret with it. Three prunes a hidden subtree, so
     * anything mounted on a hidden node is gone whatever its own state says.
     *
     * `splitGeometryFromBone` exists to stop that - after it a bone node never carries geometry, so
     * hiding the geometry cannot hide the mount. But the split only runs over nodes that WON a bone
     * index during load, and a hull whose exporter emitted a separate mesh node for the same name
     * leaves a Mesh in the bone map. That is what these two hardpoints found.
     *
     * So the split is applied HERE too, at the moment something is about to hang on the node. It is
     * idempotent: a node that is not a Mesh is already what it needs to be.
     *
     * The engine agrees from its own side - an attached thing does not inherit its host's
     * visibility. Hiding a bone's geometry is a statement about GEOMETRY.
     */
    private mountable(anchor: THREE.Object3D): THREE.Object3D {
        if (!(anchor instanceof THREE.Mesh)) {
            return anchor;
        }

        const extras = anchor.userData.alamo as MaterialExtras | undefined;
        const bone = splitGeometryFromBone(anchor, extras?.alamoMesh);

        // The maps still point at the mesh. Both are re-pointed, or the next lookup hands out the
        // node that was just taken apart - and the mount would hang on it again.
        for (const part of this.parts.values()) {
            for (const [key, held] of part.bones) {
                if (held === anchor) {
                    part.bones.set(key, bone);
                }
            }

            for (const [index, held] of part.bonesByIndex) {
                if (held === anchor) {
                    part.bonesByIndex.set(index, bone);
                }
            }
        }

        return bone;
    }

    /**
     * Remembers that something asked to attach to this bone.
     *
     * Separate from resolving it, because a caller can legitimately refuse to place anything - see
     * {@link bonePosition} - and its request still has to be reported. Both of `attachmentFor`'s
     * fallbacks draw something recoverable and say nothing, and two separate bugs this session hid
     * behind them: fire cones resolving on the wrong model, reticles piling up at the origin.
     *
     * Breakoff roots are left out on purpose - a wreck is meant to stop existing, so a request for
     * one that has been cleared is not a fault.
     */
    private noteAttachment(
        partId: string,
        bone?: string,
        boneIndex?: number,
    ): void {
        if (partId.startsWith(BREAKOFF_ATTACHMENT)) {
            return;
        }

        this.attachRequests.set(
            `${partId} / ${bone ?? ""} / ${boneIndex ?? ""}`,
            { partId, bone, boneIndex },
        );
    }

    /**
     * Every mesh belonging to the ACTIVE subject.
     *
     * Not `modelRoot.traverse`: particle systems are parented under the same root - under a bone,
     * so their smoke follows the hull as it animates - so a blanket traversal reaches their
     * geometry too. Handing a particle billboard a model material replaces the material that makes
     * it a particle at all, which is exactly what happened when the shader toggle rebuilt
     * everything under `modelRoot`.
     */
    private forEachPartMesh(visit: (mesh: THREE.Mesh) => void): void {
        // Passive subjects are left out: this is what the row chain, the tree and the subject's
        // stats are built on, and a death clone is not part of the subject it replaces, any more
        // than a piece of wreckage is. See `LoadedPart.subjectId`.
        this.walkModelMeshes(this.activeSubjectRoots(), visit);
    }

    /** The roots of the subject that opened the scene. */
    private activeSubjectRoots(): THREE.Object3D[] {
        return [...this.parts.values()]
            .filter((part) => part.subjectId === ACTIVE_SUBJECT)
            .map((part) => part.root);
    }

    /**
     * Every mesh that is DRAWN as model geometry, every subject's.
     *
     * The separation matters in both directions. A passive subject has no tree rows and no place
     * in a coverage count, so the queries that DESCRIBE the subject stay on
     * {@link forEachPartMesh}. But it is drawn by the same shaders as the hull it fell off, so
     * everything that decides HOW a mesh draws has to reach it - and for wreckage none of it did.
     *
     * That is what drew a wreck's `MeshAdditive` light strip as a black slab: `applyShaderState`
     * puts an effect's declared blend, depth and cull onto the materials using it, a translated
     * material gets its blend from nowhere else, and the sweep walked `parts` - which a breakoff
     * root was deliberately not in. The wireframe, the team tint and the shadow-casting decision
     * were all missing it for the same reason. Wreckage IS a part now, so this is simply every
     * one of them.
     */
    private forEachDrawnMesh(visit: (mesh: THREE.Mesh) => void): void {
        // Every subject: how a mesh draws - its blend, its wireframe, its team tint - is the same
        // question whoever owns it. Only WHETHER it draws differs.
        this.walkModelMeshes(
            [...this.parts.values()].map((part) => part.root),
            visit,
        );
    }

    private walkModelMeshes(
        roots: readonly THREE.Object3D[],
        visit: (mesh: THREE.Mesh) => void,
    ): void {
        // A manual walk rather than `traverse`, because this has to PRUNE. A particle system is
        // parented to a BONE so its smoke follows the hull as it animates - which puts it inside the
        // part's own root, where `traverse` still finds it. Scoping to the part roots was not enough
        // on its own: the billboards were still being handed model materials, and still turning up
        // in the mesh list as if they were geometry.
        const walk = (node: THREE.Object3D): void => {
            if (node.userData.aetParticleSystem === true || isOverlay(node)) {
                return;
            }

            // The stencil volumes' counting meshes are this class's own scaffolding, not the
            // model's geometry. Left in, they took model materials, turned up as phantom tree rows
            // and were swept by the wireframe - the same shape of bug the particle prune fixes.
            if (
                node instanceof THREE.Mesh &&
                node.userData.aetShadowVolume !== true
            ) {
                visit(node);
            }

            for (const child of node.children) {
                walk(child);
            }
        };

        // Only the OUTERMOST roots. A hardpoint's model is attached to a bone of the hull, so it is
        // already inside the hull's root - walking every part in turn visited each attached mesh
        // twice. See `outermostRoots`.
        for (const root of outermostRoots(roots)) {
            walk(root);
        }
    }

    /**
     * The bone palette for one skinned mesh, in the space the Alamo skinned shaders work in.
     *
     * WORLD space, not mesh-local. The skinned effects project with VIEWPROJECTION rather than
     * WORLDVIEWPROJECTION - their own comment says "we are working in world space here" - so the
     * skin matrix has to carry the whole way out. Conjugating only by the bind matrices, the way
     * three's own skinning shader does, leaves the result mesh-local and the turret's barrel flew
     * off; the extra `matrixWorld` is what the difference in projection matrix demands.
     */
    private skinPalette(mesh: THREE.SkinnedMesh): Float32Array {
        mesh.skeleton.update();

        // Null until the skeleton has been initialised, which `update` above has just done.
        const source = mesh.skeleton.boneMatrices;
        if (source === null || source === undefined) {
            return this.skinScratch;
        }

        const bones = mesh.skeleton.bones.length;
        const palette =
            this.skinScratch.length === bones * 16
                ? this.skinScratch
                : (this.skinScratch = new Float32Array(bones * 16));

        for (let i = 0; i < bones; i++) {
            SKIN_WORK.fromArray(source, i * 16);
            SKIN_WORK.multiply(mesh.bindMatrix);
            SKIN_WORK.premultiply(mesh.bindMatrixInverse);
            SKIN_WORK.premultiply(mesh.matrixWorld);
            SKIN_WORK.toArray(palette, i * 16);
        }

        return palette;
    }

    /**
     * The node a fire bone names, looking on the HULL before the attached model.
     *
     * A hardpoint model is cut from the hull and keeps its whole skeleton, so a bone like
     * `FP_F-L_00` exists on BOTH - and the copy inside the attached model still sits at its
     * HULL-space position. Resolving it there placed it twice over, because the attached model is
     * itself attached at the hardpoint's bone. Measured on `Calamari_Cruiser`: the hull's
     * `FP_F-L_00` is at (28, 3, 275), a few units ahead of its hardpoint at (30, 2, 249) - and the
     * attached model's copy is at (59, 6, 523), almost exactly double. Every one of its six weapons
     * had its cone out in open space instead of at the muzzle.
     *
     * The fallback still matters: a hardpoint that carries a muzzle bone the hull does not have is
     * resolved on the hardpoint, which is the only place it exists.
     */
    private fireBoneAttachment(partId: string, bone: string): THREE.Object3D {
        return (
            this.parts.get("hull")?.bones.get(bone.toLowerCase()) ??
            this.attachmentFor(partId, bone)
        );
    }

    // ── particles ──────────────────────────────────────────────────────

    /**
     * Drifts the wreckage and retires it when its time is up.
     *
     * Recomputed from the age rather than integrated per frame: an incremental drift accumulates
     * float error, and debris that ends up somewhere slightly different each run is exactly what
     * makes two looks at the same hardpoint disagree.
     */
    private advanceBreakoffs(deltaSeconds: number): void {
        if (this.breakoffs.length === 0) {
            return;
        }

        const alive: BreakoffInstance[] = [];

        for (const debris of this.breakoffs) {
            // An expired piece is a marker and nothing more: it holds the place its explosion goes
            // off, and it must not keep drifting away from it.
            if (debris.expired) {
                alive.push(debris);
                continue;
            }

            debris.age += deltaSeconds;

            if (debris.age >= debris.lifetime) {
                // The GEOMETRY goes; the root stays. `Death_Explosions` fires where the piece
                // finished, and once the root is gone nothing in the scene knows where that was -
                // which is why the wreck simply vanished with no blast at all.
                const subjectId =
                    this.parts.get(debris.partId)?.subjectId ?? debris.partId;

                for (const child of [...debris.root.children]) {
                    this.dropRegistrations(subjectId, child);
                    child.removeFromParent();
                    disposeTree(child);
                }

                debris.expired = true;
                alive.push(debris);
                this.onBreakoffExpired?.(debris.key, debris.prop);
                continue;
            }

            const pose = breakoffPose(debris.prop, debris.age);

            debris.root.position.set(
                debris.origin.x + pose.offset.x,
                debris.origin.y + pose.offset.y,
                debris.origin.z + pose.offset.z,
            );

            const spin = new THREE.Euler(
                pose.rotation.x * (Math.PI / 180),
                pose.rotation.y * (Math.PI / 180),
                pose.rotation.z * (Math.PI / 180),
            );

            debris.root.quaternion
                .copy(debris.facing)
                .multiply(new THREE.Quaternion().setFromEuler(spin));

            alive.push(debris);
        }

        this.breakoffs.length = 0;
        this.breakoffs.push(...alive);
    }

    private advanceSpinAway(dt: number): void {
        const flying = this.spinAway;
        if (flying === null) {
            return;
        }

        flying.elapsed += dt;

        const part = this.parts.get(flying.partId);
        const home = this.spinRestore;
        const pose = spinAwayPose(flying.spin, flying.elapsed);

        if (part !== undefined && home !== null) {
            // Offsets are in the model's own space, and the exporter's Z-up to Y-up rotation lives
            // on a node INSIDE the part - so adding them to the part root is adding them in the
            // space the numbers were computed in.
            //
            // Divided by the geometry scale, because the travel term is
            // `Max_Speed * frames` - an XML speed, and therefore a WORLD distance - while the part
            // root sits inside the scaled node. Without it the wreck and the explosion that marks
            // where it stopped part company on any subject that declares a scale: `spinAwayEnd`
            // hands that same offset to a particle anchored on the UNSCALED model root.
            const travel = 1 / this.modelScale;

            part.root.position.set(
                home.position.x + pose.offset.x * travel,
                home.position.y + pose.offset.y * travel,
                home.position.z + pose.offset.z * travel,
            );

            // Roll about the TRAVEL axis, which is Z - the blue one. Rolling about X while flying
            // along Z is a tumble end over end, not a corkscrew.
            part.root.quaternion
                .copy(home.quaternion)
                .multiply(
                    new THREE.Quaternion().setFromAxisAngle(
                        new THREE.Vector3(0, 0, 1),
                        pose.roll,
                    ),
                );
        }

        if (pose.done) {
            // Cleared BEFORE the callback: the panel hides the part and fires the explosion from
            // in there, and a re-entrant start would otherwise be undone by this same tick.
            const done = flying.onDone;
            this.spinAway = null;
            done();
        }
    }

    /**
     * Gives up everything the scene registered for a subtree that is about to be disposed.
     *
     * The counterpart of what {@link addPart} records, and it did not exist while only the hull
     * and its hardpoints came through there - a scene part lives as long as the subject does. Now that
     * a death clone and a piece of wreckage take the same route, geometry comes and goes inside a
     * standing scene, and every one of those registrations outlived it: the stencil pass went on
     * counting a repaired ship's death clone, and its `bySource` map held the whole disposed
     * subtree alive through the mesh it was keyed by.
     *
     * The nodes are collected before anything is dropped, because `ShadowVolumePass.remove`
     * unparents the counting meshes it owns - mutating the children of a node being traversed.
     */
    private dropRegistrations(subjectId: string, root: THREE.Object3D): void {
        const subject = this.subjectAnimation(subjectId);
        const inside: THREE.Object3D[] = [];
        root.traverse((node) => inside.push(node));

        for (const node of inside) {
            if (node instanceof THREE.Mesh) {
                this.environment.shadowVolumes.remove(node);
            }

            subject.restPose.delete(node);
            subject.fileHidden.delete(node);

            // Only where THIS node is the one holding the name. `nodeByName` is first-wins, so one
            // part of a subject must not take another part's entry with it.
            const name = alamoNodeName(node);
            if (name !== "" && subject.nodeByName.get(name) === node) {
                subject.nodeByName.delete(name);
            }
        }

        const held = new Set(inside);
        this.billboards = this.billboards.filter((node) => !held.has(node));
    }

    private applyLevels(): void {
        // What the LEVEL knows, and nothing else. Deciding visibility here as well is what let two
        // systems write the same thing: this one re-derived every mesh from the old override maps
        // and undid whatever the row chain had just settled, so a mesh hidden from the tree came
        // straight back on the next level or material refresh.
        for (const entry of this.particles.particleSystems.values()) {
            entry.levelVisible = proxyVisibleAt(
                entry.levels,
                this.alt,
                this.lod,
                this.altDescending,
            );
        }

        // And what the stencil pass counts, which follows the level and the cloak rather than the
        // reader. It was set at LOAD and by the pass's own setup and nowhere else, so a cloak
        // engaged after the model was standing left all eight volumes casting - a shadow on the
        // ground under a ship that is not being drawn. An ALT change had the same hole.
        this.forEachPartMesh((mesh) => this.applyMeshVisibility(mesh));

        // A passive subject has no rows, so `refreshRows` below will never speak for it. Its own
        // file's rules are re-applied instead - the same sweep that ran when it loaded.
        for (const part of this.parts.values()) {
            if (part.subjectId !== ACTIVE_SUBJECT) {
                this.applyOwnFileVisibility(part.subjectId, part.root);
            }
        }

        this.refreshRows();
    }

    /**
     * Rebuilds the rows and re-runs the visibility chain over them.
     *
     * Building the rows is what discovers which scene objects each one owns, so the two happen
     * together - a chain run against stale rows would answer for meshes that are no longer loaded.
     */
    private refreshRows(): void {
        this.treeItems();
    }

    /**
     * Everything that can hide a mesh, decided together.
     *
     * Separate passes would fight each other: a level change would re-show a decal whose hardpoint
     * is intact, and re-hide one whose hardpoint had just been blown off.
     */
    /**
     * Applies BOTH halves of a sub-mesh's visibility: whether it is drawn, and - for a shadow
     * volume - whether it is counted.
     *
     * The two are separate questions with the same answer source. A shadow volume is never drawn,
     * so the drawn half always says no, while the level gating that decides which ALT's volume is
     * the live one still has to reach the stencil pass. Splitting them across two sweeps is what
     * let ALT1's volume mark a model showing ALT0.
     */
    private applyMeshVisibility(mesh: THREE.Mesh): void {
        // NOT the drawn state - that belongs to the row chain, which is the only writer. This keeps
        // the shadow-volume counting in step, which follows the LEVEL rather than the reader.

        const extras = (mesh.userData.alamo ?? {}) as MaterialExtras;

        if (resolveMaterial(extras).archetype === "shadow-volume") {
            // A cloaked unit casts nothing. The volume is never DRAWN either way, so this is the
            // only place the cloak can reach it - and a stencil shadow lying on the ground under an
            // invisible ship is exactly the "something else is still shown" the swap rules out.
            this.environment.shadowVolumes.setCounting(
                mesh,
                !this.stealthed && isVisibleAt(extras, this.alt, this.lod),
            );
        }
    }

    /**
     * Runs the visibility chain over every row and puts the answers on the scene.
     *
     * Parent before child, so a row can be told whether an ancestor already hid it without walking
     * anything twice. One pass, every time something changes - which is the point: the old model
     * had three override maps and three places that applied them, and they disagreed.
     */
    private resolveRows(): void {
        const resolutions = new Map<string, Resolution>();

        // Once, not once per row. This now runs on every frame of a playing clip, and `skeleton()`
        // rebuilds its list by walking the whole hull - which was affordable when this only ran on
        // a change and is not at sixty frames a second.
        const bones = new Map(
            this.skeleton().map((bone) => [bone.index, bone]),
        );

        // The SUBTREE answer, which a merged row needs as well as its own - see `subtreeFacts`.
        // Kept beside the row answer so a child asking about its ancestor gets the one that
        // actually applies to it.
        const subtree = new Map<string, Resolution>();

        // What the MESH half of a row does, which is a different question once an effect is merged
        // in: the marker mesh of a particle proxy is hidden in the file, and showing the effect
        // must not put that plate on screen.
        const meshAnswers = new Map<string, Resolution>();

        for (const [rowId, target] of this.rowTargets) {
            const facts = this.factsFor(rowId, target, subtree, bones);
            const answer = resolveRow(
                target.particleId === undefined
                    ? facts
                    : this.effectFacts(facts, target.particleId),
            );

            resolutions.set(rowId, answer);
            meshAnswers.set(rowId, resolveRow(facts));
            subtree.set(rowId, resolveRow(subtreeFacts(facts)));
        }

        this.rowResolutions = resolutions;
        this.rowSubtrees = subtree;

        for (const [rowId, target] of this.rowTargets) {
            const visible = resolutions.get(rowId)?.visible ?? true;

            if (target.mesh !== undefined) {
                setMeshDrawn(
                    target.mesh,
                    meshAnswers.get(rowId)?.visible ?? visible,
                );
            }

            if (target.boneIndex !== undefined) {
                const node = this.hull()?.bonesByIndex.get(target.boneIndex);

                // `visible` on a bone PRUNES ITS SUBTREE, so it takes the subtree answer, not the
                // row's. On a merged row the two differ: the file marking a proxy's marker mesh
                // hidden says nothing about the effect attached to that same bone.
                if (node !== undefined) {
                    node.visible = subtree.get(rowId)?.visible ?? visible;
                }
            }

            if (target.particleId !== undefined) {
                this.particles.setParticleSystemDrawn(
                    target.particleId,
                    visible,
                );
            }
        }
    }

    /** Everything the chain needs to know about one row. */
    private factsFor(
        rowId: string,
        target: { boneIndex?: number; mesh?: THREE.Mesh; particleId?: string },
        subtree: ReadonlyMap<string, Resolution>,
        bones: ReadonlyMap<number, FlatBone>,
    ): RowFacts {
        const parent = this.rowParents.get(rowId) ?? null;

        // The parent's SUBTREE answer, which is the one that reaches down here. Its own row answer
        // is about its mesh and stops there.
        const parentResolution =
            parent === null ? undefined : subtree.get(parent);

        const facts: RowFacts = {
            inFile: true,
            gated: false,
            // No masters here. The effects master belongs to the EFFECT half of a row and is added
            // by `effectFacts` - put on the base facts it would reach the marker mesh and, through
            // the subtree answer, the proxy bone, so switching effects off would prune geometry.
            masters: [],
            override: this.rowOverrides.get(rowId),
            ancestorHidden:
                parentResolution?.visible === false
                    ? (this.rowNames.get(parent ?? "") ?? "")
                    : undefined,

            // The ancestor as the MODEL leaves it, which is a different question: a bone the reader
            // unticked hides its subtree, but the model still draws every row in it. Without this
            // the whole subtree claimed the model did not draw it and went italic on one tick.
            ancestorHiddenAuthored:
                parentResolution?.authored === false
                    ? (this.rowNames.get(parent ?? "") ?? "")
                    : undefined,
        };

        if (target.mesh !== undefined) {
            const extras = (target.mesh.userData.alamo ?? {}) as MaterialExtras;

            const spec = resolveMaterial(extras);

            // The shield is the one archetype an ABILITY can talk the model into drawing, so its
            // `alamoHidden` is not final - and it has to be exempted in BOTH links, because the
            // level check reads the same flag. Exempting one alone changed nothing.
            // By the MESH, not the archetype: the shield shader is the engine glow's too on some
            // models, and archetype alone lit the engines with the ability.
            const shield = isShieldMesh(extras);

            // The stealth shell and the hull are one switch with two positions. The shell ships
            // VISIBLE, so `alamoHidden` says nothing about it and the cloak has to say everything:
            // off, the shell is not drawn; on, the shell is drawn and NOTHING else is.
            //
            // The level gate is left alone for it, unlike the shield's - `Ui_tyber` and
            // `Ui_urai_fen` ship `Stealth_LOD0/1/2`, and a shell exempted from LOD would draw all
            // three at once.
            const stealth = isStealthMesh(extras);

            facts.inFile = shield
                ? this.shieldRevealed
                : stealth
                  ? this.stealthed
                  : extras.alamoHidden !== true;

            facts.gated =
                spec.hidden ||
                !(shield
                    ? isVisibleAtLevel(extras, this.alt, this.lod)
                    : isVisibleAt(extras, this.alt, this.lod)) ||
                this.collisionGatedOff(extras) ||
                (this.stealthed && !stealth);

            // A damage decal's bottom of the chain is the DAMAGE RULE, not the file - see
            // `damageMeshFacts`. It used to enter as a `gated` veto beside the level gates, which
            // reached the same answer only because no shipped decal is marked hidden.
            const decal = (extras.alamoMesh ?? "").toLowerCase();
            if (this.knownDecals.has(decal)) {
                Object.assign(
                    facts,
                    damageMeshFacts(facts, this.shownDecals.has(decal)),
                );
            }

            // A SKINNED mesh rides no bone, so every track below missed it - see `skinVisibility`.
            // Only ever a hide, and only ever ONE of the two questions.
            //
            // Asking both cost Yoda his blade during every attack. The rule is a hide-or-silence,
            // so a clip that SHOWS the bones leaves `animated` undefined - and the idle's answer,
            // computed beside it, then hid the mesh from below. `attack_00` shows the saber bones
            // and `idle_00` hides them, so the resting pose was overruling the clip on screen.
            //
            // A resting pose is what the model does when nothing is playing. If something is
            // playing, that clip is the only one entitled to speak.
            const skin = this.skinBonesOf(target.mesh);

            if (skin.length > 0) {
                const playing = this.active.action !== null;
                const asked = skinHiddenByClip(
                    skin,
                    playing
                        ? (id) => this.animatedVisibilityOf(id)
                        : (id) => this.restingVisibilityOf(id),
                );

                if (asked === true) {
                    if (playing) {
                        facts.animated = false;
                    } else {
                        facts.resting = false;
                    }
                }
            }
        }

        if (target.boneIndex !== undefined) {
            const bone = bones.get(target.boneIndex);

            // Deliberately NOT `bone.visible`: that reads the scene node this chain WRITES, so a
            // bone hidden once resolved as hidden-in-the-file ever after and clearing the override
            // could never bring it back. A bone is always in the file - a bone row is a subtree
            // switch, and what the model says about it is said by the clip, below.
            // The canonical id off the NODE, never the display name the tree shows. Two bones can
            // share a name; only the id separates them - see `boneIds.ts`.
            const id = this.hull()?.bonesByIndex.get(target.boneIndex)?.name;

            facts.animated = this.animatedVisibilityOf(id);

            // Only when nothing is playing, which is what a resting pose MEANS and what the method
            // above it has always claimed. Computed unconditionally, the idle's opinion reached the
            // chain from below whenever the playing clip happened to be silent about a bone - so a
            // clip that shows a part the idle hides lost to the idle. Yoda's hilt is that: his
            // `attack_00` shows `b_hilt` and his `idle_00` hides it, and the hilt stayed away.
            facts.resting =
                this.active.action === null
                    ? this.restingVisibilityOf(id)
                    : undefined;
        }

        return facts;
    }

    /**
     * The same row, asked about the EFFECT it carries.
     *
     * A proxy row is a bone, a marker mesh and a system at once, and the three do not answer alike:
     * the file hides the marker plate, while what the file says about the EFFECT is whether the
     * game's own rules have it running. So the effect takes the row's ancestors, the reader's word
     * and the clip from the base facts, and supplies its own bottom of the chain.
     *
     * The rules and the levels are the two the rewrite dropped once already: a plume that only
     * lights when a hardpoint is destroyed was drawing from the moment the model opened, and a
     * proxy the current ALT/LOD gates off was drawing too.
     */
    private effectFacts(facts: RowFacts, systemId: string): RowFacts {
        const entry = this.particles.particleSystems.get(systemId);

        return {
            // Which drops the ancestors the MODEL hides: an effect answers to its own bone, as the
            // engine does. See `visibility.effectFacts`.
            ...effectRowFacts(facts),

            // The one master anyone legitimately keeps switched off while watching a clip: effects
            // are what obstructs the view of the thing being checked.
            masters: [{ id: "effects", on: this.particles.particlesVisible }],
            inFile: entry?.gateVisible ?? true,

            // The cloak, in the same chain that swaps the meshes. A cloaked unit shows its stealth
            // shell and nothing else - not its engines, not its damage smoke, not the proxies of
            // the ability doing the cloaking.
            //
            // Here rather than in the caller's effect decider, which was where it went first: that
            // put the flag in two places, and the two disagreed the moment anything drove one
            // without the other. `gated` and not `inFile` because it is a STATE, and because it
            // leaves `gateVisible` holding what the effect rules last decided - so uncloaking
            // restores exactly what was playing rather than a guess at it.
            gated:
                this.stealthed || (entry !== undefined && !entry.levelVisible),
        };
    }

    /** A mesh a hardpoint names as its collision hull, which the engine never draws. */
    private collisionGatedOff(extras: MaterialExtras): boolean {
        return this.collisionMeshes.has((extras.alamoMesh ?? "").toLowerCase());
    }

    /**
     * What a playing clip says about a bone on this frame, or undefined when it says nothing.
     *
     * Undefined is NOT false: a clip that never mentions a bone is not asking for it to be hidden,
     * and treating silence as a hide is how an animation came to blink meshes out.
     */
    /**
     * What the model's idle clip says about a bone when NOTHING is playing.
     *
     * Read at frame zero: this is a resting pose, not a performance. A model with no idle clip, or
     * an idle that says nothing about this bone, gets undefined and falls back to the file - the
     * rule the user set.
     */
    private restingVisibilityOf(id: BoneId | undefined): boolean | undefined {
        const clip = this.restingClipName();

        return clip === undefined ? undefined : this.trackedAt(clip, id, 0);
    }

    /**
     * Which clip the model rests in, worked out once per set of clips.
     *
     * It was copied, sorted and searched on every call - and this is asked once per bone row
     * per frame, now once per skin bone of every skinned mesh as well. The answer cannot
     * change while the subject's clips do not, so it is memoised against the map it reads.
     */
    private restingClipName(): string | undefined {
        const clips = this.active.visibility;

        if (this.restingClipFor !== clips) {
            this.restingClipFor = clips;
            this.restingClipCached = restingClip([...clips.keys()]);
        }

        return this.restingClipCached;
    }

    private animatedVisibilityOf(id: BoneId | undefined): boolean | undefined {
        return this.active.action === null
            ? undefined
            : this.trackedAt(
                  this.active.action.getClip().name,
                  id,
                  this.active.action.time,
              );
    }

    /**
     * The scene node for any way of naming a bone.
     *
     * The one place a name becomes a node. It used to be three different places with three
     * different rules - a lowercased map for cameras, a canonical-id map for the animation tracks,
     * a stripped-name lookup for the row chain - and a key that suited one of them matched nothing
     * in the others. `resolveBoneId` knows the fallbacks; nothing else needs to.
     */
    private boneNodeFor(key: string): THREE.Object3D | undefined {
        const hull = this.hull();

        if (hull === null) {
            return undefined;
        }

        const byId = new Map(
            [...hull.bonesByIndex.values()].map(
                (node) => [alamoNodeName(node), node] as const,
            ),
        );
        const id = resolveBoneId(byId.keys(), key);

        return id === undefined ? undefined : byId.get(id);
    }

    /**
     * What one clip's visibility track says about a bone at a moment, or undefined when it is
     * silent about it.
     *
     * One lookup for both the playing clip and the resting one, so the two cannot drift apart -
     * which is exactly how this broke before. The track is keyed by CANONICAL ID, and so is the
     * question; `resolveBoneId` is the fallback for a track written against a bare name.
     */
    /**
     * The canonical ids of every bone a mesh is weighted to, or empty when it is not skinned.
     *
     * Cached per mesh: this is asked on every frame of a playing clip, once per row, and the answer
     * cannot change - a skeleton's joint list is fixed when the model loads. Rebuilding the array
     * each time is the same mistake `resolveRows` records above about `skeleton()`.
     */
    private skinBonesOf(mesh: THREE.Mesh): readonly BoneId[] {
        const cached = this.skinBoneIds.get(mesh);

        if (cached !== undefined) {
            return cached;
        }

        const skinned = mesh as THREE.SkinnedMesh;
        const ids =
            skinned.isSkinnedMesh === true
                ? (skinned.skeleton?.bones ?? []).map((bone) =>
                      alamoNodeName(bone),
                  )
                : [];

        this.skinBoneIds.set(mesh, ids);

        return ids;
    }

    private trackedAt(
        clip: string,
        id: BoneId | undefined,
        seconds: number,
    ): boolean | undefined {
        const track = this.active.visibility.get(clip);

        if (id === undefined || track === undefined) {
            return undefined;
        }

        const key = track.bones.has(id)
            ? id
            : resolveBoneId(track.bones.keys(), id);
        const bits = key === undefined ? undefined : track.bones.get(key);

        return bits === undefined
            ? undefined
            : !hiddenAt(bits, track.fps, seconds);
    }

    /**
     * What a row carrying an effect is called.
     *
     * The `[heat]` tag goes on only when EVERY emitter is a heat distortion, which is the case that
     * draws nothing at all here. A system with one heat emitter among several still shows
     * something, and flagging it would read as a broken effect rather than an honest limitation.
     */
    private effectRowName(systemId: string): string {
        const entry = this.particles.particleSystems.get(systemId);
        if (entry === undefined) {
            return "";
        }

        const heat = entry.instance.emitterIsHeat();

        return (
            entry.system.name +
            (heat.length > 0 && heat.every(Boolean) ? " [heat]" : "")
        );
    }

    /**
     * Records what one row stands for.
     *
     * Called as each row is built, beside the row itself, so the two cannot drift apart. Everything
     * a row owns is applied from ONE answer - see `resolveRows`.
     */
    private claimRow(
        rowId: string,
        name: string,
        parentId: string | null,
        target: { boneIndex?: number; mesh?: THREE.Mesh; particleId?: string },
    ): void {
        this.rowTargets.set(rowId, target);
        this.rowParents.set(rowId, parentId);
        this.rowNames.set(rowId, name);
    }

    private refreshParticleVisibility(entry: ParticleEntry): void {
        // Deliberately does not decide anything itself. A system's row is in the chain like every
        // other row - the effects master vetoes it, its level gates it, the reader overrides it -
        // and a second opinion here would silently undo the first.
        void entry;
        this.refreshRows();
    }

    /**
     * A sphere around the subject.
     *
     * `includeEffects` separates two questions that were being answered with one number. Where the
     * CAMERA goes is about the model: a firing arc is a weapon's range and a smoke plume is where
     * the smoke has drifted to, and neither should push the hull into the distance. What the FAR
     * PLANE is set to is about reach: cull at the hull and a long trail is chopped off in mid-air,
     * which is what put this in in the first place.
     */
    private boundingSphere(includeEffects = false): BoundingSphere {
        // How far the particles will get. Their geometry is allocated full of zeros and filled in over
        // the following seconds, so a box around it measures a point at the origin however large the
        // effect is about to be - and a zero radius puts the camera on its own target, which draws
        // nothing at all, grid included.
        const particleReach = !includeEffects
            ? 0
            : Math.max(
                  0,
                  ...[...this.particles.particleSystems.values()].map((entry) =>
                      entry.instance.extent(),
                  ),
              );

        // Only what is on screen, and only geometry. See `modelBounds.ts` - the firing-arc cones
        // under this same root are thousands of units across and are not the model.
        //
        // The SHADOW volume is excluded even when it is on screen. It is an extruded silhouette,
        // routinely larger than the hull it belongs to and offset from it, so a model that draws
        // one framed itself around a shape nobody was looking at - which is what made the presets
        // look like they were choosing at random. Someone who ticks it on wants to SEE it, not to
        // have the camera measure the subject by it.
        const box = drawnBounds(
            this.modelRoot,
            (mesh) => this.drawn(mesh) && !isShadowVolume(mesh),
            // The arcs and handles are drawn and are meshes, so `drawn` alone let them in whenever
            // they were on screen: measured on the Gargantuan with its arcs showing, the framing
            // radius was 508 against a hull of 99, and every camera preset stood five times too far
            // off. The defect this function exists for, still open whenever the arcs were visible.
            isOverlay,
        );
        const sphere = box.isEmpty()
            ? { center: { x: 0, y: 0, z: 0 }, radius: 0 }
            : (() => {
                  const s = box.getBoundingSphere(new THREE.Sphere());
                  return {
                      center: { x: s.center.x, y: s.center.y, z: s.center.z },
                      radius: s.radius,
                  };
              })();

        // Guards NaN as well as zero: `>` is false for NaN, so the fallback wins.
        const radius = Math.max(particleReach, sphere.radius);
        return { center: sphere.center, radius: radius > 0 ? radius : 1 };
    }

    /**
     * Advances every PASSIVE subject's clip and applies what it says about visibility.
     *
     * The active subject is not here: its mixer is ticked by the loop and its visibility goes
     * through the ROW CHAIN, which is the thing that describes the model being previewed. A passive
     * subject has no rows, so its track is applied to its own nodes directly - safe now that
     * `nodeByName` is the subject's own rather than a scene-wide map the hull had filled.
     *
     * A mesh the FILE hides is never brought back by the clip: a shadow volume is not geometry that
     * happens to be switched off this frame.
     */
    private advancePassiveSubjects(dt: number): void {
        for (const [subjectId, subject] of this.subjects) {
            if (
                subjectId === ACTIVE_SUBJECT ||
                subject.action === null ||
                subject.mixer === null
            ) {
                continue;
            }

            subject.mixer.update(dt);

            const action = subject.action;
            const track = subject.visibility.get(action.getClip().name);

            if (track !== undefined) {
                // Every bone the track names, proxies included: a proxy carries only the marker
                // mesh the file already hides, and no effect is attached to it to be taken with it.
                // Emission is what the clip decides for a proxy (`applyEmitterGating`); what is
                // already in the air keeps drawing, exactly as it does in the engine.
                //
                // Through the subject's own name map rather than a traversal of its whole root:
                // this runs every frame, and a capital ship's wreck is a hundred nodes.
                for (const [name, bits] of track.bones) {
                    const node = subject.nodeByName.get(name);

                    if (node !== undefined && !subject.fileHidden.has(node)) {
                        node.visible = !hiddenAt(bits, track.fps, action.time);
                    }
                }
            }

            // The same gate the active subject gets, on this subject's own clip. Without it the
            // clip decides only what is SEEN: a burst switched off at this moment goes on burning,
            // and is spent long before the frame that was meant to set it off.
            this.particles.applyEmitterGating(subjectId, track, action);

            // Gone at the end of it. A death clone is what dying LOOKS like, not what is left
            // afterwards - the engine takes the object away when the clip is done.
            if (action.time >= action.getClip().duration) {
                // Its effects are released FIRST, and they outlive it. The Calamari clone's three
                // `p_explosion_huge00` ignite at 59.7s of a 60.1s clip - the detonation the whole
                // minute builds to - and hiding the part they are attached to cut them 0.4s in, so the
                // end of a death was 719 particles alive and NONE of them on screen.
                this.particles.releaseSubjectEffects(subjectId);

                for (const part of this.partsOf(subjectId)) {
                    this.setPartHidden(part.id, true);
                }
            }
        }
    }

    /**
     * Switches bones on and off for the frame the playing clip is on.
     *
     * Only the nodes this pass hides are touched, and every one of them is released the moment the
     * clip stops naming it - so a bone hidden by one clip cannot stay hidden into the next, and a
     * bone the reader hid by hand in the tree is never quietly shown again by an animation ending.
     */
    private applyAnimatedVisibility(): void {
        // Looked up ONCE: this runs on every frame of a playing clip, and so does the emitter
        // gating below, which asks per particle system.
        const active = this.active;
        const track =
            active.action === null
                ? undefined
                : active.visibility.get(active.action.getClip().name);

        if (track !== undefined && active.action !== null) {
            // Through the ROW CHAIN, which is the only thing that writes visibility - the clip is a
            // link in it (`animated`), above the level gates and the file. This used to write
            // `node.visible` here instead, straight past the chain, and the two disagreed about how
            // a track is keyed: tracks by glTF NODE name, the chain by BONE name. Whichever of them
            // matched was the one that worked, and re-keying the tracks silently swapped which.
            //
            // The symptom that gave it away: with the per-frame writer missing its lookup, the only
            // resolve was the one `play` does at frame 0 - so a muzzle whose track began `1111` sat
            // on for the whole clip and the one beginning `0000` never lit.
            this.resolveRows();
            this.overridingVisibility = true;
        } else if (this.overridingVisibility) {
            this.overridingVisibility = false;
            this.applyLevels();
        }

        this.particles.applyEmitterGating(ACTIVE_SUBJECT, track, active.action);
    }

    /**
     * Whether a mesh actually reaches the screen: its own flag AND every ancestor's.
     *
     * A mesh with no ALT or LOD tag is always drawn unless it is hidden itself or is attached to a hidden
     * bone - and that second half is the part `node.visible` alone cannot answer, because three
     * stops at the first invisible ancestor without touching the flag on anything below it. Counting
     * only the node's own flag reported meshes as drawn while the screen showed nothing.
     */
    private drawn(mesh: THREE.Mesh): boolean {
        // The mesh's own layer, then every ancestor's `visible`. Two mechanisms because they mean
        // different things: a mesh switches ITSELF off without taking its bones with it, a bone
        // switches off everything hanging beneath it. See `meshVisibility.ts`.
        if (!meshDrawn(mesh)) {
            return false;
        }

        for (
            let at: THREE.Object3D | null = mesh;
            at !== null;
            at = at.parent
        ) {
            if (!at.visible) {
                return false;
            }

            if (at === this.modelRoot) {
                break;
            }
        }

        return true;
    }

    /** Where the climb for an emission mesh has to stop: the model the system was attached to. */
    private partRootOf(partId?: string): THREE.Object3D | null {
        return partId === undefined
            ? this.modelRoot
            : (this.parts.get(partId)?.root ?? null);
    }

    /**
     * The part the skeleton view describes: the ACTIVE subject's root.
     *
     * Everything that describes "the model" - the tree, the stats, the skeleton, the row chain, the
     * bone labels, the camera bindings - asks for this one. It used to be the first part in the
     * scene, full stop, which was the hull only by the luck of load order; now it is the first part
     * OF THE SUBJECT, and a wreck or a clone arriving cannot be mistaken for it.
     */
    private hull(): LoadedPart | null {
        return this.rootPartOf(ACTIVE_SUBJECT);
    }

    /** The first-loaded part of one subject - its hull. */
    private rootPartOf(subjectId: string): LoadedPart | null {
        for (const part of this.parts.values()) {
            if (part.subjectId === subjectId) {
                return part;
            }
        }

        return null;
    }

    /** Every part belonging to one subject. */
    private partsOf(subjectId: string): LoadedPart[] {
        return [...this.parts.values()].filter(
            (part) => part.subjectId === subjectId,
        );
    }

    /**
     * Draws a passive subject's geometry by its OWN file's rules, at the current levels.
     *
     * The row chain speaks only for the ACTIVE subject, so this is the whole of what decides
     * whether a wreck's mesh is on screen. It runs when the geometry loads AND whenever the levels
     * change: applied once and never again, a death clone kept whatever detail level happened to be
     * set at the moment it arrived, and moving the control afterwards left it behind. Measured over
     * the shipped tree that is one ship - the Broadside Class Cruiser, whose clone carries a `_LOD1`
     * mesh and whose hull declares the levels that make the control live - but the engine plainly
     * supports it and a mod authoring an ALT-tagged wreck is exactly who would be confused.
     *
     * Through {@link setMeshDrawn}, never `visible`: a mesh node is very often a bone node too, and
     * `visible` prunes the whole subtree under it. See `meshVisibility.ts`.
     *
     * A node the RUNNING CLIP names is left alone. The clip is the model's own word about this
     * frame and it sits above the level gate, which is the same precedence `resolveRow` gives the
     * active subject.
     */
    private applyOwnFileVisibility(
        subjectId: string,
        root: THREE.Object3D,
    ): void {
        const subject = this.subjectAnimation(subjectId);
        const action = subject.action;
        const track =
            action === null
                ? undefined
                : subject.visibility.get(action.getClip().name);

        root.traverse((node) => {
            if (
                !(node instanceof THREE.Mesh) ||
                node.userData.aetShadowVolume === true
            ) {
                return;
            }

            if (track?.bones.has(alamoNodeName(node)) === true) {
                return;
            }

            const extras = (node.userData.alamo ?? {}) as MaterialExtras;

            setMeshDrawn(
                node,
                !resolveMaterial(extras).hidden &&
                    isVisibleAt(extras, this.alt, this.lod),
            );
        });
    }

    /** One subject's animation state, created the first time it is asked for. */
    private subjectAnimation(subjectId: string): SubjectAnimation {
        const held = this.subjects.get(subjectId);

        if (held !== undefined) {
            return held;
        }

        const made: SubjectAnimation = {
            mixer: null,
            clips: [],
            visibility: new Map(),
            nodeByName: new Map(),
            restPose: new Map(),
            action: null,
            fileHidden: new Set(),
        };

        this.subjects.set(subjectId, made);

        return made;
    }

    /**
     * How big the subject is, across its diagonal, cached until the scene changes.
     *
     * `Box3.setFromObject` walks every vertex, so anything asking per frame - the bone axes did -
     * turns a size lookup into a full geometry traversal. The hull does not change size while it is
     * loaded; an animation moves it. The one thing that does change it is `setModelScale`, which
     * drops the cache, because the span is measured in WORLD units.
     */
    private modelSpan(): number {
        if (this.cachedSpan !== null) {
            return this.cachedSpan;
        }

        const hull = this.hull();
        if (hull === null) {
            return 100;
        }

        // Every mesh the hull carries, drawn or not, as before - but NOT the overlays hung inside it.
        // `setFromObject` measured those too, and on the Gargantuan with its arcs on the span came out
        // at 1015 against a hull of 204. The span sizes the turret handles and caps how long an arc
        // is drawn, so that fed back: handles sized against the arcs, arcs capped against themselves.
        const bounds = drawnBounds(hull.root, () => true, isOverlay);
        this.cachedSpan = bounds.isEmpty()
            ? 100
            : bounds.getSize(new THREE.Vector3()).length();

        return this.cachedSpan;
    }

    private onPointerDown = (event: PointerEvent): void => {
        if (this.turret.grabKnob(event)) {
            return;
        }

        if (!this.overlay.skeletonShown || this.onBoneSelected === null) {
            return;
        }

        const hull = this.hull();
        if (hull === null) {
            return;
        }

        const rect = this.canvas.getBoundingClientRect();
        const x = event.clientX - rect.left;
        const y = event.clientY - rect.top;

        let best: { index: number; distance: number } | null = null;
        const world = new THREE.Vector3();

        for (const [index, node] of hull.bonesByIndex) {
            node.getWorldPosition(world);
            const ndc = world.project(this.camera);
            if (ndc.z > 1) {
                continue;
            }

            const distance = Math.hypot(
                (ndc.x * 0.5 + 0.5) * rect.width - x,
                (-ndc.y * 0.5 + 0.5) * rect.height - y,
            );

            if (best === null || distance < best.distance) {
                best = { index, distance };
            }
        }

        // Only a deliberate click on a joint counts. Without a threshold, every drag of the camera
        // would also reselect whichever bone happened to be nearest the cursor.
        if (best !== null && best.distance <= JOINT_PICK_RADIUS) {
            this.onBoneSelected(best.index);
        }
    };

    private loop = (): void => {
        if (this.disposed) {
            return;
        }

        this.frameHandle = requestAnimationFrame(this.loop);

        const dt = this.clock.getDelta();
        this.elapsed += dt;
        this.active.mixer?.update(dt);
        this.applyAnimatedVisibility();
        // After the mixer, so a clip that drives the same bone wins: an animation is the model's own
        // word about where the turret is, and the sweep is the reader asking a question about it.
        this.turret.advanceTurretSweep(dt);
        this.advanceBreakoffs(dt);
        this.advanceSpinAway(dt);
        // LAST among the things that move bones: the clip, the sweep and the spin-away have all had
        // their say, so the muzzle this reads is the one that will be drawn.
        this.turret.followHullArcs();
        // Same reason and same place: the turret bones have been moved for this frame, so the
        // tracks can now be put back in the frames their angles are measured against.
        this.turret.placeTurretAnchors();

        this.advancePassiveSubjects(dt);
        this.controls.update();

        this.particles.step(dt, this.camera.matrixWorldInverse);

        this.overlay.beforeCamera(this.active.mixer !== null);
        this.prepareForCamera();
        // After the camera's matrix is updated, so the marks are projected through THIS frame's view.
        // See `OverlayLayer.afterCamera`.
        this.overlay.afterCamera();

        this.environment.renderFrame();
    };

    /**
     * Everything that has to be recomputed for the camera AS IT STANDS, before anything is drawn.
     *
     * Its own method because there are two callers and they must not drift: the animation loop, and
     * `capture`, which moves the camera and changes the aspect before rendering a single frame.
     * Capture used to call `renderFrame` on its own, and the result did not represent the scene at
     * all - the translated materials still held the PREVIOUS frame's projection, so every mesh
     * drawn by one landed in the wrong place or off the edge while the floor and the grid, which
     * are ordinary three materials, drew correctly against the new one. Meshes ghosting off to the
     * side with the back and the engines missing is exactly what a stale WORLDVIEWPROJECTION looks
     * like.
     *
     * Time is NOT advanced here. Nothing in this method simulates anything - it only answers "where
     * is the camera now", which is the one thing a capture changes.
     */
    private prepareForCamera(): void {
        this.camera.updateMatrixWorld();

        // The engine's per-frame contract, including the matrices. Last, not first, so it sees the
        // bone transforms the animation and skeleton work above have already settled.
        this.materials.updateAlamoUniforms();
        this.updateBillboards();

        // The sky dome travels with the camera and keeps its own orientation, so the horizon stays
        // level and infinitely far however the camera is turned.
        if (this.environment.sky !== null && this.environment.sky.visible) {
            this.environment.sky.position.copy(this.camera.position);
            this.environment.sky.scale.setScalar(
                Math.max(this.camera.near * 4, this.camera.far * 0.4),
            );
        }

        // The stars need the same treatment, and for a reason that took a regression to notice:
        // parenting them to the CAMERA exempts them from frustum CULLING but not from the far
        // plane, which clips their vertices like anything else's. The far plane is fitted to the
        // model, so the moment the hardpoint rotation was corrected and the bounds shrank, the
        // whole sky fell outside it and the stars simply went out.
        if (this.environment.stars !== null && this.environment.stars.visible) {
            this.environment.stars.scale.setScalar(
                Math.max(this.camera.near * 4, this.camera.far * 0.4),
            );
        }
    }

    /**
     * Turns each billboarded bone's geometry to face whatever it is meant to face.
     *
     * On the MESH rather than on the bone, which is what the engine does: `DoBillboard` composes the
     * rotation onto the sub-mesh's world matrix as it draws, so the bone still supplies the position
     * and its child bones are left exactly where the file put them. Rotating the bone itself would
     * swing a whole limb round with the card.
     */
    private updateBillboards(): void {
        if (this.billboards.length === 0) {
            return;
        }

        // The direction the light TRAVELS, which is the reverse of where it stands.
        const light = BILLBOARD_LIGHT.copy(
            lightDirection(
                this.environment.lightAzimuth,
                this.environment.lightElevation,
            ),
        ).negate();

        for (const node of this.billboards) {
            const type = billboardTypeOf(node);
            if (type === null) {
                continue;
            }

            node.updateWorldMatrix(true, false);
            node.getWorldPosition(BILLBOARD_AT);
            node.getWorldQuaternion(BILLBOARD_BONE);

            const world = billboardRotation(
                type,
                this.camera,
                light,
                BILLBOARD_AT,
            );

            for (const child of node.children) {
                if (child instanceof THREE.Mesh) {
                    // The billboard turns what the node ALREADY had, the way
                    // `ObjectTemplate::DoBillboard` does - it must not replace the world rotation,
                    // because that throws away the exporter's Z-up-to-Y-up root correction and lays
                    // every tree's shadow card flat. See `billboardLocalRotation`.
                    billboardLocalRotation(
                        BILLBOARD_BONE,
                        world,
                        child.quaternion,
                    );
                }
            }
        }
    }

    /**
     * Sets the depth range from the model, its effects and the reader's own draw distance.
     *
     * The camera is framed on the MODEL - fitting it to the effects would push a trooper into the
     * distance to make room for his flamethrower - but the far PLANE has to clear both, or the
     * flame is culled halfway along and reads as an effect that stops.
     */
    private applyClipPlanes(sphere: BoundingSphere, distance: number): void {
        const planes = clipPlanes(
            sphere,
            distance,
            this.particles.effectReach(),
            this.drawDistance,
        );

        this.camera.near = planes.near;
        this.camera.far = planes.far;
        this.camera.updateProjectionMatrix();
    }
}

function decodeBase64(value: string): ArrayBuffer {
    const binary = atob(value);
    const bytes = new Uint8Array(binary.length);
    for (let i = 0; i < binary.length; i++) {
        bytes[i] = binary.charCodeAt(i);
    }
    return bytes.buffer;
}

/**
 * The root of something this viewport DRAWS OVER the model - a firing arc, a turret handle - rather
 * than part of it.
 *
 * One flag for every overlay, checked everywhere the model is walked or measured: the mesh walk the
 * tree, the stats, the shader rebuild, the wireframe and the tint go through, plus the framing sphere
 * and the subject span. Both kinds of overlay hang INSIDE the model on bones, and both are meshes, so
 * neither a type test nor scoping to the part roots keeps them out - the same trap the particle
 * systems fell into first.
 *
 * Measured on the Gargantuan with both showing, before this: 24 overlay meshes walked as geometry,
 * 24 phantom tree rows, shader coverage counting 35 meshes where the model has 11, the knob handed a
 * model material on a shader rebuild and swept by the wireframe, the span five times the hull's and
 * the camera framing five times too far.
 */
function isOverlay(node: THREE.Object3D): boolean {
    return node.userData.aetOverlay === true;
}

/** A mesh the exporter marked as a shadow volume, rather than part of the model's own shape. */
function isShadowVolume(mesh: THREE.Mesh): boolean {
    const extras = (mesh.userData.alamo ?? {}) as MaterialExtras;

    return resolveMaterial(extras).archetype === "shadow-volume";
}

/** Releases GPU memory for a subtree; the renderer does not do it on removal. */
function disposeTree(root: THREE.Object3D): void {
    root.traverse((node) => {
        if (!(node instanceof THREE.Mesh)) {
            return;
        }

        node.geometry.dispose();
        for (const material of Array.isArray(node.material)
            ? node.material
            : [node.material]) {
            material.dispose();
        }
    });
}

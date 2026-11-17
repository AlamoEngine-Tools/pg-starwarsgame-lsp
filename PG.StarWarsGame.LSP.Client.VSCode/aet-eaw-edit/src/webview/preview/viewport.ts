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
    FIELD_OF_VIEW,
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
import { type SubjectAnimation } from "./subjectAnimation";
import { drawnBounds, isOverlay, triangleCount } from "./modelBounds";
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
import {
    ACTIVE_SUBJECT,
    BREAKOFF_ATTACHMENT,
    type LoadedPart,
} from "./loadedPart";
import { type ParticleEntry } from "./particleEntry";

export { ACTIVE_SUBJECT, BREAKOFF_ATTACHMENT };
import { VIEWPORT_BACKGROUND } from "./backdrop";
export { VIEWPORT_BACKGROUND };
import { TurretRig } from "./turretRig";
import { ParticleHost } from "./particleHost";
import { MaterialBinder } from "./materialBinder";
import { EnvironmentRig } from "./environmentRig";
import { ModelRows } from "./modelRows";
import { CameraRig } from "./cameraRig";
import { disposeTree, PartAssembly } from "./partAssembly";

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
    /** The model tree's rows: what each row stands for, its facts, and the reader's overrides. See `ModelRows`. */
    private readonly rows: ModelRows;
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
    /** The framing: how far the camera sits, what it is fitted to, the model's own cameras and the clip planes. See `CameraRig`. */
    private readonly cameraRig: CameraRig;
    private alt = 0;
    private lod = 0;
    /** Labels, reticles, selection boxes, the skeleton and the bone axes. See `OverlayLayer`. */
    private readonly overlay: OverlayLayer;
    /** The firing arcs, the turret handles, the sweeps and the hand-set aims. See `TurretRig`. */
    private readonly turret: TurretRig;
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
    /** Wreckage currently in the air, each on its own clock. */
    private readonly breakoffs: BreakoffInstance[] = [];
    /** The parts: loading one into the scene, attaching it to its bone, hiding and removing it, and walking their meshes. See `PartAssembly`. */
    private readonly assembly: PartAssembly;
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
        this.assembly = new PartAssembly({
            get alt() {
                return viewport.alt;
            },
            get billboards() {
                return viewport.billboards;
            },
            get environment() {
                return viewport.environment;
            },
            get loader() {
                return viewport.loader;
            },
            get lod() {
                return viewport.lod;
            },
            get materials() {
                return viewport.materials;
            },
            get modelRoot() {
                return viewport.modelRoot;
            },
            get particles() {
                return viewport.particles;
            },
            get parts() {
                return viewport.parts;
            },
            get scaledRoot() {
                return viewport.scaledRoot;
            },
            get subjects() {
                return viewport.subjects;
            },
            applyLevels: () => viewport.applyLevels(),
            bindLoadedTextures: (root) => viewport.bindLoadedTextures(root),
            dropRegistrations: (subjectId, root) =>
                viewport.dropRegistrations(subjectId, root),
            removeParticleSystem: (id, immediate) =>
                viewport.removeParticleSystem(id, immediate),
            setColorization: (colour) => viewport.setColorization(colour),
            subjectAnimation: (subjectId) =>
                viewport.subjectAnimation(subjectId),
        });
        // First: the environment places its key light against the framing sphere as it is built.
        this.cameraRig = new CameraRig({
            get camera() {
                return viewport.camera;
            },
            get controls() {
                return viewport.controls;
            },
            get environment() {
                return viewport.environment;
            },
            get materials() {
                return viewport.materials;
            },
            get modelRoot() {
                return viewport.modelRoot;
            },
            get particles() {
                return viewport.particles;
            },
            get renderer() {
                return viewport.renderer;
            },
            drawn: (mesh) => viewport.drawn(mesh),
            hull: () => viewport.assembly.hull(),
            updateBillboards: () => viewport.updateBillboards(),
        });
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
                viewport.cameraRig.boundingSphere(includeEffects),
            forEachDrawnMesh: (visit) =>
                viewport.assembly.forEachDrawnMesh(visit),
            skinPalette: (mesh) => viewport.skinPalette(mesh),
        });
        this.rows = new ModelRows({
            get active() {
                return viewport.active;
            },
            get alt() {
                return viewport.alt;
            },
            get collisionMeshes() {
                return viewport.collisionMeshes;
            },
            get knownDecals() {
                return viewport.knownDecals;
            },
            get lod() {
                return viewport.lod;
            },
            get particles() {
                return viewport.particles;
            },
            get shieldRevealed() {
                return viewport.shieldRevealed;
            },
            get shownDecals() {
                return viewport.shownDecals;
            },
            get stealthed() {
                return viewport.stealthed;
            },
            forEachPartMesh: (visit) =>
                viewport.assembly.forEachPartMesh(visit),
            hull: () => viewport.assembly.hull(),
            skeleton: () => viewport.skeleton(),
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
                    return viewport.rows.rowParents;
                },
                get rowTargets() {
                    return viewport.rows.rowTargets;
                },
                get onReticleClicked() {
                    return viewport.onReticleClicked;
                },
                bonePosition: (partId, bone) =>
                    viewport.bonePosition(partId, bone),
                boundingSphere: (includeEffects) =>
                    viewport.cameraRig.boundingSphere(includeEffects),
                hull: () => viewport.assembly.hull(),
                modelSpan: () => viewport.cameraRig.modelSpan(),
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
            modelSpan: () => viewport.cameraRig.modelSpan(),
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
            forEachDrawnMesh: (visit) =>
                viewport.assembly.forEachDrawnMesh(visit),
            forEachPartMesh: (visit) =>
                viewport.assembly.forEachPartMesh(visit),
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
                return viewport.cameraRig.fitDistance;
            },
            get cameraPosition() {
                return viewport.cameraRig.cameraPosition;
            },
            get modelRoot() {
                return viewport.modelRoot;
            },
            get parts() {
                return viewport.parts;
            },
            get hiddenParts() {
                return viewport.assembly.hiddenParts;
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
                viewport.cameraRig.applyClipPlanes(sphere, distance),
            attachmentFor: (partId, bone, boneIndex) =>
                viewport.assembly.attachmentFor(partId, bone, boneIndex),
            boundingSphere: (includeEffects) =>
                viewport.cameraRig.boundingSphere(includeEffects),
            partRootOf: (partId) => viewport.assembly.partRootOf(partId),
            refreshParticleVisibility: (entry) =>
                viewport.refreshParticleVisibility(entry),
            refreshRows: () => viewport.rows.refreshRows(),
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
        return this.cameraRig.cameraPosition;
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
        return this.cameraRig.canvasCoverage();
    }

    /** The bounding sphere a preset is resolved against, so the caller sizes the shot correctly. */
    get subjectSphere(): { center: Vector3Like; radius: number } {
        return this.cameraRig.subjectSphere;
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
        return this.assembly.addPart(
            id,
            glbBase64,
            attachToPartId,
            attachBone,
            options,
        );
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
        return this.assembly.unresolvedAttachments();
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
        this.assembly.attachRequests.clear();
        this.cameraRig.forgetSpan();
        // The marks name parts that are about to stop existing; leaving them would draw a reticle
        // over the next model's geometry at whatever the last one's bounds happened to be.
        this.overlay.dropReticles();
        this.parts.clear();
        this.billboards = [];
        this.assembly.hiddenParts.clear();
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
        this.cameraRig.forgetSpan();

        this.turret.applyModelScale();

        this.cameraRig.applyClipPlanes(
            this.cameraRig.boundingSphere(),
            this.cameraRig.fitDistance,
        );
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
        const anchor = this.assembly.attachmentFor(
            at.partId,
            at.bone,
            at.boneIndex,
        );
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
        this.assembly.setPartHidden(partId, hidden);
    }

    /**
     * Removes one loaded part and frees its geometry.
     *
     * For parts that come and go with a STATE rather than with the subject - a death clone appears
     * when the ship dies and goes again when it is repaired. A scene part is never removed this
     * way; it lives as long as the subject does and is hidden instead.
     */
    removePart(partId: string): void {
        this.assembly.removePart(partId);
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
        return this.rows.treeItems();
    }

    /**
     * The row that owns one effect.
     *
     * A merged proxy row is anchored on its bone, so an effect's row id is not derivable from its
     * system id. Asked of the row table, which is the only thing that knows.
     */
    rowForSystem(systemId: string): string {
        return this.rows.rowForSystem(systemId);
    }

    /**
     * What a tree row actually is, for the inspector.
     *
     * A list because a merged row is two things at once. Empty for a row the tree no longer has -
     * a selection can outlive a reload or a level change.
     */
    inspectionOf(id: string): Inspection[] {
        return this.rows.inspectionOf(id);
    }

    /**
     * The rows the reader has switched off by hand.
     *
     * The OVERRIDES, not what happens to be invisible: a mesh gated off by the current damage level
     * is not something anyone chose, and writing it down would turn a level's own rule into an
     * explicit hide that outlives the level. Restorable now that a mesh key survives a reload.
     */
    hiddenItemIds(): string[] {
        return this.rows.hiddenItemIds();
    }

    /**
     * The rows the reader has switched ON by hand.
     *
     * Both directions are carried now. A mesh is usually drawn until someone hides it, but a
     * shadow volume or a collision hull is gated off until someone asks for it - and asking for it
     * is exactly the deliberate act worth surviving a reopen.
     */
    shownItemIds(): string[] {
        return this.rows.shownItemIds();
    }

    /**
     * Shows or hides any mix of bones, meshes and particle systems at once.
     *
     * One entry point, because the tree selects across kinds and a multi-select toggle has to move
     * all of them together. A bone carries what is attached to it, so switching one off takes its
     * subtree with it - which is what the reader is asking for when they untick a limb.
     */
    setItemsVisible(ids: readonly string[], visible: boolean): void {
        this.rows.setItemsVisible(ids, visible);
    }

    /**
     * Records the reader's word on a set of rows and re-runs the chain.
     *
     * `null` gives a row back to the model. One entry point and one map, because the old code kept
     * three - meshes by tree key, bones by index, systems by id - and a row anchored on a bone's id
     * while standing for a mesh wrote one and reported the other.
     */
    applyRowOverrides(changes: readonly OverrideChange[]): void {
        this.rows.applyRowOverrides(changes);
    }

    /** What the reader has said about each row, for carrying across a reopen. */
    rowOverrideEntries(): { row: string; override: RowOverride }[] {
        return this.rows.rowOverrideEntries();
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
        this.rows.clearRowOverrides();
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
        this.rows.releaseAbilityRows(ownership);
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
        return this.cameraRig.applyModelCamera(name);
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
        this.cameraRig.applyCameraPose(position, target);
    }

    frameAll(preset: PresetView = "threeQuarter"): void {
        this.cameraRig.frameAll(preset);
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
            parts: this.assembly.activeSubjectRoots().length,
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
        this.assembly.noteAttachment(partId, bone);

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
        return this.assembly.hasPart(partId);
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
        const hull = this.assembly.hull();
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
        const hull = this.assembly.hull();
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

            this.cameraRig.prepareForCamera();

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
            this.cameraRig.prepareForCamera();
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
        this.cameraRig.setDrawDistance(multiplier);
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
            this.assembly.attachmentFor(partId, bone)
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
        this.assembly.forEachPartMesh((mesh) => this.applyMeshVisibility(mesh));

        // A passive subject has no rows, so `refreshRows` below will never speak for it. Its own
        // file's rules are re-applied instead - the same sweep that ran when it loaded.
        for (const part of this.parts.values()) {
            if (part.subjectId !== ACTIVE_SUBJECT) {
                this.assembly.applyOwnFileVisibility(part.subjectId, part.root);
            }
        }

        this.rows.refreshRows();
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

    private refreshParticleVisibility(entry: ParticleEntry): void {
        // Deliberately does not decide anything itself. A system's row is in the chain like every
        // other row - the effects master vetoes it, its level gates it, the reader overrides it -
        // and a second opinion here would silently undo the first.
        void entry;
        this.rows.refreshRows();
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

                for (const part of this.assembly.partsOf(subjectId)) {
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
            this.rows.resolveRows();
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

    private onPointerDown = (event: PointerEvent): void => {
        if (this.turret.grabKnob(event)) {
            return;
        }

        if (!this.overlay.skeletonShown || this.onBoneSelected === null) {
            return;
        }

        const hull = this.assembly.hull();
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
        this.cameraRig.prepareForCamera();
        // After the camera's matrix is updated, so the marks are projected through THIS frame's view.
        // See `OverlayLayer.afterCamera`.
        this.overlay.afterCamera();

        this.environment.renderFrame();
    };

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
}

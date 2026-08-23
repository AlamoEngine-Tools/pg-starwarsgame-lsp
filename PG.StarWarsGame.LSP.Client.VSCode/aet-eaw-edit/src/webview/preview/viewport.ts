// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The three.js side of the preview, kept imperative and apart from React.
//
// Mixing a render loop into a component means every state change risks tearing down a GPU context,
// and the two lifecycles do not line up: React re-renders on a checkbox, the scene should not. The
// component owns a canvas and calls into this; nothing here knows React exists.

import * as THREE from 'three';
import { GLTFLoader } from 'three/examples/jsm/loaders/GLTFLoader.js';
import { OrbitControls } from 'three/examples/jsm/controls/OrbitControls.js';

import {
    clipPlanes, frameSphere, DEFAULT_AZIMUTH_DEGREES, DEFAULT_ELEVATION_DEGREES,
    type BoundingSphere,
} from './framing';
import type { AlamoParticleContent } from '../../protocol/modelPreview';
import type { NormalisedColour } from './colour';
import { attachmentProblem, type AttachmentRequest } from './attachments';
import { drawnConeRange, fireConeOutline, fireConeSurface } from './fireArc';
import type { FxMaterialState } from './fx/renderState';
import { definedLevels, proxyVisibleAt, type DefinedLevels, type LevelTagged } from './levels';
import { AlamoMaterial } from './alamoMaterial';
import type { TranslatedEffect } from './fx/effect';
import { engineLight, neutralHarmonics, type AlamoFrame } from './fx/uniforms';
import {
    castsShadowMap, debugColour, isShieldMesh, isVisibleAt, isVisibleAtLevel, resolveMaterial,
    shieldMeshOffShader,
    type MaterialExtras, type MaterialSpec,
} from './materials';
import {
    lightDirection, shadowFrustum,
    DEFAULT_LIGHT_AZIMUTH_DEGREES, DEFAULT_LIGHT_ELEVATION_DEGREES,
} from './lighting';
import {
    cancelRootCorrection, outermostRoots, splitGeometryFromBone, stampTreeKeys, treeKeyOf,
} from './boneNodes';
import { meshDrawn, setMeshDrawn } from './meshVisibility';
import { boneRowId, effectPlacement, meshPlacement, type TreeItem } from './previewTree';
import { healthColour, reticleSizePx, type ReticleMark } from './reticles';
import { breakoffLifetime, breakoffPose, type BreakoffAnchor } from './breakoff';
import type { PreviewBreakoffProp } from '../../protocol/modelPreview';
import { sweepAngles } from './deathClone';
import type { PreviewTurret } from '../../protocol/modelPreview';
import { type Inspection } from './inspector';
import { emissionMeshFor } from './emissionSource';
import { billboardRotation, billboardTypeOf } from './billboards';
import { hiddenAt, visibilityTracks, type VisibilityTrack } from './boneVisibility';
import { drawnBounds } from './modelBounds';
import { HeatPass } from './heatPass';
import { BloomPass } from './bloomPass';
import { ShadowVolumePass } from './shadowVolumePass';
import { applyBlend, applyDepth } from './materialState';
import { resolveBoneId, type BoneId } from './boneIds';
import {
    damageMeshFacts, deathClip, effectFacts as effectRowFacts, resolveRow, restingClip, subtreeFacts,
    type OverrideChange, type Resolution, type RowFacts, type RowOverride,
} from './visibility';
import { boxRoots, rowsInBox } from './selectionBox';
import { type Vector3 as Vector3Like } from './lighting';
import { skyGradient, starPositions } from './backdrop';
import { GROUND_TEXTURE_SIZE, concreteNoise } from './groundTexture';
import { missingTexture } from './missingTexture';
import {
    DEFAULT_LIGHTS, DEFAULT_VIEWER_SETTINGS, type BackgroundKind, type LightRig, type Wind,
} from './viewerSettings';
import { HEAT_LAYER, ParticleSystemInstance } from './particleRenderer';

/**
 * The firing-arc gizmo's look.
 *
 * Unlit and never writing depth, because it is an annotation drawn over a model rather than a thing
 * in the scene.
 */
/**
 * The fill of a firing cone.
 *
 * Translucent and double-sided, with no depth WRITE: fifty overlapping cones on a capital ship must
 * not occlude each other or the hull they describe, and the reader is usually looking at the model
 * through them. Additive was tried in spirit and rejected - fifty stacked cones saturate to white
 * and the hull disappears inside its own arcs.
 */
const FIRE_ARC_MATERIAL = new THREE.MeshBasicMaterial({
    color: 0xff9d3c,
    transparent: true,
    // Faint on purpose. The outline is what carries the shape now, so the fill only has to suggest
    // a body - and six overlapping banks at 0.10 still stacked up into a solid orange field.
    opacity: 0.05,
    depthWrite: false,
    side: THREE.DoubleSide,
});

/** How many segments go round a cone's rim. Enough to read as a cone, cheap enough for fifty. */
const FIRE_ARC_SEGMENTS = 24;

/** Rings from the axis to the rim. Enough to hold a wide cap's curve without being a mesh.  */
const FIRE_ARC_RINGS = 5;

/** Ribs from the apex to the rim. Every rim point would be a solid disc of lines. */
const FIRE_ARC_RIBS = 4;

/**
 * The cone's EDGES, drawn over the fill.
 *
 * The same hue at full strength. The fill alone gives a cone no boundary - at 10% opacity a wide
 * arc is a soft haze, and fifty overlapping ones are one field - so the outline is what says where
 * each arc actually stops. Depth-tested like the fill, so an edge behind the hull stays behind it.
 */
const FIRE_ARC_OUTLINE_MATERIAL = new THREE.LineBasicMaterial({
    color: 0xff9d3c,
    transparent: true,
    opacity: 0.55,
    depthWrite: false,
});

/**
 * A cone from a fire bone's origin out to its range: a faint fill inside a drawn outline.
 *
 * Both, because each alone fails at one end of the corpus. A filled volume on its own drowns the
 * ship - a Star Destroyer's arcs are 160 degrees wide and reach 2000 units on a hull about 600
 * long, so at any opacity worth seeing they paint over the model they describe. An outline on its
 * own says where the guns bear and how far, but gives the eye no body to read the direction from.
 * Kept faint enough to look THROUGH, with the edges carrying the shape.
 */
function coneGeometry(
    arc: { widthDegrees: number; heightDegrees: number; range: number },
): THREE.BufferGeometry {
    // A cone with a rounded bottom - see `fireConeSurface`. The apex-to-rim fan it replaced drew a
    // Pringle at the wide arcs the shipped mounts declare.
    const geometry = new THREE.BufferGeometry();

    geometry.setAttribute('position', new THREE.Float32BufferAttribute(
        fireConeSurface(arc.widthDegrees, arc.heightDegrees, arc.range,
            FIRE_ARC_SEGMENTS, FIRE_ARC_RINGS),
        3));

    return geometry;
}

/** The edges of the same cone, as line segments in the same space. */
function coneOutlineGeometry(
    arc: { widthDegrees: number; heightDegrees: number; range: number },
): THREE.BufferGeometry {
    const geometry = new THREE.BufferGeometry();

    geometry.setAttribute('position', new THREE.Float32BufferAttribute(
        fireConeOutline(arc.widthDegrees, arc.heightDegrees, arc.range,
            FIRE_ARC_SEGMENTS, FIRE_ARC_RIBS),
        3));

    return geometry;
}

/** Splits a `systemId#index` emitter key. The id may itself contain a `#`, so split from the right. */
function splitEmitterKey(key: string): [string, number] {
    const at = key.lastIndexOf('#');
    return [key.slice(0, at), Number(key.slice(at + 1))];
}

/** What the viewport remembers about one running system, so it can be rebuilt on restart. */
interface ParticleEntry {
    /** Its key in {@link PreviewViewport.particleSystems}, so an entry alone can answer for itself. */
    id: string;
    system: AlamoParticleContent;
    attachToPartId?: string;
    attachBone?: string;
    /** The bone's index, which is what tells two same-named bones apart. */
    attachBoneIndex?: number;
    /**
     * What the game's own rules say: the opening rule and the damage state, together.
     *
     * Recomputed whenever a hardpoint blows up or is repaired, which is why the reader's own choice
     * cannot live here - it would be overwritten every time anything else on the model changed.
     */
    gateVisible: boolean;
    /** What the current ALT/LOD says, held apart again so a level change cannot clobber a toggle. */
    levelVisible: boolean;
    levels: LevelTagged;
    /** The owning object's uniform render scale, so a rebuild keeps it. */
    scaleFactor: number;
    instance: ParticleSystemInstance;
}

/**
 * The scene node an effect is described by: the bone it is anchored on.
 *
 * NOT the system's own root, which hangs off the simulation space and so is owned by no bone at
 * all - see `ParticleSystemInstance.attachTo`. Falls back to the root for a system with no anchor,
 * which is a particle file opened on its own.
 */
function anchorNodeOf(entry: ParticleEntry): THREE.Object3D {
    return entry.instance.anchorNode ?? entry.instance.root;
}

import {
    alamoBoneName, labelCandidates, type BoneAttachment, type FlatBone, type LabelMode,
} from './skeleton';

/** Vertical field of view, in degrees. Narrow enough to keep perspective distortion off a long hull. */
const FIELD_OF_VIEW = 50;

/** What the stats readout shows. */
export interface ViewportStats {
    parts: number;
    meshes: number;
    triangles: number;
    bones: number;
    animations: string[];
}

/** A named camera angle the toolbar can jump to. */
export type PresetView = 'front' | 'side' | 'top' | 'threeQuarter';

const PRESETS: Record<PresetView, { azimuth: number; elevation: number }> = {
    front: { azimuth: 0, elevation: 0 },
    side: { azimuth: 90, elevation: 0 },
    top: { azimuth: 0, elevation: 89.9 },
    threeQuarter: { azimuth: DEFAULT_AZIMUTH_DEGREES, elevation: DEFAULT_ELEVATION_DEGREES },
};

/** One loaded part, kept so it can be hidden, re-tinted or removed without a reload. */
interface LoadedPart {
    id: string;
    root: THREE.Object3D;
    /** Bones by their Alamo name, lowercased - node names carry a `#index` suffix to stay unique. */
    bones: Map<string, THREE.Object3D>;
    /** Bones by their model bone index, in index order, for the skeleton view. */
    bonesByIndex: Map<number, THREE.Object3D>;
    /**
     * The subject this part belongs to - {@link ACTIVE_SUBJECT} for the one that opened the scene.
     *
     * A death clone and a piece of wreckage are their own subjects, not further pieces of the one
     * being previewed. Run through the ACTIVE subject's row chain they inherit its answers by
     * NAME - the Calamari Cruiser's wreck carries meshes called `HP_F-L_Blast` and so does its
     * hull, so every blast decal on the wreck lit because the hull's mounts were all destroyed.
     * Worse, nothing wrote the meshes the chain had no row for, so the file's raw visibility
     * stood: its `RSkinShadowVolume` hull and its two `alamoHidden` engine plates were drawn as
     * solid geometry.
     */
    subjectId: string;
}

/**
 * The subject that opened the scene: the one the tree, the stats and the row chain describe.
 *
 * Exactly one subject is ACTIVE at a time and it is always this one. Everything else in the scene -
 * a death clone, a piece of wreckage - is a PASSIVE subject: it is drawn by its own file's rules,
 * runs its own clips, and never answers a question about the model being previewed.
 */
export const ACTIVE_SUBJECT = 'subject';

/** Anything about a loaded model beyond the bone it hangs off. */
export interface AddPartOptions {
    /** Which subject the geometry belongs to. The active one when not given. */
    subjectId?: string;
    /**
     * A world pose to drop the model at, instead of parenting it to a bone.
     *
     * Wreckage stops being attached to the ship the moment it breaks off, which is the whole point
     * of it - so it captures where its mount WAS and is placed there under the model root.
     */
    pose?: { position: THREE.Vector3; quaternion: THREE.Quaternion };
}

/**
 * What the viewport clears to.
 *
 * The scene paints its OWN ground rather than letting the transparent canvas show the panel
 * through. That is not only a theming choice: with nothing cleared behind it, any blended draw
 * raises the canvas's ALPHA as well as its colour, so an additive quad whose texture is black
 * turned the see-through background into an opaque black rectangle - it added no light, it just
 * stopped the page showing through. Kept in step with the canvas background in `modelPreview.tsx`,
 * which covers the moment before the first frame.
 */
export const VIEWPORT_BACKGROUND = 0x1b1d21;

/** Fixed, so the sky is the same one every session and a screenshot can be compared to another. */
const STAR_SEED = 20260817;

/** The flat grey an untinted mesh draws in, until the real shaders land. */
const UNTINTED = 0xb8b8b8;

/**
 * How solid a collision hull or shadow volume draws.
 *
 * Both are seen THROUGH, and for a collision hull that is the entire reason to look at one: the
 * question being asked is whether the hull encloses the model, which you can only answer if the
 * geometry inside it is visible and anything poking out of it reads immediately.
 *
 * Lower than it looks, because the value COMPOUNDS. A rancor carries three shadow volumes and two
 * collision hulls, and at the effects' own alpha - the ShadowVolume ones ask for FULL - the five of
 * them stacked into a solid block with the model nowhere in it.
 */
const DEBUG_HULL_OPACITY = 0.3;

/** Smallest gap between two bone labels, in pixels, before the further one is dropped. */
const LABEL_SPACING = 26;

/** How often the label overlay is repositioned. Labels do not need the frame rate. */
const LABEL_INTERVAL_MS = 80;

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
        THREE.Object3D, { position: THREE.Vector3; quaternion: THREE.Quaternion; scale: THREE.Vector3 }
    >;
    /** The clip running on this subject right now. */
    action: THREE.AnimationAction | null;
    /** Meshes the FILE hides. A clip never brings one of these back. */
    fileHidden: Set<THREE.Object3D>;
}

/** Every mesh a model's own materials say is not drawn - collision hulls, shadow volumes. */
function hiddenMeshes(root: THREE.Object3D): Set<THREE.Object3D> {
    const hidden = new Set<THREE.Object3D>();

    root.traverse(node => {
        if (node instanceof THREE.Mesh
            && resolveMaterial((node.userData.alamo ?? {}) as MaterialExtras).hidden) {
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
    /** Where the mount was when it broke off. The debris does not follow the ship. */
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
export const BREAKOFF_ATTACHMENT = 'wreck:';

/** A three vector as the plain shape the wire and the arithmetic use. */
function vectorOf(at: THREE.Vector3): { x: number; y: number; z: number } {
    return { x: at.x, y: at.y, z: at.z };
}

/** Reused so a tick of reticle projection allocates nothing at all. */
const RETICLE_NDC = new THREE.Vector3();

/** How close to a joint a click must land, in pixels, to count as selecting it. */
const JOINT_PICK_RADIUS = 10;

/** Scratch for the bone-palette maths, so a per-frame loop allocates nothing. */
const SKIN_WORK = new THREE.Matrix4();

/** Reused per frame by the billboards, which run before every render. */
/**
 * The engine's light intensities are stored as the engine means them; this converts to three's.
 *
 * `MeshStandardMaterial` applies a Lambert BRDF of `albedo / PI`, which the engine's fixed-function
 * shading does not - so the engine's 0.5 sun has to arrive here as 0.5 * PI to land at the same
 * brightness. That factor is the whole difference between a model that matches AloViewer and one
 * that renders as a dark silhouette, and it is why the old hard-coded 1.6 default looked right: it
 * was the engine's 0.5 with this factor already baked in, which then made every OTHER value in the
 * rig wrong by comparison.
 */
const ENGINE_LIGHT_TO_THREE = Math.PI;

/**
 * How many times the ground's grain repeats across the floor plane.
 *
 * Constant, because the plane is already scaled to the subject - so this is the apparent density of
 * the grain, and it stays the same whether the reader is looking at a trooper or a Star Destroyer.
 */
const GROUND_TILES = 14;

/** Reused so measuring the canvas each frame allocates nothing. */
const FRAME_SIZE = new THREE.Vector2();

const BILLBOARD_AT = new THREE.Vector3();
const BILLBOARD_LIGHT = new THREE.Vector3();
const BILLBOARD_BONE = new THREE.Quaternion();

export class PreviewViewport {
    private readonly renderer: THREE.WebGLRenderer;
    private readonly scene = new THREE.Scene();
    private readonly camera: THREE.PerspectiveCamera;
    private readonly controls: OrbitControls;
    private readonly loader = new GLTFLoader();
    private readonly clock = new THREE.Clock();

    private readonly grid: THREE.GridHelper;

    private readonly floor: THREE.Mesh;

    /**
     * The ground's grain, built once and shared by its colour and its roughness.
     *
     * A FIXED repeat, not one scaled to the model: the plane itself already grows with the subject,
     * so a repeat that grew as well counted the size twice and put 67 tiles of sub-pixel grain on a
     * Star Destroyer's floor, which aliased into a dark mush rather than reading as a surface.
     *
     * Mipmapped and filtered, which a `DataTexture` is not by default - it arrives as nearest with
     * no mip chain at all, and a tiled surface seen at a grazing angle is exactly the case that
     * punishes. Anisotropy for the same reason: the ground is almost always viewed edge-on.
     */
    private readonly groundGrain = ((): THREE.DataTexture => {
        const texture = new THREE.DataTexture(
            concreteNoise(), GROUND_TEXTURE_SIZE, GROUND_TEXTURE_SIZE);

        texture.wrapS = THREE.RepeatWrapping;
        texture.wrapT = THREE.RepeatWrapping;
        texture.colorSpace = THREE.SRGBColorSpace;
        texture.repeat.set(GROUND_TILES, GROUND_TILES);
        texture.generateMipmaps = true;
        texture.minFilter = THREE.LinearMipmapLinearFilter;
        texture.magFilter = THREE.LinearFilter;
        texture.needsUpdate = true;

        return texture;
    })();

    /** The one shadow-casting light, held so the dial and the framing can both move it. */
    private keyLight: THREE.DirectionalLight | null = null;
    private ambient: THREE.AmbientLight | null = null;

    /** The rig as the reader has it. Held so a colour change can be applied without a reframe. */
    private rig: LightRig = DEFAULT_LIGHTS;

    /** The wind as a vector in the space the particles are simulated in. */
    private windVector: { x: number; y: number; z: number } = { x: 0, y: 0, z: 0 };

    /** The same wind in the terms the foliage shaders read it: a heading and a speed. */
    private wind: Wind = DEFAULT_VIEWER_SETTINGS.wind;

    /** The starfield and the sky dome, built on first use and then just shown or hidden. */
    private stars: THREE.Points | null = null;
    private sky: THREE.Mesh | null = null;

    private lightAzimuth = DEFAULT_LIGHT_AZIMUTH_DEGREES;

    private lightElevation = DEFAULT_LIGHT_ELEVATION_DEGREES;
    private readonly axes: THREE.AxesHelper;
    private readonly parts = new Map<string, LoadedPart>();
    private readonly modelRoot = new THREE.Group();

    /** Effects that translated, by lower-cased shader name. */
    private readonly translated = new Map<string, TranslatedEffect>();

    /** Declared render state per shader, so a material rebuilt later is not left on the defaults. */
    private readonly shaderStates = new Map<string, FxMaterialState>();

    /** Sub-meshes the user has shown or hidden by hand, by three.js uuid. */
    /**
     * What one tree row controls.
     *
     * Explicit, because the alternative was inferring it from the row's id - and a merged row is
     * anchored on its BONE's id while standing for a mesh as well. Reading the mesh's state and
     * writing the bone's is what made a hidden mesh impossible to show again.
     */
    private readonly rowTargets = new Map<string, {
        boneIndex?: number;
        mesh?: THREE.Mesh;
        particleId?: string;
    }>();

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

    /** Every decoded texture, kept so a material rebuilt later can be handed them again. */
    private readonly textures = new Map<string, THREE.Texture>();

    /** Draw with a translated shader wherever one is available. */
    private translatedShaders = true;

    /** Seconds since the viewport opened, for the effects that read the clock. */
    private elapsed = 0;

    /** The directional rig, in the order the engine numbers its lights. */
    private alamoLights: THREE.DirectionalLight[] = [];

    /**
     * The ambient probe the translated shaders read, kept in step with the rig.
     *
     * Rebuilt whenever the rig changes rather than fixed at construction. It used to be a hard 0.6
     * that nothing ever updated, so Game mode washed out under six times the engine's own 0.1 and
     * the reader's Ambient slider moved the scene light while the translated shaders ignored it.
     */
    private harmonics = neutralHarmonics(DEFAULT_LIGHTS.ambient.colour);

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

    // Held on the viewport rather than read off the action, because they have to survive the action
    // being torn down and rebuilt - which happens on every clip change. Setting the loop mode on the
    // panel and then picking a different clip used to silently drop it.
    private animationSpeed = 1;
    private animationLoop = true;
    private animationPaused = false;

    /** Whether a clip is currently driving bone visibility, so the release happens exactly once. */
    private overridingVisibility = false;

    private frameHandle = 0;
    private disposed = false;

    /** Built the first time a heat sprite is on screen, and not before - it costs two buffers. */
    private heat: HeatPass | null = null;

    /**
     * The engine's own shadows: stencil volumes cast by the authored shadow mesh.
     *
     * Lives in the scene graph and is ordered after the model, so one `renderer.render` still makes
     * the finished frame whether it is going to the canvas, the heat composite or bloom.
     */
    private readonly shadowVolumes = new ShadowVolumePass();

    /**
     * OFF until the volume pass is right.
     *
     * It is close but not correct: some of the volume's own silhouette still darkens pixels it
     * should not, so on a model with an authored volume it can blacken the hull. A half-working
     * renderer on by default is worse than one behind a switch - the reader has no way to tell a
     * shadow bug from a model bug. Default mode keeps three's shadow mapping; this is what Game
     * mode will use once it draws only what it should.
     */
    private shadowVolumesEnabled = false;

    /** Whether heat sprites bend the frame at all. */
    private heatEnabled = true;

    /** Built the first time bloom is switched on, like the heat buffers - it is not free either. */
    private bloom: BloomPass | null = null;

    private bloomEnabled = false;

    /**
     * Where the frame goes when something comes after it.
     *
     * Only allocated while bloom is on. With bloom off the chain is one step long and draws
     * straight to the canvas, exactly as it did before any of this existed.
     */
    private frameCopy: THREE.FramebufferTexture | null = null;

    /** Draw the heat BUFFER rather than the bent picture. */
    private heatDebug = false;

    private wireframe = false;

    /**
     * A shadow-only plane over the floor.
     *
     * Separate from the floor rather than replacing its material: `ShadowMaterial` draws the shadow
     * and NOTHING else, so using it for the floor itself would trade a lit ground surface for a
     * tinted shadow floating in mid-air. Two coplanar planes, the catcher pushed in front by a
     * polygon offset, gives both.
     */
    private readonly shadowCatcher: THREE.Mesh;

    private readonly shadowMaterial: THREE.ShadowMaterial;

    /** Bone nodes whose geometry turns to face the camera, the light or the wind. */
    private billboards: THREE.Object3D[] = [];

    /** How far the camera stands off, kept so the depth range can be redone without reframing. */
    private fitDistance = 1;

    /** The reader's own multiplier on the fitted far plane. */
    private drawDistance = 1;

    private alt = 0;
    private lod = 0;

    /** Joints and the lines between them, rebuilt each frame because animation moves bones. */
    private readonly joints: THREE.Points;
    private readonly jointLines: THREE.LineSegments;

    /** One wireframe box per selected row, so the tree can point at something in the viewport. */
    private readonly selectionBoxes: THREE.LineSegments;

    /** The selected bone's local axes, so its FACING is visible and not just its position. */
    private readonly boneAxes: THREE.LineSegments;

    /** The rows the reader has selected in the tree, in the order they picked them. */
    private selectedRows: readonly string[] = [];
    private skeletonVisible = false;
    private labelMode: LabelMode = 'none';
    private selectedBone: number | null = null;
    private lastLabelUpdate = 0;
    private readonly labelPool: HTMLElement[] = [];

    /** The targeting marks currently being drawn, and the pool of images drawing them. */
    private reticles: ReticleMark[] = [];
    private readonly reticlePool: HTMLElement[] = [];
    private reticleScreenSize: number | null | undefined = null;

    /** The mount the pointer is over, so its mark can show the tracked art. */
    private hoveredReticle: string | null = null;

    /** Raised when a targeting mark is clicked, so the dock can follow the viewport. */
    onReticleClicked: ((hardpointId: string) => void) | null = null;

    /** The turrets currently sweeping, and how far through the swing they are. */
    private turretSweeps: {
        partId: string; turretBone: string; barrelBone: string | null; turret: PreviewTurret;
    }[] = [];
    private sweepPhase = 0;

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

    private readonly particleSystems = new Map<string, ParticleEntry>();

    /**
     * Systems that have been killed but whose particles are still finishing.
     *
     * `leaveParticles` on the SYSTEM decides this, and it is what the engine does:
     * `Engine::KillParticleSystem` calls `StopSpawning()` for one that says so and lets the air
     * clear, and `Kill()` for one that does not. It defaults TRUE, and the ones that say false are
     * the ENGINE GLOWS - a glow cuts with the engine, a fire trails off. Nothing read the flag, so
     * a wreck's fire vanished mid-flame the instant its debris expired.
     *
     * Held OUT of {@link particleSystems} rather than flagged inside it: they are no longer
     * addressable, must not be re-gated by anything, and must not collide with a new system taking
     * the same id.
     */
    private fadingParticles: ParticleSystemInstance[] = [];

    /**
     * What each tree row stands for, so the inspector can describe it.
     *
     * `boneIndex` means the row IS that bone; `ownerBone` means the row is a mesh that rides one.
     * A merged row carries both a bone and a mesh, and gets a panel describing each.
     */
    private readonly inspectSources =
        new Map<string, { mesh?: THREE.Mesh; boneIndex?: number; ownerBone?: number; systemId?: string }>();
    private readonly particleTextures = new Map<string, THREE.Texture>();
    private particlesVisible = true;
    private particlesPaused = false;

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
    /**
     * The team tint, or null for the model's own colours.
     *
     * Kept here rather than applied and forgotten, because materials are rebuilt whenever a part
     * loads - a turret arriving after the faction was picked has to come in already tinted.
     */
    private colorization: NormalisedColour | null = null;

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

    /** Bones a weapon fires from, lowercased. Their aim axis is drawn as an arrow. */
    private fireBones: ReadonlySet<string> = new Set();

    /** See {@link modelSpan}. Cleared with the scene. */
    private cachedSpan: number | null = null;

    /** Wreckage currently in the air, each on its own clock. */
    private readonly breakoffs: BreakoffInstance[] = [];

    /** Every named attachment a caller has asked for, so {@link unresolvedAttachments} can check. */
    private readonly attachRequests = new Map<string, AttachmentRequest>();

    /** Parts broken off by a destroyed hardpoint. */
    private readonly hiddenParts = new Set<string>();

    /** One-shot systems - death explosions - removed once they burn out. */
    private readonly transient = new Set<string>();

    /** Firing-arc gizmos, hung off their fire bones so they follow the turret. */
    private readonly arcs: THREE.Mesh[] = [];
    private arcsVisible = false;

    /**
     * Emitters switched off, keyed `systemId#index`.
     *
     * Held by the viewport rather than only in the shell because restart rebuilds every instance from
     * scratch; without this, a restart would silently switch them all back on. Keyed by index because
     * emitter names repeat within one system.
     */
    private readonly hiddenEmitters = new Set<string>();

    /**
     * Playback rate for particles only.
     *
     * Separate from the animation mixer's: a smoke plume worth slowing down to inspect usually sits on
     * a model whose animation you still want at speed.
     */
    private particleSpeed = 1;

    constructor(
        private readonly canvas: HTMLCanvasElement,
        /** Absolutely-positioned container the bone labels are written into. */
        private readonly labelLayer: HTMLElement,
    ) {
        // `stencil` is NOT on by default in this three - it defaults to false, and without a
        // stencil buffer every stencil op is a silent no-op, so the shadow-volume count never
        // happens and the darken covers the entire frame instead of the shadow.
        this.renderer = new THREE.WebGLRenderer({
            canvas, antialias: true, alpha: true, stencil: true,
        });
        this.renderer.setPixelRatio(Math.min(globalThis.devicePixelRatio ?? 1, 2));

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
        this.controls.addEventListener('start', () => this.onCameraMoved?.());

        this.scene.background = new THREE.Color(VIEWPORT_BACKGROUND);
        this.scene.add(this.modelRoot);
        this.addLighting();

        this.grid = new THREE.GridHelper(100, 20);
        this.axes = new THREE.AxesHelper(10);

        // A ground plane, off by default. The grid alone gives scale but no surface, so a walker or
        // a turret reads as floating; a lit floor under it puts the model somewhere. Pushed back by
        // a polygon offset rather than dropped below y=0, so the grid stays exactly on the ground
        // plane it is measuring instead of hovering a hand's width above it.
        this.floor = new THREE.Mesh(
            new THREE.PlaneGeometry(100, 100),
            new THREE.MeshStandardMaterial({
                // Lighter than the bare floor used to be, because the grain map multiplies it
                // down again - and a dull mid grey is what reads as poured concrete rather than as
                // the black glass the flat plane looked like.
                color: 0x64645f,
                roughness: 1,
                metalness: 0,

                // The grain, as albedo AND as roughness. The albedo alone would only mottle the
                // colour; it is the roughness map that breaks up the dielectric sheen a raking key
                // light sweeps across an otherwise perfectly even surface, which is what made the
                // floor read as polished stone or as dark still liquid with the models floating
                // over it. Both off one tile, so it costs one texture.
                map: this.groundGrain,
                roughnessMap: this.groundGrain,

                side: THREE.DoubleSide,
                polygonOffset: true,
                polygonOffsetFactor: 1,
                polygonOffsetUnits: 1,
            }));
        this.floor.rotation.x = -Math.PI / 2;
        this.floor.visible = false;

        // Rides with the floor and is only ever shown with it: with no ground there is nothing for
        // a shadow to fall on, and a tinted patch hanging in space would be a lie about the scene.
        this.shadowMaterial = new THREE.ShadowMaterial({
            // The shadow-map catcher still DRAWS its colour rather than multiplying, so it takes
            // a dark tone while the stencil darken takes the reader's chosen one. It only applies
            // to the 63% of models that author no volume.
            color: 0x0f0f14,
            transparent: true,
            opacity: 1,
            depthWrite: false,
            polygonOffset: true,
            polygonOffsetFactor: -1,
            polygonOffsetUnits: -1,
        });

        this.shadowCatcher = new THREE.Mesh(
            new THREE.PlaneGeometry(100, 100), this.shadowMaterial);
        this.shadowCatcher.rotation.x = -Math.PI / 2;
        this.shadowCatcher.receiveShadow = true;
        this.shadowCatcher.visible = false;
        this.floor.receiveShadow = true;

        this.scene.add(this.grid, this.axes, this.floor, this.shadowCatcher);
        this.scene.add(this.shadowVolumes.darken);

        // Drawn on top of the model rather than inside it: a joint buried in a hull is not a joint
        // you can click, and the point of the skeleton view is reaching the bones.
        this.joints = new THREE.Points(
            new THREE.BufferGeometry(),
            new THREE.PointsMaterial({ size: 7, sizeAttenuation: false, depthTest: false }));
        this.jointLines = new THREE.LineSegments(
            new THREE.BufferGeometry(),
            new THREE.LineBasicMaterial({ color: 0x6fb3ff, depthTest: false, transparent: true,
                opacity: 0.7 }));

        // Amber rather than the skeleton's blue: the two are shown together, and a selection has
        // to be tellable from a joint at a glance. Drawn through the model on purpose - a box only
        // visible when the thing it points at is already in front is no help finding it.
        this.selectionBoxes = new THREE.LineSegments(
            new THREE.BufferGeometry(),
            new THREE.LineBasicMaterial({ color: 0xffb347, depthTest: false }));
        this.selectionBoxes.renderOrder = 999;
        this.selectionBoxes.frustumCulled = false;
        this.selectionBoxes.visible = false;
        this.scene.add(this.selectionBoxes);

        // The selected bone's own AXES. A box says where a bone is and nothing about which way it
        // faces, which is the question that actually matters here - a fire bone aims along its
        // local X, and a hardpoint's whole placement is its rotation. Vertex-coloured red/green/blue
        // for X/Y/Z, the convention every 3D tool shares.
        this.boneAxes = new THREE.LineSegments(
            new THREE.BufferGeometry(),
            new THREE.LineBasicMaterial({ vertexColors: true, depthTest: false }));
        this.boneAxes.renderOrder = 1000;
        this.boneAxes.frustumCulled = false;
        this.boneAxes.visible = false;
        this.scene.add(this.boneAxes);

        this.joints.renderOrder = 999;
        this.jointLines.renderOrder = 999;
        this.joints.visible = false;
        this.jointLines.visible = false;
        this.joints.frustumCulled = false;
        this.jointLines.frustumCulled = false;
        this.scene.add(this.joints, this.jointLines);

        canvas.addEventListener('pointerdown', this.onPointerDown);

        this.resize();
        this.loop();
    }

    /**
     * Ambient plus two directionals.
     *
     * Not an attempt at the engine's lighting - that is what the shader work is for. This exists so
     * an untextured hull reads as a solid rather than a silhouette, which is what a single light or
     * ambient alone would give.
     */
    private addLighting(): void {
        this.ambient = new THREE.AmbientLight(0xffffff, DEFAULT_LIGHTS.ambient.intensity);
        this.scene.add(this.ambient);

        const key = new THREE.DirectionalLight(0xffffff, 1.6);

        // Only the KEY light casts. A second shadow-casting light doubles the cost and crosses two
        // shadows over the model, which reads as a rendering fault rather than as a fill light.
        key.castShadow = true;
        key.shadow.mapSize.set(2048, 2048);

        // Shadow acne on a hull covered in near-parallel panels needs a normal bias, not just a
        // depth one: a constant bias large enough to clear the panels detaches the shadow from the
        // geometry casting it.
        key.shadow.bias = -0.0005;
        key.shadow.normalBias = 0.02;

        // Two fills, not one, which is AloViewer's own rig and the engine's: it numbers three
        // directional lights and a translated shader asks for all three by semantic. With only two
        // supplied, DIR_LIGHT_VEC_2 kept whatever the header declared.
        const fill1 = new THREE.DirectionalLight(0xffffff, 0.5);
        const fill2 = new THREE.DirectionalLight(0xffffff, 0.25);

        this.scene.add(key, fill1, fill2, key.target);

        this.keyLight = key;

        // In the order the engine numbers them, so a shader asking for DIR_LIGHT_VEC_0 is lit by
        // the same sun everything else in the viewport is.
        this.alamoLights = [key, fill1, fill2];
        this.applyLightRig();

        this.placeKeyLight();
    }

    /**
     * Puts the key light at its current angles, far enough out to clear the model, and sizes the
     * shadow frustum to match.
     *
     * A directional light's shadow camera does not follow anything on its own, so this has to run
     * again whenever the model's size changes as well as whenever the dial moves.
     */
    private placeKeyLight(): void {
        const light = this.keyLight;
        if (light === null) {
            return;
        }

        const sphere = this.boundingSphere();
        const direction = lightDirection(this.lightAzimuth, this.lightElevation);
        const frustum = shadowFrustum(sphere.radius);

        // Stood off at the frustum's far edge, so the model sits comfortably inside the depth range
        // rather than against its near plane.
        const distance = frustum.far / 2;

        light.position.set(
            sphere.center.x + direction.x * distance,
            sphere.center.y + direction.y * distance,
            sphere.center.z + direction.z * distance);

        light.target.position.set(sphere.center.x, sphere.center.y, sphere.center.z);
        light.target.updateMatrixWorld();

        // The volumes extrude away from this same light, so the two can never disagree about where
        // the shadow falls - and their reach is fitted to the model for the same reason the shadow
        // camera is.
        this.shadowVolumes.setLight(direction);
        this.shadowVolumes.setScale(sphere.radius, this.camera.far);

        const camera = light.shadow.camera;
        camera.left = -frustum.extent;
        camera.right = frustum.extent;
        camera.top = frustum.extent;
        camera.bottom = -frustum.extent;
        camera.near = frustum.near;
        camera.far = frustum.far;
        camera.updateProjectionMatrix();
    }

    /**
     * Moves the key light, in the same azimuth/elevation terms as the camera presets.
     *
     * The light drives the translated shaders too - they read it as DIR_LIGHT_VEC_0 - so this is one
     * dial for the shading and the shadow both, rather than a viewer-only conceit.
     */
    setLightAngles(azimuthDegrees: number, elevationDegrees: number): void {
        this.lightAzimuth = azimuthDegrees;
        this.lightElevation = elevationDegrees;
        this.placeKeyLight();
    }

    /**
     * Takes the whole rig: three directionals with their own colours, plus the global terms.
     *
     * The same shape AloViewer's settings dialog offers, because it is the engine's own - a sun and
     * two fills, each with a heading, a tilt, a colour and a brightness. Colour and brightness are
     * kept apart so a light can be dimmed without desaturating it.
     */
    setLightRig(rig: LightRig): void {
        this.rig = rig;

        // The sun's angles drive the shadow and the dial the reader already has, so they stay the
        // viewport's own state rather than being read back out of the rig on every frame.
        this.lightAzimuth = rig.sun.azimuth;
        this.lightElevation = rig.sun.elevation;

        this.applyLightRig();
        this.placeKeyLight();
    }

    /**
     * Chooses what is behind the model.
     *
     * A flat grey is honest but makes a hull hard to judge; a starfield gives a space model
     * something to sit against and a sky does the same for a ground one. Both are BACKDROPS: never
     * lit, never in the depth buffer, and they follow the camera so they read as infinitely far.
     */
    setBackground(kind: BackgroundKind): void {
        if (kind === 'starfield') {
            this.stars ??= this.buildStars();
        }

        if (kind === 'sky') {
            this.sky ??= this.buildSky();
        }

        if (this.stars !== null) {
            this.stars.visible = kind === 'starfield';
        }

        if (this.sky !== null) {
            this.sky.visible = kind === 'sky';
        }

        // Space is black behind the stars; the flat backdrop keeps the neutral grey that reads as
        // "no background at all" rather than as a choice.
        this.scene.background = new THREE.Color(
            kind === 'starfield' ? 0x05070c : VIEWPORT_BACKGROUND);
    }

    private buildStars(): THREE.Points {
        const geometry = new THREE.BufferGeometry();
        geometry.setAttribute(
            'position', new THREE.BufferAttribute(starPositions(STAR_SEED), 3));

        const stars = new THREE.Points(geometry, new THREE.PointsMaterial({
            color: 0xffffff,
            // In pixels, and NOT scaled by distance: these are meant to read as points of light at
            // infinity, and a star that grows as you dolly in is a speck of dust on the lens.
            size: 1.6,
            sizeAttenuation: false,
            depthTest: false,
            depthWrite: false,
        }));

        // Parented to the CAMERA and drawn first: it turns with the view like a sky should and
        // never occludes anything. It is NOT exempt from the far plane, though - `frustumCulled`
        // only stops object-level culling - so `prepareForCamera` scales it to sit inside whatever
        // plane the model has been fitted with.
        stars.frustumCulled = false;
        stars.renderOrder = -1;
        this.camera.add(stars);
        this.scene.add(this.camera);

        return stars;
    }

    private buildSky(): THREE.Mesh {
        const geometry = new THREE.SphereGeometry(1, 32, 24);
        const colours = new Float32Array(geometry.attributes.position.count * 3);
        const position = geometry.attributes.position;

        for (let i = 0; i < position.count; i++) {
            const [r, g, b] = skyGradient(position.getY(i));
            colours.set([r, g, b], i * 3);
        }

        geometry.setAttribute('color', new THREE.BufferAttribute(colours, 3));

        const sky = new THREE.Mesh(geometry, new THREE.MeshBasicMaterial({
            vertexColors: true,
            // Seen from the inside, and never in the depth buffer.
            side: THREE.BackSide,
            depthTest: false,
            depthWrite: false,
        }));

        // Follows the camera's POSITION but not its rotation - the horizon has to stay level and at
        // eye height however the camera turns, which is what makes it read as a horizon rather than
        // as a painted backdrop stuck to the screen.
        sky.frustumCulled = false;
        sky.renderOrder = -1;
        this.scene.add(sky);

        return sky;
    }

    /**
     * Sets the one wind the scene has.
     *
     * Read by the foliage shaders through their own semantics and by the 484 emitters that set
     * `affectedByWind`, which take it into their initial speed at birth.
     */
    setWind(headingDegrees: number, speed: number): void {
        this.wind = { heading: headingDegrees, speed };
        const heading = (headingDegrees * Math.PI) / 180;

        this.windVector = {
            x: Math.cos(heading) * speed || 0,
            y: 0,
            z: -Math.sin(heading) * speed || 0,
        };
    }

    /** Puts the current rig onto the three lights and the ambient term. */
    private applyLightRig(): void {
        const rig = this.rig;
        const settings = [rig.sun, rig.fill1, rig.fill2];

        this.alamoLights.forEach((light, index) => {
            const setting = settings[index];
            if (setting === undefined) {
                return;
            }

            light.color.setRGB(...setting.colour);
            light.intensity = setting.intensity * ENGINE_LIGHT_TO_THREE;

            // The sun is placed by `placeKeyLight`, which also has the shadow frustum to fit; the
            // fills only need a direction, and a directional light's position IS its direction.
            if (index > 0) {
                const direction = lightDirection(setting.azimuth, setting.elevation);
                light.position.set(direction.x, direction.y, direction.z);
            }
        });

        if (this.ambient !== null) {
            this.ambient.color.setRGB(...rig.ambient.colour);
            this.ambient.intensity = rig.ambient.intensity * ENGINE_LIGHT_TO_THREE;
        }

        // The engine's own shaders take their ambient from the probe, not from a scene light, so
        // this is the same setting expressed the other way round - and without the PI, which is
        // three's correction and not theirs.
        this.harmonics = neutralHarmonics([
            rig.ambient.colour[0] * rig.ambient.intensity,
            rig.ambient.colour[1] * rig.ambient.intensity,
            rig.ambient.colour[2] * rig.ambient.intensity,
        ]);
    }

    // ── content ───────────────────────────────────────────────────────────────

    /**
     * Adds a model, attached to a bone of an already-loaded part when asked.
     *
     * Attaching to the bone's Object3D rather than copying its transform is what makes a mounted
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
        id: string, glbBase64: string, attachToPartId?: string, attachBone?: string,
        options: AddPartOptions = {},
    ): Promise<THREE.Object3D> {
        const subjectId = options.subjectId ?? ACTIVE_SUBJECT;

        // Its own file's word decides how it is drawn, because the row chain speaks only for the
        // active subject and will never speak for this one.
        const passive = subjectId !== ACTIVE_SUBJECT;

        const gltf = await this.loader.parseAsync(decodeBase64(glbBase64), '');
        const root = gltf.scene;

        const bones = new Map<string, THREE.Object3D>();
        const bonesByIndex = new Map<number, THREE.Object3D>();

        // Pass one: who owns each bone index.
        //
        // `<name>#<n>` is TWO namespaces sharing one syntax: on a bone node `n` is the bone index,
        // on a mesh node of its own it is the SUB-MESH index. Neither exclusion works - a rigid
        // mesh hangs on the bone's own node, so that node really is both, while a skinned or
        // collision mesh gets a node of its own that is not a bone at all.
        //
        // So it is settled by PRECEDENCE: a node without geometry always wins the index, and one
        // with geometry only takes it if nothing better claims it. Letting a mesh overwrite a bone
        // made `Ei_trooper` report its root as `Storm_Trooper_LOD2` when bone 0 is `Root`, and
        // every bone count, parent and attachment was then read off the wrong object.
        root.traverse(node => {
            const parsed = alamoBoneName(node.name);
            if (parsed === null) {
                return;
            }

            const held = bonesByIndex.get(parsed.index);
            const better = held === undefined
                || (held instanceof THREE.Mesh && !(node instanceof THREE.Mesh));

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

            // Off the MATERIAL, which is where the exporter puts them and where `applyMaterial`
            // reads them from - that has not run yet, so `node.userData.alamo` is still empty.
            const source = Array.isArray(node.material) ? node.material[0] : node.material;
            const extras = (node.userData.alamo ?? source?.userData ?? {}) as MaterialExtras;
            const bone = splitGeometryFromBone(node, extras.alamoMesh);

            bonesByIndex.set(index, bone);
            for (const [key, held] of bones) {
                if (held === node) {
                    bones.set(key, bone);
                }
            }
        }

        // Pass three: materials, over the graph as it now stands.
        root.traverse(node => {
            if (node instanceof THREE.Mesh) {
                this.applyMaterial(node);
            }
        });

        // A posed part is placed under the model root at a world transform it captured itself.
        const parent = options.pose === undefined
            ? this.attachmentFor(attachToPartId, attachBone)
            : this.modelRoot;

        // A part attached to a BONE of another part is already inside that part's Z-up-to-Y-up
        // correction, so its own copy would apply the rotation twice - see `cancelRootCorrection`.
        // The hull goes under the model root, which is uncorrected, and keeps its own.
        //
        // A posed part is in the same position for the same reason: the world transform it copies
        // ALREADY contains the correction of the part it was taken from, so its own copy is the
        // identical mirrored 90-degree roll a mounted hardpoint used to have.
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
        this.applyWireframe();

        // And the render state every already-translated effect declares. A material built now gets
        // three's DEFAULTS, not the effect's - and `RSkinBumpColorize.fx` declares its cull mode
        // there, so a part arriving after its shader had was drawn with every triangle facing away
        // and rendered NOTHING. That is what made a death clone invisible with shaders switched on
        // and perfect with them off. `addBreakoff` has always done this; `addPart` never did.
        for (const [shader, state] of this.shaderStates) {
            this.applyStoredShaderState(shader, state);
        }

        if (this.colorization !== null) {
            this.setColorization(this.colorization);
        }
        this.collectShadowVolumes(root);

        // The handful of bones that turn to face something. Collected once per part rather than
        // searched for each frame: across the shipped models 268 bones out of 22866 declare a mode
        // at all, so a per-frame traversal would be almost entirely wasted.
        root.traverse(node => {
            if (billboardTypeOf(node) !== null) {
                this.billboards.push(node);
            }
        });

        // Geometry can arrive after its hardpoint was destroyed; a part loading late must not
        // reappear intact.
        root.visible = !this.hiddenParts.has(id);

        // Everything the animation needs goes to THIS PART'S SUBJECT, never to the scene. One path
        // for the hull, a mounted turret, a death clone and a piece of wreckage alike - what used
        // to differ between them was only which of these maps they were allowed into.
        const subject = this.subjectAnimation(subjectId);

        // Before any clip can touch it. Recorded for the whole part, not only for the bones this
        // GLB's own clips drive - a later part of the same subject can reach in here too.
        root.traverse(node => {
            if (!subject.restPose.has(node)) {
                subject.restPose.set(node, {
                    position: node.position.clone(),
                    quaternion: node.quaternion.clone(),
                    scale: node.scale.clone(),
                });
            }

            // First wins. A visibility track names a bone, and a bone name is unique within one
            // model because the exporter suffixes it with the bone index.
            if (node.name !== '' && !subject.nodeByName.has(node.name)) {
                subject.nodeByName.set(node.name, node);
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

    /** The object a part hangs off: a named bone of another part, or the scene root. */
    private attachmentFor(
        partId?: string, bone?: string, boneIndex?: number,
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
        // so both effects hung off the same jet and he fired from one.
        const byIndex = boneIndex === undefined
            ? undefined
            : parent.bonesByIndex.get(boneIndex);

        // A hardpoint naming a bone the hull does not have is already reported as a problem by the
        // server; here it just means the part sits at the hull's origin rather than vanishing.
        return byIndex ?? parent.bones.get(bone.toLowerCase()) ?? parent.root;
    }

    /**
     * Remembers that something asked to hang off this bone.
     *
     * Separate from resolving it, because a caller can legitimately refuse to place anything - see
     * {@link bonePosition} - and its request still has to be reported. Both of `attachmentFor`'s
     * fallbacks draw something recoverable and say nothing, and two separate bugs this session hid
     * behind them: fire cones resolving on the wrong model, reticles piling up at the origin.
     *
     * Breakoff roots are left out on purpose - a wreck is meant to stop existing, so a request for
     * one that has been cleared is not a fault.
     */
    private noteAttachment(partId: string, bone?: string, boneIndex?: number): void {
        if (partId.startsWith(BREAKOFF_ATTACHMENT)) {
            return;
        }

        this.attachRequests.set(`${partId} / ${bone ?? ''} / ${boneIndex ?? ''}`,
            { partId, bone, boneIndex });
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
                bone: request.bone === undefined || parent === undefined
                    ? false
                    : (request.boneIndex !== undefined
                        && parent.bonesByIndex.has(request.boneIndex))
                        || parent.bones.has(request.bone.toLowerCase()),
            };

            const problem = attachmentProblem(request, found);
            if (problem !== null && !problems.includes(problem)) {
                problems.push(problem);
            }
        }

        return problems;
    }

    /**
     * Replaces a loaded mesh's material with what the seam says to draw.
     *
     * The glTF material is only a carrier - the exporter puts the Alamo shader name and its parameters
     * in `extras` precisely because glTF cannot express them.
     */
    private applyMaterial(mesh: THREE.Mesh): void {
        // On the FIRST pass the glTF material is the carrier. After that this mesh's material is one
        // we made, whose userData is `{ spec, extras }` - reading extras off it again would find no
        // shader name at all, which silently drops every texture and every translated effect.
        const source = Array.isArray(mesh.material) ? mesh.material[0] : mesh.material;
        const extras = (mesh.userData.alamo ?? source?.userData ?? {}) as MaterialExtras;
        const spec = resolveMaterial(extras);

        mesh.userData.alamo = extras;

        // `meshVisible`, not `!spec.hidden`: that is the single authority, and it also weighs the
        // current ALT/LOD and whether a damage decal has been blown open. Setting visibility from
        // the material spec alone was harmless while this only ran at load time, and became a real
        // bug once the shader toggle started rebuilding materials - it re-showed every mesh the
        // level gating had hidden, including the collision and shadow hulls, and nothing put them
        // back because toggling again simply re-ran the same wrong assignment.
        this.applyMeshVisibility(mesh);

        // Only SOLID geometry casts. A collision hull and a shadow volume are overlays and would
        // throw a second silhouette over the model; an additive glow or an alpha-blended girder has
        // no opaque body to cast from, and a shadow map has no way to express a partial one - an
        // engine flare casting a hard black shadow is worse than it casting none.
        // Once a model authors a shadow volume, the volume IS the shadow - the hull stops casting
        // into the map, exactly as the engine does it. Recomputed for every mesh whenever a part
        // arrives, because a volume can turn up after the geometry that it belongs to.
        mesh.castShadow = castsShadowMap({
            hasVolumes: this.stencilShadowing, hidden: spec.hidden, blend: spec.blend,
        });
        mesh.receiveShadow = !spec.hidden;

        // A collision hull or a shadow volume draws NOTHING in the engine - the shadow techniques
        // literally set `ColorWriteEnable = 0` and write only stencil. But the reason to tick one
        // on in a preview is to LOOK at it, so it gets the debug colour its own effect declares:
        // `MeshCollision.fx` ships `Color = {0, 0, 1, 0.5}` and both ShadowVolume effects ship
        // `DebugColor = {0, 1, 1, 1}`. Left on the glTF material they came in on, both drew as
        // flat white, which is the one thing they are definitely not.
        if (spec.hidden) {
            mesh.material = this.debugMaterial(extras, spec);
            return;
        }

        // The effect's own shader when there is one and it is switched on, the archetype otherwise.
        const effect = this.translated.get((extras.alamoShader ?? '').toLowerCase());
        if (this.translatedShaders && effect !== undefined) {
            const translated = new AlamoMaterial(effect, extras);

            // A rebuild makes new materials, so the current tint has to be put back on them.
            if (spec.colorize) {
                translated.setColorization(this.colorization);
            }

            translated.userData = { spec, extras };
            mesh.material = translated;
            return;
        }

        const shared = {
            transparent: spec.blend !== 'opaque',
            depthWrite: spec.depthWrite,
            side: THREE.FrontSide,
        };

        // Unlit for the additive family, and it has to be a different MATERIAL rather than a
        // brighter light: a standard material's dielectric specular is not multiplied by the base
        // colour, so a black texel still reflects about 4% of the key light. Under an additive
        // blend that 4% is the muzzle flash's whole quad showing as a grey rectangle around the
        // flare. `Additive.fxh` is `texel * constant`, so basic is not an approximation here - it
        // is what the effect does.
        const material = spec.lit
            ? new THREE.MeshStandardMaterial({
                ...shared, color: UNTINTED, metalness: 0, roughness: 0.85,
            })
            // White, not UNTINTED: nothing is going to light this, so any grey here just dims the
            // texture the effect meant to show at full strength.
            : new THREE.MeshBasicMaterial({ ...shared, color: 0xffffff });

        // The archetype's own blend and depth, through the single place that knows how each one is
        // expressed in three. An additive ARCHETYPE means the engine's ONE, ONE - the same thing
        // the effect would have declared - so the two routes to a material cannot disagree, and a
        // mesh does not shift when its effect finally arrives.
        applyBlend(material, spec.blend);
        applyDepth(material, {
            depthTest: true, depthWrite: spec.depthWrite, depthFunc: 'lessEqual',
        });

        material.userData = { spec, extras };
        this.tint(material);
        mesh.material = material;

        // The effect's own declared blend, depth and cull state, if we have read it. Applied here
        // and not only when the shader arrives, because every material rebuild - a shader toggle, a
        // faction change - throws the previous one away and would otherwise silently fall back to
        // the archetype's guess for the rest of the session.
        const state = this.shaderStates.get((extras.alamoShader ?? '').toLowerCase());
        if (state !== undefined) {
            this.applyStoredShaderState((extras.alamoShader ?? '').toLowerCase(), state);
        }
    }

    /**
     * How a non-visual hull is drawn when the reader asks to see it.
     *
     * Neither of these is drawn by the engine at all - the shadow techniques set
     * `ColorWriteEnable = 0` and write only stencil - so there is no "correct" appearance to copy,
     * only a useful one. The COLOUR is still the effect's own: `MeshCollision.fx` ships
     * `Color = {0, 0, 1, 0.5}` and both ShadowVolume effects ship `DebugColor = {0, 1, 1, 1}`, and
     * a mod is free to change either.
     *
     * The two differ in what they ARE, so they differ in how they draw:
     *
     * - a COLLISION hull is a closed solid standing in for the model, and nobody switches one on by
     *   accident, so it draws solid and occludes - which is what makes its shape readable;
     * - a SHADOW VOLUME is an extruded silhouette whose interior is the point, so it stays
     *   translucent and writes no depth.
     */
    private debugMaterial(extras: MaterialExtras, spec: MaterialSpec): THREE.Material {
        // The effect's own declared colour where there is one, and the colour that effect DEFAULTS
        // to otherwise. A hull authored on `alDefault.fx` - 174 of them across the shipped models -
        // declares no swatch at all, so the fallback has to come from what KIND of hull this is, or
        // the Nebulon-B's `COLLISION` draws in the shadow volume's cyan.
        const volume = spec.archetype === 'shadow-volume';
        const colour = debugColour(extras)
            ?? (volume ? { r: 0, g: 1, b: 1, a: 1 } : { r: 0, g: 0, b: 1, a: 1 });

        const material = new THREE.MeshBasicMaterial({
            color: new THREE.Color(colour.r, colour.g, colour.b),
            opacity: Math.min(colour.a, DEBUG_HULL_OPACITY),
            transparent: true,
            // No depth write, so the hull layers OVER the model rather than carving it out - the
            // model has to stay visible through it for the enclosure to be readable.
            depthWrite: false,
            // Front faces only. Double-sided doubles the translucent layers to see through, and a
            // closed hull's back faces add nothing to reading its shape.
            side: THREE.FrontSide,
        });

        material.userData = { spec, extras };
        return material;
    }

    /**
     * Applies the team colour to one material, if that material takes it.
     *
     * Which materials do is the seam's business: a `*Colorize` shader, or an `FC_`-prefixed mesh,
     * which takes the colour whatever its shader says.
     *
     * PER PIXEL, weighted by the base texture's ALPHA - not a flat tint over the whole sub-mesh.
     * That is what the shipped shaders do, `lerp(base.rgb, Colorization * base.rgb, base.a)`, and
     * it is the difference between an Imperial hull with coloured markings and an Imperial hull
     * painted entirely green. `material.color` therefore stays white; the tint is injected into the
     * standard material's own fragment shader, because nothing three offers expresses "multiply by
     * this colour only where the map's alpha says so".
     */
    private tint(material: THREE.MeshStandardMaterial | THREE.MeshBasicMaterial): void {
        const spec = (material.userData as { spec?: { colorize?: boolean } }).spec;
        if (spec?.colorize !== true) {
            return;
        }

        material.color.setHex(0xffffff);
        this.installColorization(material);

        const uniform = (material.userData as { colorization?: { value: THREE.Color } })
            .colorization;
        if (uniform === undefined) {
            return;
        }

        // White is the identity for a multiply, so an untinted mesh keeps its texture exactly.
        if (this.colorization === null) {
            uniform.value.setRGB(1, 1, 1);
            return;
        }

        uniform.value.setRGB(this.colorization.r, this.colorization.g, this.colorization.b);
    }

    /**
     * Teaches one standard material to tint by the base map's alpha.
     *
     * `sampledDiffuseColor` is declared by three's own `map_fragment` chunk and is still in scope
     * after it, so the mask is the texture's real alpha rather than anything already multiplied
     * into `diffuseColor`.
     */
    private installColorization(
        material: THREE.MeshStandardMaterial | THREE.MeshBasicMaterial,
    ): void {
        const data = material.userData as { colorization?: { value: THREE.Color } };
        if (data.colorization !== undefined) {
            return;
        }

        const colorization = { value: new THREE.Color(1, 1, 1) };
        data.colorization = colorization;

        material.onBeforeCompile = shader => {
            shader.uniforms.aetColorization = colorization;

            shader.fragmentShader = shader.fragmentShader
                .replace('void main() {', 'uniform vec3 aetColorization;\nvoid main() {')
                .replace('#include <map_fragment>', `#include <map_fragment>
#ifdef USE_MAP
    diffuseColor.rgb = mix(
        diffuseColor.rgb, aetColorization * diffuseColor.rgb, sampledDiffuseColor.a);
#endif`);
        };

        // three caches programs by this key, so two materials differing only in the injection would
        // otherwise share one compiled shader.
        material.customProgramCacheKey = () => 'aet-colorize';
    }

    /**
     * Applies a shader's declared render state to every material using it.
     *
     * This is the fidelity that needs no shader translation: the effects spell their blend, depth
     * and cull state out declaratively. A `custom` blend is left alone deliberately - the archetype
     * guess is better than a wrong one.
     */
    applyShaderState(shaderName: string, state: FxMaterialState): void {
        this.shaderStates.set(shaderName.toLowerCase(), state);
        this.applyStoredShaderState(shaderName.toLowerCase(), state);
    }

    /**
     * Puts one shader's render state onto every material currently using it.
     *
     * Both kinds: a translated `AlamoMaterial` needs the blend and depth state just as much as the
     * archetype does, and skipping it drew meshes that should be alpha-blended with depth writes on
     * - which is how an inner hull ends up occluding the model around it.
     */
    private applyStoredShaderState(wanted: string, state: FxMaterialState): void {
        this.forEachDrawnMesh(node => {
            const material = node.material;
            const extras = node.userData.alamo as MaterialExtras | undefined;

            if (Array.isArray(material) || !(material instanceof THREE.Material)
                || (extras?.alamoShader ?? '').toLowerCase() !== wanted) {
                return;
            }

            // A collision hull or shadow volume is not drawn by the engine at all, so the state its
            // effect declares describes a pass we are not running - `MeshCollision.fx` asks for
            // opaque depth writes, which turned the translucent blue debug hull back into a solid
            // box over the model. The debug material keeps its own state.
            if (resolveMaterial(extras ?? {}).hidden) {
                return;
            }

            applyDepth(material, state);
            material.side = state.cull === 'none'
                ? THREE.DoubleSide
                : state.cull === 'front' ? THREE.BackSide : THREE.FrontSide;

            // Two different mechanisms for the same declaration. An archetype material is one of
            // three's own, which reads `alphaTest` and injects the discard itself; a translated
            // effect is a RawShaderMaterial, which gets no such chunk and needs the threshold as a
            // uniform. Missing the second is what drew `Tree.fx`'s palm fronds - a leaf texture on
            // a plain rectangle - as solid green cards.
            if (material instanceof AlamoMaterial) {
                material.setAlphaTest(state.alphaTest);
            } else {
                // Cleared rather than left, so a material that inherited a cutoff does not keep
                // one the effect never asked for.
                material.alphaTest = state.alphaTest ?? 0;
            }

            applyBlend(material, state.blend);
            material.needsUpdate = true;
        });
    }

    /** Every distinct shader name the loaded geometry names, so each is fetched once. */
    shaderNames(): string[] {
        const names = new Set<string>();

        this.modelRoot.traverse(node => {
            if (!(node instanceof THREE.Mesh)) {
                return;
            }

            const shader = (node.userData.alamo as MaterialExtras | undefined)?.alamoShader ?? '';
            if (shader !== '') {
                names.add(shader);
            }
        });

        return [...names].sort();
    }

    /** Sets the team tint, or clears it back to the model's own colours. */
    setColorization(colour: NormalisedColour | null): void {
        this.colorization = colour;

        this.forEachDrawnMesh(node => {
            const material = node.material;

            if (material instanceof AlamoMaterial) {
                // The effect masks the tint itself; it only needs the colour.
                material.setColorization(colour);
            } else if (material instanceof THREE.MeshStandardMaterial
                || material instanceof THREE.MeshBasicMaterial) {
                // Basic as well as standard: an FC_ mesh on an additive shader is unlit and still
                // takes the team colour, so checking only for standard left it untinted.
                this.tint(material);
            }
        });
    }

    /** Which distinct sub-mesh names take the team colour, for the dock to report. */
    colorizedMeshes(): string[] {
        const names = new Set<string>();

        this.modelRoot.traverse(node => {
            if (!(node instanceof THREE.Mesh)) {
                return;
            }

            const data = node.material as { userData?: { spec?: { colorize?: boolean } } };
            const extras = node.userData.alamo as MaterialExtras | undefined;

            if (data.userData?.spec?.colorize === true && extras?.alamoMesh !== undefined) {
                names.add(extras.alamoMesh);
            }
        });

        return [...names].sort();
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
        this.translated.set(shaderName.toLowerCase(), effect);

        if (this.translatedShaders) {
            this.rebuildMaterials();
        }
    }

    /** Whether to draw with translated shaders where one is available. */
    setTranslatedShaders(enabled: boolean): void {
        if (this.translatedShaders === enabled) {
            return;
        }

        this.translatedShaders = enabled;
        this.rebuildMaterials();
    }

    /** How many loaded sub-meshes a translated shader actually reaches, out of how many there are. */
    translatedCoverage(): { translated: number; total: number } {
        let translated = 0;
        let total = 0;

        this.forEachPartMesh(mesh => {
            if (!meshDrawn(mesh)) {
                return;
            }

            total++;
            if (mesh.material instanceof AlamoMaterial) {
                translated++;
            }
        });

        return { translated, total };
    }

    /**
     * Feeds every translated material what the engine would recompute this frame.
     *
     * Per mesh rather than per scene, because half the contract is object-relative: WORLD and
     * WORLDVIEWPROJECTION differ for a turret mounted on a hull, and a shader working in object
     * space wants the eye and the lights transformed into ITS space, not the hull's.
     */
    private updateAlamoUniforms(): void {
        const materials: { mesh: THREE.Mesh; material: AlamoMaterial }[] = [];

        this.modelRoot.traverse(node => {
            if (node instanceof THREE.Mesh && node.material instanceof AlamoMaterial
                && node.visible) {
                materials.push({ mesh: node, material: node.material });
            }
        });

        if (materials.length === 0) {
            return;
        }

        this.camera.updateMatrixWorld();

        const view = this.camera.matrixWorldInverse;
        const projection = this.camera.projectionMatrix;
        const viewProjection = new THREE.Matrix4().multiplyMatrices(projection, view);
        const eyeWorld = this.camera.getWorldPosition(new THREE.Vector3());

        for (const { mesh, material } of materials) {
            material.updateFrame(this.frameFor(mesh, view, projection, viewProjection, eyeWorld));
        }
    }

    /** The engine's per-frame contract, as it stands for one mesh. */
    private frameFor(
        mesh: THREE.Mesh, view: THREE.Matrix4, projection: THREE.Matrix4,
        viewProjection: THREE.Matrix4, eyeWorld: THREE.Vector3,
    ): AlamoFrame {
        const world = mesh.matrixWorld;
        const worldInverse = new THREE.Matrix4().copy(world).invert();
        const worldView = new THREE.Matrix4().multiplyMatrices(view, world);

        const toObject = (v: THREE.Vector3): [number, number, number] => {
            const out = v.clone().applyMatrix4(worldInverse);
            return [out.x, out.y, out.z];
        };

        // Direction TO the light, which is what the engine's vectors mean. A three
        // DirectionalLight's position IS its direction, since it always targets the origin.
        // Direction off the three light, because that is where `placeKeyLight` put it - but the
        // COLOUR and intensity off the rig, because three's copy of them is multiplied by PI for a
        // BRDF the engine's shaders do not have. See `engineLight`.
        const settings = [this.rig.sun, this.rig.fill1, this.rig.fill2];

        const lights = this.alamoLights.map((light, index) => {
            const direction = light.position.clone().normalize();
            const rotationOnly = new THREE.Matrix4().extractRotation(worldInverse);
            const inObject = direction.clone().applyMatrix4(rotationOnly).normalize();

            return engineLight(
                settings[index] ?? this.rig.sun,
                this.rig.specular,
                [direction.x, direction.y, direction.z],
                [inObject.x, inObject.y, inObject.z]);
        });

        // The Alamo shaders do their own bone lookup, so the palette is handed over rather than
        // left to three's vertex shader - but not raw. `Skeleton.boneMatrices` maps BIND WORLD
        // space to current world space, while the shader applies it to a mesh-local vertex and then
        // multiplies by WORLDVIEWPROJECTION, which carries the world transform a second time.
        // Conjugating by the bind matrices is what three's own skinning does, and it lands the
        // result back in mesh-local space where the rest of the shader expects it. Without it the
        // turret's barrel flew off and its dome collapsed.
        const skinMatrices = mesh instanceof THREE.SkinnedMesh
            ? this.skinPalette(mesh)
            : null;

        return {
            wind: this.wind,
            matrices: {
                world: world.elements,
                worldInverse: worldInverse.elements,
                worldView: worldView.elements,
                worldViewInverse: new THREE.Matrix4().copy(worldView).invert().elements,
                worldViewProjection:
                    new THREE.Matrix4().multiplyMatrices(viewProjection, world).elements,
                view: view.elements,
                viewInverse: this.camera.matrixWorld.elements,
                viewProjection: viewProjection.elements,
                projection: projection.elements,
            },
            eyeWorld: [eyeWorld.x, eyeWorld.y, eyeWorld.z],
            eyeObject: toObject(eyeWorld),
            lights,
            // The rig's own ambient, the same value the harmonics probe is built from. This
            // used to read a hard-coded `ambientLevel` field, and when that went the reference
            // stayed: the uniform was being handed `undefined` on every draw, which the bundler
            // never saw because only the webview's own tsconfig type-checks this file.
            ambient: [
                this.rig.ambient.colour[0] * this.rig.ambient.intensity,
                this.rig.ambient.colour[1] * this.rig.ambient.intensity,
                this.rig.ambient.colour[2] * this.rig.ambient.intensity,
                1,
            ],
            lightScale: [1, 1, 1, 1],
            time: this.elapsed,
            resolution: [this.canvas.clientWidth, this.canvas.clientHeight, 0, 0],
            sphericalHarmonics: this.harmonics,
            skinMatrices,
        };
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
            .filter(part => part.subjectId === ACTIVE_SUBJECT)
            .map(part => part.root);
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
        this.walkModelMeshes([...this.parts.values()].map(part => part.root), visit);
    }

    private walkModelMeshes(
        roots: readonly THREE.Object3D[], visit: (mesh: THREE.Mesh) => void,
    ): void {
        // A manual walk rather than `traverse`, because this has to PRUNE. A particle system is
        // parented to a BONE so its smoke follows the hull as it animates - which puts it inside the
        // part's own root, where `traverse` still finds it. Scoping to the part roots was not enough
        // on its own: the billboards were still being handed model materials, and still turning up
        // in the mesh list as if they were geometry.
        const walk = (node: THREE.Object3D): void => {
            if (node.userData.aetParticleSystem === true) {
                return;
            }

            // The stencil volumes' counting meshes are this class's own scaffolding, not the
            // model's geometry. Left in, they took model materials, turned up as phantom tree rows
            // and were swept by the wireframe - the same shape of bug the particle prune fixes.
            if (node instanceof THREE.Mesh && node.userData.aetShadowVolume !== true) {
                visit(node);
            }

            for (const child of node.children) {
                walk(child);
            }
        };

        // Only the OUTERMOST roots. A hardpoint's model hangs off a bone of the hull, so it is
        // already inside the hull's root - walking every part in turn visited each mounted mesh
        // twice. See `outermostRoots`.
        for (const root of outermostRoots(roots)) {
            walk(root);
        }
    }

    /**
     * Samplers on translated materials that are reading nothing, in words.
     *
     * A sampler with no texture reads black, and the shader then draws something plausible and
     * wrong - the sort of thing that looks like a translation bug and is actually a missing file.
     */
    shaderTextureProblems(): string[] {
        const problems = new Set<string>();

        this.forEachPartMesh(mesh => {
            if (!(mesh.material instanceof AlamoMaterial)) {
                return;
            }

            const shader = (mesh.userData.alamo as MaterialExtras | undefined)?.alamoShader
                ?? 'a shader';

            for (const { sampler, file } of mesh.material.unboundSamplers()) {
                problems.add(`${shader}: sampler '${sampler}' is still waiting for '${file}'.`);
            }
            for (const sampler of mesh.material.unnamedSamplers) {
                problems.add(`${shader}: sampler '${sampler}' has no texture on this sub-mesh.`);
            }
        });

        return [...problems];
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
        const palette = this.skinScratch.length === bones * 16
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

    /** Rebuilds every mesh's material for the current translated/archetype choice. */
    private rebuildMaterials(): void {
        this.forEachDrawnMesh(mesh => this.applyMaterial(mesh));

        // Same reason as after a part loads: these are different material objects, and a flag set
        // on the ones they replace is gone.
        this.applyWireframe();

        // New material objects, so everything already known has to be applied to them again.
        for (const [shader, state] of this.shaderStates) {
            this.applyStoredShaderState(shader, state);
        }

        // Materials are new objects, so everything already decoded has to be handed over again.
        for (const [name, texture] of this.textures) {
            this.setTexture(name, texture);
        }
    }

    /** Applies a decoded texture wherever a material asked for it by name. */
    setTexture(name: string, texture: THREE.Texture): void {
        this.textures.set(name, texture);
        this.bindTextureIn(this.modelRoot, name, texture);
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
        for (const [name, texture] of this.textures) {
            this.bindTextureIn(root, name, texture);
        }
    }

    private bindTextureIn(root: THREE.Object3D, name: string, texture: THREE.Texture): void {
        const wanted = name.toLowerCase();

        root.traverse(node => {
            if (!(node instanceof THREE.Mesh)) {
                return;
            }

            if (node.material instanceof AlamoMaterial) {
                texture.colorSpace = THREE.SRGBColorSpace;
                node.material.bindTexture(name, texture);
                return;
            }

            const material = node.material as THREE.MeshStandardMaterial | THREE.MeshBasicMaterial;
            const spec = material.userData?.spec;
            if (spec === undefined) {
                return;
            }

            if (spec.textures.base?.toLowerCase() === wanted) {
                texture.colorSpace = THREE.SRGBColorSpace;
                material.map = texture;
                material.color.setHex(0xffffff);
                material.needsUpdate = true;
            }

            // Only a lit material has a normal map to put this in; an unlit one has no normal to
            // perturb, and assigning it would be a silent no-op that reads as a working bind.
            if (spec.textures.normal?.toLowerCase() === wanted
                && material instanceof THREE.MeshStandardMaterial) {
                material.normalMap = texture;
                material.needsUpdate = true;
            }
        });
    }

    /** Removes everything, so a new subject starts from an empty scene. */
    clear(): void {
        for (const entry of this.particleSystems.values()) {
            entry.instance.dispose();
        }
        this.particleSystems.clear();
        // A new subject is one of the two things allowed to cut a fading system short.
        this.clearFadingParticles();

        // BEFORE the sweep below, not after: a wreck is one of those parts now, and taking it
        // apart a second time would dispose a tree that has already gone.
        this.clearBreakoffs();

        for (const part of this.parts.values()) {
            part.root.removeFromParent();
            disposeTree(part.root);
        }

        this.clearFireArcs();

        // A new subject asks for its own bones. Keeping the old subject's requests would report a
        // Star Destroyer's missing mount against a Mon Calamari Cruiser for the rest of the session.
        this.attachRequests.clear();
        this.cachedSpan = null;
        // The marks name parts that are about to stop existing; leaving them would draw a reticle
        // over the next model's geometry at whatever the last one's bounds happened to be.
        this.reticles = [];
        this.hoveredReticle = null;
        this.clearReticles();
        this.parts.clear();
        this.billboards = [];
        this.hiddenParts.clear();
        this.transient.clear();
        this.knownDecals = new Set();
        this.shownDecals = new Set();
        this.collisionMeshes = new Set();
        this.shieldRevealed = false;
        // Every subject's clips, mixer, tracks, rest pose and node names at once. They hold nodes
        // of a model nobody is looking at any more, and keeping them would leak the whole graph.
        this.subjects.clear();
        this.overridingVisibility = false;
    }

    // ── damage ────────────────────────────────────────────────────────────────

    /**
     * Which decal meshes exist, and which of them are showing.
     *
     * Both together, because a decal that is not showing has to be actively hidden: the model ships
     * these meshes VISIBLE - measured on Ev_stardestroyer.alo, where HP_F-L_Blast is both a bone and
     * a mesh - so the engine hides them until the mount is destroyed, and so must this.
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
        const names = new Set<string>();

        this.forEachPartMesh(mesh => {
            const extras = (mesh.userData.alamo ?? {}) as MaterialExtras;

            if (shieldMeshOffShader(extras)) {
                names.add(extras.alamoMesh ?? mesh.name);
            }
        });

        return [...names];
    }

    /** Shows or hides the shield mesh, for the abilities that reveal it. */
    setShieldRevealed(revealed: boolean): void {
        this.shieldRevealed = revealed;
        this.applyLevels();
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
            widthDegrees: number;
            heightDegrees: number;
            range: number;
        }[],
    ): void {
        this.clearFireArcs();

        // How BIG the subject is, so a 2000-unit reach on a 600-unit hull is drawn at a length the
        // reader can stand outside of. See `drawnConeRange` - the row still states the true range.
        const modelSize = this.modelSpan();

        for (const arc of arcs) {
            const parent = this.fireBoneAttachment(arc.partId, arc.bone);
            const drawn = {
                ...arc,
                range: drawnConeRange(
                    arc.range, modelSize, arc.widthDegrees, arc.heightDegrees),
            };
            const mesh = new THREE.Mesh(coneGeometry(drawn), FIRE_ARC_MATERIAL.clone());

            mesh.visible = this.arcsVisible;
            // The gizmo is a hint, not geometry: it must never occlude the hull it describes.
            mesh.renderOrder = 1;

            // A CHILD of the fill, so one visible flag governs both and an outline can never be
            // left burning over an arc that has been switched off.
            const outline = new THREE.LineSegments(
                coneOutlineGeometry(drawn), FIRE_ARC_OUTLINE_MATERIAL.clone());
            outline.renderOrder = 2;
            mesh.add(outline);

            parent.add(mesh);
            this.arcs.push(mesh);
        }
    }

    /**
     * The node a fire bone names, looking on the HULL before the mounted model.
     *
     * A hardpoint model is cut from the hull and keeps its whole skeleton, so a bone like
     * `FP_F-L_00` exists on BOTH - and the copy inside the mounted model still sits at its
     * HULL-space position. Resolving it there placed it twice over, because the mounted model is
     * itself attached at the hardpoint's bone. Measured on `Calamari_Cruiser`: the hull's
     * `FP_F-L_00` is at (28, 3, 275), a few units ahead of its mount at (30, 2, 249) - and the
     * mounted model's copy is at (59, 6, 523), almost exactly double. Every one of its six banks
     * had its cone out in open space instead of at the muzzle.
     *
     * The fallback still matters: a mount that carries a muzzle bone the hull does not have is
     * resolved on the mount, which is the only place it exists.
     */
    private fireBoneAttachment(partId: string, bone: string): THREE.Object3D {
        return this.parts.get('hull')?.bones.get(bone.toLowerCase())
            ?? this.attachmentFor(partId, bone);
    }

    setFireArcsVisible(visible: boolean): void {
        this.arcsVisible = visible;
        for (const arc of this.arcs) {
            arc.visible = visible;
        }
    }

    private clearFireArcs(): void {
        for (const arc of this.arcs) {
            arc.removeFromParent();
            arc.geometry.dispose();
            (arc.material as THREE.Material).dispose();

            // The outline hangs off the fill, so it goes with it - it holds its own geometry and
            // its own cloned material, and neither is freed by dropping the parent.
            for (const child of arc.children) {
                if (child instanceof THREE.LineSegments) {
                    child.geometry.dispose();
                    (child.material as THREE.Material).dispose();
                }
            }
        }

        this.arcs.length = 0;
    }

    /**
     * Poses turrets at their rest angle.
     *
     * Traverse is about the bone's local Z. Alamo is Z-up, and the exporter's Z-up-to-Y-up rotation
     * sits on the ROOT node, so a bone's own axes still follow the file's convention - the same one
     * the fire bones showed: X forward, Y lateral, Z up.
     */
    setTurretRestAngles(
        poses: readonly { partId: string; bone: string; restAngleDegrees: number }[],
    ): void {
        for (const pose of poses) {
            const bone = this.parts.get(pose.partId)?.bones.get(pose.bone.toLowerCase());
            if (bone !== undefined) {
                bone.rotation.z = pose.restAngleDegrees * (Math.PI / 180);
            }
        }
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
        this.turretSweeps = [...sweeps];

        if (sweeps.length === 0) {
            this.sweepPhase = 0;
        }
    }

    /** Advances the sweep. Called from the render loop; a no-op when nothing is sweeping. */
    private advanceTurretSweep(deltaSeconds: number): void {
        if (this.turretSweeps.length === 0) {
            return;
        }

        // Twelve seconds end to end. Slow enough to follow a barrel with the eye, which is what a
        // reader checking a traverse is actually doing.
        this.sweepPhase = (this.sweepPhase + deltaSeconds / 12) % 1;

        for (const sweep of this.turretSweeps) {
            const pose = sweepAngles(sweep.turret, this.sweepPhase);
            const part = this.parts.get(sweep.partId);

            const turret = part?.bones.get(sweep.turretBone.toLowerCase());
            if (turret !== undefined) {
                turret.rotation.z = pose.rotate * (Math.PI / 180);
            }

            // The barrel carries the elevation, when the model separates the two. A turret with no
            // barrel bone elevates on its own bone instead - 19 objects name a turret bone and only
            // 17 a barrel, so the pair is genuinely optional.
            const barrel = sweep.barrelBone === null
                ? undefined
                : part?.bones.get(sweep.barrelBone.toLowerCase());

            if (barrel !== undefined) {
                barrel.rotation.x = pose.elevate * (Math.PI / 180);
            } else if (turret !== undefined) {
                turret.rotation.x = pose.elevate * (Math.PI / 180);
            }
        }
    }

    /**
     * Drops a piece of wreckage where a mount used to be.
     *
     * A PASSIVE SUBJECT loaded by the one loader, which is all that is left here that is specific
     * to debris: where the mount was, and the clock it runs on. It used to load itself, and the
     * hand copy drifted - four of its six duplicated steps were added one bug report at a time,
     * and nine more were simply missing.
     *
     * Its own subject rather than another piece of the ship: debris is transient, it is removed on
     * its own clock, it is drawn by its own file's rules, and re-parenting the mount's own model
     * would fight the destruction rules that hid it.
     */
    async addBreakoff(
        key: string, glbBase64: string, at: BreakoffAnchor,
        prop: PreviewBreakoffProp,
    ): Promise<void> {
        // Where the mount was, in world space, captured once - the debris does not follow the ship.
        // WHICH node that is, is `breakoffAnchor`'s decision and not this one's: a mount model can
        // carry a copy of the hull's own bones, and resolving the attachment bone by name inside it
        // applied the offset twice.
        const anchor = this.attachmentFor(at.partId, at.bone, at.boneIndex);
        const origin = anchor.getWorldPosition(new THREE.Vector3());
        const facing = anchor.getWorldQuaternion(new THREE.Quaternion());

        // The wreck's part id and its subject id are the same string on purpose: it is one model
        // and one subject, and the effects it trails already address it by this name.
        const wreckId = `${BREAKOFF_ATTACHMENT}${key}`;

        const root = await this.addPart(wreckId, glbBase64, undefined, undefined, {
            subjectId: wreckId,
            pose: { position: origin, quaternion: facing },
        });

        this.breakoffs.push({
            key, partId: wreckId, root, prop, origin, facing,
            age: 0,
            lifetime: breakoffLifetime(prop),
            expired: false,
        });
    }

    /** Clears every piece of wreckage - a repair puts the mounts back, so the debris goes. */
    clearBreakoffs(): void {
        for (const debris of this.breakoffs) {
            this.removePart(debris.partId);
        }

        this.breakoffs.length = 0;
    }

    /**
     * Told when a piece of wreckage reaches the end of its declared lifetime.
     *
     * A callback rather than something polled: it is an EVENT, like a hardpoint's own death, and
     * the explosion it triggers has to be fetched by the client - the viewport has no way to ask
     * for a particle system itself.
     */
    onBreakoffExpired: ((key: string, prop: PreviewBreakoffProp) => void) | null = null;

    /** Whether a piece of wreckage for this key is already in the air. */
    hasBreakoff(key: string): boolean {
        return this.breakoffs.some(debris => debris.key === key);
    }

    /**
     * Drifts the wreckage and retires it when its time is up.
     *
     * Recomputed from the age rather than integrated per frame: an incremental drift accumulates
     * float error, and debris that ends up somewhere slightly different each run is exactly what
     * makes two looks at the same mount disagree.
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
                const subjectId = this.parts.get(debris.partId)?.subjectId ?? debris.partId;

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
                debris.origin.z + pose.offset.z);

            const spin = new THREE.Euler(
                pose.rotation.x * (Math.PI / 180),
                pose.rotation.y * (Math.PI / 180),
                pose.rotation.z * (Math.PI / 180));

            debris.root.quaternion.copy(debris.facing)
                .multiply(new THREE.Quaternion().setFromEuler(spin));

            alive.push(debris);
        }

        this.breakoffs.length = 0;
        this.breakoffs.push(...alive);
    }

    /** Breaks a mounted part off, or puts it back. */
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

        this.holdEffectsOf(partId);
    }

    /**
     * Holds the effects hanging off a part while that part is off screen.
     *
     * An emitter nobody is drawing must not spawn - the rule `EmitterRenderer` already follows one
     * level down, and the reason its clock is held with it. It was never applied to a whole PART,
     * and a death clone is exactly the case that needs it: the clone is fetched early and held
     * hidden so its arrival is not a visible gap, which gave its effects a free run at a model
     * nobody could see. Its explosions are ONE-SHOTS - `p_explosion_big00` fires a single burst of
     * 50 sparks - so by the time the ship actually died they were spent, and the blast that covers
     * the wreck breaking up drew two stray particles.
     *
     * Only ever HOLDS here. Releasing is the clip's business: `advancePassiveSubjects` gates a
     * playing subject frame by frame, and a subject with no clip is released as it is shown.
     */
    private holdEffectsOf(partId: string): void {
        const hidden = this.hiddenParts.has(partId);

        for (const entry of this.particleSystems.values()) {
            if (entry.attachToPartId === partId) {
                entry.instance.hold('part', hidden);
            }
        }
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
        for (const [id, entry] of [...this.particleSystems]) {
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
        if (part.subjectId !== ACTIVE_SUBJECT && this.rootPartOf(part.subjectId) === null) {
            this.subjects.delete(part.subjectId);
        }
    }

    /**
     * Gives up everything the scene registered for a subtree that is about to be disposed.
     *
     * The counterpart of what {@link addPart} records, and it did not exist while only the hull
     * and its mounts came through there - a scene part lives as long as the subject does. Now that
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
        root.traverse(node => inside.push(node));

        for (const node of inside) {
            if (node instanceof THREE.Mesh) {
                this.shadowVolumes.remove(node);
            }

            subject.restPose.delete(node);
            subject.fileHidden.delete(node);

            // Only where THIS node is the one holding the name. `nodeByName` is first-wins, so one
            // part of a subject must not take another part's entry with it.
            if (node.name !== '' && subject.nodeByName.get(node.name) === node) {
                subject.nodeByName.delete(node.name);
            }
        }

        const held = new Set(inside);
        this.billboards = this.billboards.filter(node => !held.has(node));
    }

    /**
     * Plays a system once and forgets it.
     *
     * A death explosion is an event, not a state: it fires as the mount is destroyed and must not
     * come back when the effect list is toggled or the levels change.
     */
    playOnce(
        id: string,
        system: AlamoParticleContent,
        attachToPartId?: string,
        attachBone?: string,
        /** The owning object's uniform render scale. See {@link addParticleSystem}. */
        scaleFactor = 1,
    ): void {
        this.addParticleSystem(
            id, system, attachToPartId, attachBone, undefined, undefined, scaleFactor);
        this.transient.add(id);
    }

    // ── levels ────────────────────────────────────────────────────────────────

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

        this.modelRoot.traverse(node => {
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

        for (const entry of this.particleSystems.values()) {
            tagged.push(entry.levels);
        }

        return definedLevels(tagged);
    }

    private applyLevels(): void {
        // What the LEVEL knows, and nothing else. Deciding visibility here as well is what let two
        // systems write the same thing: this one re-derived every mesh from the old override maps
        // and undid whatever the row chain had just settled, so a mesh hidden from the tree came
        // straight back on the next level or material refresh.
        for (const entry of this.particleSystems.values()) {
            entry.levelVisible =
                proxyVisibleAt(entry.levels, this.alt, this.lod, this.altDescending);
        }

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

        if (resolveMaterial(extras).archetype === 'shadow-volume') {
            this.shadowVolumes.setCounting(mesh, isVisibleAt(extras, this.alt, this.lod));
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
        const bones = new Map(this.skeleton().map(bone => [bone.index, bone]));

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
                target.particleId === undefined ? facts : this.effectFacts(facts, target.particleId));

            resolutions.set(rowId, answer);
            meshAnswers.set(rowId, resolveRow(facts));
            subtree.set(rowId, resolveRow(subtreeFacts(facts)));
        }

        this.rowResolutions = resolutions;
        this.rowSubtrees = subtree;

        for (const [rowId, target] of this.rowTargets) {
            const visible = resolutions.get(rowId)?.visible ?? true;

            if (target.mesh !== undefined) {
                setMeshDrawn(target.mesh, meshAnswers.get(rowId)?.visible ?? visible);
            }

            if (target.boneIndex !== undefined) {
                const node = this.hull()?.bonesByIndex.get(target.boneIndex);

                // `visible` on a bone PRUNES ITS SUBTREE, so it takes the subtree answer, not the
                // row's. On a merged row the two differ: the file marking a proxy's marker mesh
                // hidden says nothing about the effect hanging off that same bone.
                if (node !== undefined) {
                    node.visible = subtree.get(rowId)?.visible ?? visible;
                }
            }

            if (target.particleId !== undefined) {
                this.setParticleSystemDrawn(target.particleId, visible);
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
        const parentResolution = parent === null ? undefined : subtree.get(parent);

        const facts: RowFacts = {
            inFile: true,
            gated: false,
            // No masters here. The effects master belongs to the EFFECT half of a row and is added
            // by `effectFacts` - put on the base facts it would reach the marker mesh and, through
            // the subtree answer, the proxy bone, so switching effects off would prune geometry.
            masters: [],
            override: this.rowOverrides.get(rowId),
            ancestorHidden: parentResolution?.visible === false
                ? this.rowNames.get(parent ?? '') ?? 'a bone above it'
                : undefined,

            // The ancestor as the MODEL leaves it, which is a different question: a bone the reader
            // unticked hides its subtree, but the model still draws every row in it. Without this
            // the whole subtree claimed the model did not draw it and went italic on one tick.
            ancestorHiddenAuthored: parentResolution?.authored === false
                ? this.rowNames.get(parent ?? '') ?? 'a bone above it'
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

            facts.inFile = shield ? this.shieldRevealed : extras.alamoHidden !== true;
            facts.gated = spec.hidden
                || !(shield
                    ? isVisibleAtLevel(extras, this.alt, this.lod)
                    : isVisibleAt(extras, this.alt, this.lod))
                || this.collisionGatedOff(extras);

            // A damage decal's bottom of the chain is the DAMAGE RULE, not the file - see
            // `damageMeshFacts`. It used to enter as a `gated` veto beside the level gates, which
            // reached the same answer only because no shipped decal is marked hidden.
            const decal = (extras.alamoMesh ?? '').toLowerCase();
            if (this.knownDecals.has(decal)) {
                Object.assign(facts, damageMeshFacts(facts, this.shownDecals.has(decal)));
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
            facts.resting = this.restingVisibilityOf(id);
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
        const entry = this.particleSystems.get(systemId);

        return {
            // Which drops the ancestors the MODEL hides: an effect answers to its own bone, as the
            // engine does. See `visibility.effectFacts`.
            ...effectRowFacts(facts),

            // The one master anyone legitimately keeps switched off while watching a clip: effects
            // are what obstructs the view of the thing being checked.
            masters: [{ id: 'effects', on: this.particlesVisible }],
            inFile: entry?.gateVisible ?? true,
            gated: entry !== undefined && !entry.levelVisible,
        };
    }

    /** A mesh a hardpoint names as its collision hull, which the engine never draws. */
    private collisionGatedOff(extras: MaterialExtras): boolean {
        return this.collisionMeshes.has((extras.alamoMesh ?? '').toLowerCase());
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
        const clip = restingClip([...this.active.visibility.keys()]);

        return clip === undefined ? undefined : this.trackedAt(clip, id, 0);
    }

    private animatedVisibilityOf(id: BoneId | undefined): boolean | undefined {
        return this.active.action === null
            ? undefined
            : this.trackedAt(this.active.action.getClip().name, id, this.active.action.time);
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
            [...hull.bonesByIndex.values()].map(node => [node.name, node] as const));
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
    private trackedAt(clip: string, id: BoneId | undefined, seconds: number): boolean | undefined {
        const track = this.active.visibility.get(clip);

        if (id === undefined || track === undefined) {
            return undefined;
        }

        const key = track.bones.has(id) ? id : resolveBoneId(track.bones.keys(), id);
        const bits = key === undefined ? undefined : track.bones.get(key);

        return bits === undefined ? undefined : !hiddenAt(bits, track.fps, seconds);
    }

    /**
     * Everything the model is made of, as flat items for ONE tree: bones, the meshes hanging off
     * them, and the particle emitters attached to them.
     *
     * A mesh's origin IS a bone and an emitter is attached to one, so the skeleton is already the
     * structure all three live in. Ids are prefixed by kind so they cannot collide.
     */
    /** For the harness: drives controls that have no UI yet. Costs nothing when unused. */
    expose(): void {
        (globalThis as unknown as { __aetViewport?: unknown }).__aetViewport = this;
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

        const rootBone = [...hull.bonesByIndex.keys()].sort((a, b) => a - b)[0] ?? 0;

        // From the object ITSELF, not from its parent: an effect names its owning bone directly
        // rather than hanging off it, so the answer is usually the very node it was handed.
        const ownerOf = (object: THREE.Object3D): number => {
            for (let at: THREE.Object3D | null = object; at !== null; at = at.parent) {
                const index = boneOf.get(at);
                if (index !== undefined) {
                    return index;
                }
            }

            if (object instanceof THREE.SkinnedMesh) {
                const first = object.skeleton.bones[0];
                const index = first === undefined ? undefined : boneOf.get(first);

                if (index !== undefined) {
                    return index;
                }
            }

            return rootBone;
        };

        const meshByBone = new Map<number, THREE.Mesh>();
        const extraMeshes: { mesh: THREE.Mesh; owner: number }[] = [];

        // The effect a proxy bone IS, rather than one hanging off it - see `effectPlacement`. Held
        // by bone so the bone loop below can claim both halves as one row, the way it already does
        // for a mesh of the same name.
        const effectByBone = new Map<number, string>();

        // Rebuilt from scratch: a row that no longer exists must not keep answering for a scene
        // object that has been unloaded.
        this.rowTargets.clear();
        this.rowParents.clear();
        this.rowNames.clear();

        const bones = this.skeleton();
        const roots = new Set(bones.filter(bone => bone.parent < 0).map(bone => bone.index));

        const boneName = (index: number): string => {
            const node = hull.bonesByIndex.get(index);
            return node === undefined ? '' : alamoBoneName(node.name)?.name ?? node.name;
        };

        this.forEachPartMesh(mesh => {
            // The loader guarantees a mesh is never a bone node itself, so its owner is simply the
            // nearest bone above it. `meshPlacement` decides on the two NAMES from there.
            const owner = ownerOf(mesh);
            const extras = (mesh.userData.alamo ?? {}) as MaterialExtras;

            const placement = meshPlacement(owner, roots,
                { bone: boneName(owner), mesh: extras.alamoMesh ?? mesh.name });

            // A bone carrying two meshes keeps the first as its own row; the second stays a child
            // rather than being dropped.
            if ('merge' in placement && !meshByBone.has(placement.merge)) {
                meshByBone.set(placement.merge, mesh);
            } else {
                extraMeshes.push({
                    mesh,
                    owner: 'merge' in placement ? placement.merge : placement.childOf,
                });
            }
        });

        // The same question for the effects, before any row is claimed: a system whose proxy bone
        // carries its name IS that row. A bone with two of them keeps the first, exactly as it
        // keeps the first of two meshes.
        for (const [systemId, entry] of this.particleSystems) {
            const owner = ownerOf(anchorNodeOf(entry));
            const placement = effectPlacement(owner, roots,
                { bone: boneName(owner), effect: entry.system.name });

            if ('merge' in placement && !effectByBone.has(placement.merge)) {
                effectByBone.set(placement.merge, systemId);
            }
        }

        const meshItem = (mesh: THREE.Mesh): { name: string; visible: boolean; gatedOff: boolean } => {
            const extras = (mesh.userData.alamo ?? {}) as MaterialExtras;

            return {
                // The ALO's own mesh name, not the glTF node name.
                name: extras.alamoMesh ?? mesh.name,
                visible: meshDrawn(mesh),
                gatedOff: resolveMaterial(extras).hidden
                    || !isVisibleAt(extras, this.alt, this.lod),
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
            const name = effect === undefined
                ? details?.name ?? bone.name
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
                kind: effect !== undefined ? 'particle' : mesh !== undefined ? 'mesh' : 'bone',
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
            this.inspectSources.set(`mesh:${treeKeyOf(mesh)}`, { mesh, ownerBone: owner });
            this.claimRow(`mesh:${treeKeyOf(mesh)}`, details.name, boneId(owner), { mesh });

            items.push({
                id: `mesh:${treeKeyOf(mesh)}`,
                kind: 'mesh',
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
        for (const [systemId, entry] of this.particleSystems) {
            const owner = ownerOf(anchorNodeOf(entry));

            // Already folded into its proxy bone's row above. Listing it again would put the same
            // effect on screen twice, with two checkboxes writing one thing.
            if (effectByBone.get(owner) === systemId) {
                continue;
            }

            this.inspectSources.set(`particle:${systemId}`, { systemId });
            this.claimRow(`particle:${systemId}`, entry.system.name,
                boneId(owner), { particleId: systemId });

            items.push({
                id: `particle:${systemId}`,
                kind: 'particle',
                name: this.effectRowName(systemId),
                parentId: boneId(owner),
                visible: entry.gateVisible && entry.levelVisible,
                gatedOff: !entry.levelVisible,
                systemId,
            });
        }

        // Every row is known now, so the chain can run - parents are already ahead of children,
        // because the skeleton is walked parent-first and the meshes hang off it.
        this.resolveRows();

        return items.map(item => {
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
     * What a row carrying an effect is called.
     *
     * The `[heat]` tag goes on only when EVERY emitter is a heat distortion, which is the case that
     * draws nothing at all here. A system with one heat emitter among several still shows
     * something, and flagging it would read as a broken effect rather than an honest limitation.
     */
    private effectRowName(systemId: string): string {
        const entry = this.particleSystems.get(systemId);
        if (entry === undefined) {
            return '';
        }

        const heat = entry.instance.emitterIsHeat();

        return entry.system.name + (heat.length > 0 && heat.every(Boolean) ? ' [heat]' : '');
    }

    /**
     * Records what one row stands for.
     *
     * Called as each row is built, beside the row itself, so the two cannot drift apart. Everything
     * a row owns is applied from ONE answer - see `resolveRows`.
     */
    private claimRow(
        rowId: string, name: string, parentId: string | null,
        target: { boneIndex?: number; mesh?: THREE.Mesh; particleId?: string },
    ): void {
        this.rowTargets.set(rowId, target);
        this.rowParents.set(rowId, parentId);
        this.rowNames.set(rowId, name);
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
            bones.find(bone => bone.index === index)?.name ?? '';

        if (source.mesh !== undefined) {
            const extras = (source.mesh.userData.alamo ?? {}) as MaterialExtras;
            const geometry = source.mesh.geometry;
            const vertices = geometry.getAttribute('position')?.count ?? 0;

            found.push({
                kind: 'mesh',
                name: extras.alamoMesh ?? source.mesh.name,
                boneName: nameOf(source.ownerBone ?? source.boneIndex ?? -1),
                vertexCount: vertices,
                // An indexed geometry draws its index buffer, not its vertices - reading the vertex
                // count would under-report every shipped mesh, all of which are indexed.
                triangleCount: Math.floor((geometry.getIndex()?.count ?? vertices) / 3),
                drawn: meshDrawn(source.mesh),
                extras,
                meshIndex: typeof extras.alamoMeshIndex === 'number'
                    ? extras.alamoMeshIndex
                    : undefined,
                subMeshIndex: typeof extras.alamoSubMeshIndex === 'number'
                    ? extras.alamoSubMeshIndex
                    : undefined,
            });
        }

        if (source.boneIndex !== undefined) {
            const bone = bones.find(entry => entry.index === source.boneIndex);
            const node = this.hull()?.bonesByIndex.get(source.boneIndex);

            if (bone !== undefined) {
                found.push({
                    kind: 'bone',
                    index: bone.index,
                    name: bone.name,
                    parentIndex: bone.parent,
                    parentName: bone.parent < 0 ? '' : nameOf(bone.parent),
                    visible: bone.visible,
                    billboard: node === undefined ? null : billboardTypeOf(node),
                });
            }
        }

        if (source.systemId !== undefined) {
            const entry = this.particleSystems.get(source.systemId);

            if (entry !== undefined) {
                found.push({
                    kind: 'particle',
                    name: entry.system.name,
                    boneName: entry.attachBoneIndex === undefined
                        ? entry.attachBone ?? ''
                        : nameOf(entry.attachBoneIndex),
                    emitterCount: entry.system.emitters.length,
                    playing: this.rowResolutions.get(this.rowForSystem(source.systemId))?.visible
                        ?? (entry.gateVisible && entry.levelVisible),
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
            .filter(([, override]) => override === 'hidden')
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
            .filter(([, override]) => override === 'shown')
            .map(([row]) => row);
    }

    /**
     * Shows or hides any mix of bones, meshes and particle systems at once.
     *
     * One entry point, because the tree selects across kinds and a multi-select toggle has to move
     * all of them together. A bone carries what hangs off it, so switching one off takes its
     * subtree with it - which is what the reader is asking for when they untick a limb.
     */
    setItemsVisible(ids: readonly string[], visible: boolean): void {
        this.applyRowOverrides(
            ids.map(row => ({ row, override: visible ? 'shown' as const : 'hidden' as const })));
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
        return [...this.rowOverrides].map(([row, override]) => ({ row, override }));
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
     * Draws or holds one system, as the row chain decided.
     *
     * The chain has already weighed the effects master, the level gate and the reader, so this only
     * carries the answer - it must not weigh any of them a second time.
     */
    private setParticleSystemDrawn(id: string, visible: boolean): void {
        this.particleSystems.get(id)?.instance.setVisible(visible);
    }

    private refreshParticleVisibility(entry: ParticleEntry): void {
        // Deliberately does not decide anything itself. A system's row is in the chain like every
        // other row - the effects master vetoes it, its level gates it, the reader overrides it -
        // and a second opinion here would silently undo the first.
        void entry;
        this.refreshRows();
    }

    // ── camera ────────────────────────────────────────────────────────────────

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
            new THREE.Vector3().setFromMatrixPosition(at.matrixWorld));

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
            position.x - target.x, position.y - target.y, position.z - target.z);
        this.applyClipPlanes(this.boundingSphere(true), this.fitDistance);

        this.camera.position.set(position.x, position.y, position.z);
        this.controls.target.set(target.x, target.y, target.z);
        this.controls.update();
    }

    /** Where the camera stands now, for saving the current shot as a preset. */
    get cameraPosition(): Vector3Like {
        return { x: this.camera.position.x, y: this.camera.position.y, z: this.camera.position.z };
    }

    /** The bounding sphere a preset is resolved against, so the caller sizes the shot correctly. */
    get subjectSphere(): { center: Vector3Like; radius: number } {
        return this.boundingSphere();
    }

    frameAll(preset: PresetView = 'threeQuarter'): void {
        const sphere = this.boundingSphere();
        const { azimuth, elevation } = PRESETS[preset];
        const aspect = this.camera.aspect;

        const pose = frameSphere(sphere, FIELD_OF_VIEW, aspect, azimuth, elevation);
        const distance = Math.hypot(
            pose.position.x - sphere.center.x,
            pose.position.y - sphere.center.y,
            pose.position.z - sphere.center.z,
        );

        this.fitDistance = distance;
        this.applyClipPlanes(this.boundingSphere(true), distance);

        this.camera.position.set(pose.position.x, pose.position.y, pose.position.z);
        this.controls.target.set(pose.target.x, pose.target.y, pose.target.z);
        this.controls.update();

        // The grid is a sense of scale, so it has to grow with the subject: a fixed 100-unit grid is
        // invisible under a capital ship and swallows a fighter.
        this.rebuildGrid(sphere);
    }

    private rebuildGrid(sphere: BoundingSphere): void {
        const size = Math.max(sphere.radius * 4, 1);
        this.grid.scale.setScalar(size / 100);
        this.axes.scale.setScalar(Math.max(sphere.radius / 10, 0.1));

        // Wider than the grid, so the floor runs past the edge of the measured area rather than
        // ending in a visible square the model appears to be standing on.
        this.floor.scale.setScalar((size * 1.5) / 100);
        this.shadowCatcher.scale.copy(this.floor.scale);


        // The shadow frustum is sized off the model, so it has to be rebuilt with everything else
        // that scales - a frustum left at the last model's size either misses this one entirely or
        // spends its whole depth map on empty space around it.
        this.placeKeyLight();
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
        const particleReach = !includeEffects ? 0 : Math.max(
            0, ...[...this.particleSystems.values()].map(entry => entry.instance.extent()));

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
            mesh => this.drawn(mesh) && !isShadowVolume(mesh));
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

    setGridVisible(visible: boolean): void {
        this.grid.visible = visible;
        this.axes.visible = visible;
    }

    /** Shows or hides the ground plane. Independent of the grid: either is useful without it. */
    setFloorVisible(visible: boolean): void {
        this.floor.visible = visible;
        this.shadowCatcher.visible = visible;
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
        this.floor.position.y = level;
        this.grid.position.y = level;
        this.axes.position.y = level;
        this.shadowCatcher.position.y = level;
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
        this.wireframe = on;
        this.applyWireframe();
    }

    /**
     * Adopts this part's authored shadow meshes as stencil volumes.
     *
     * The source meshes stay exactly as they were - switched off, drawn only when the reader ticks
     * one on to look at it. What this adds is a pair of counting meshes per volume, sharing the
     * geometry.
     */
    private collectShadowVolumes(root: THREE.Object3D): void {
        const found: THREE.Mesh[] = [];

        root.traverse(node => {
            if (!(node instanceof THREE.Mesh) || node.userData.aetShadowVolume === true) {
                return;
            }

            const extras = (node.userData.alamo ?? {}) as MaterialExtras;

            if (resolveMaterial(extras).archetype === 'shadow-volume') {
                found.push(node);
            }
        });

        for (const mesh of found) {
            this.shadowVolumes.add(mesh);
        }

        if (found.length === 0) {
            return;
        }

        this.shadowVolumes.setEnabled(this.shadowVolumesEnabled);
        this.placeKeyLight();

        // Which ALT's volume is the live one, before anything is counted.
        for (const mesh of found) {
            const extras = (mesh.userData.alamo ?? {}) as MaterialExtras;
            this.shadowVolumes.setCounting(mesh, isVisibleAt(extras, this.alt, this.lod));
        }

        // The model can only be known to have a volume AFTER one is found, and the materials were
        // built before that - so the casting decision has to be taken again over everything.
        this.refreshShadowCasting();
    }

    /** Re-decides which meshes cast into the shadow map, now that the volumes are known. */
    private refreshShadowCasting(): void {
        this.forEachDrawnMesh(mesh => {
            if (mesh.userData.aetShadowVolume === true) {
                return;
            }

            const extras = (mesh.userData.alamo ?? {}) as MaterialExtras;
            const spec = resolveMaterial(extras);

            mesh.castShadow = castsShadowMap({
                hasVolumes: this.stencilShadowing, hidden: spec.hidden, blend: spec.blend,
            });
        });
    }

    /**
     * Whether the stencil volumes are the thing casting this model's shadow right now.
     *
     * Both halves matter. Weighing only "does the model author a volume" left a model with volumes
     * casting NOTHING in the shading mode that does not draw volumes - it had already handed its
     * shadow over to a pass that was switched off.
     */
    private get stencilShadowing(): boolean {
        return this.shadowVolumesEnabled && this.shadowVolumes.active;
    }

    /** Whether the engine's stencil shadows are drawn at all. */
    /** Draws the extruded volumes themselves, for diagnosing what the stencil is counting. */
    setShadowVolumeDebug(on: boolean): void {
        this.shadowVolumes.setDebug(on);
    }

    setShadowVolumes(on: boolean): void {
        this.shadowVolumesEnabled = on;
        this.shadowVolumes.setEnabled(on);
        this.refreshShadowCasting();
    }

    /**
     * Puts the current wireframe state onto whatever materials exist now.
     *
     * Called again after a part loads and after a renderer switch, because both replace materials
     * wholesale - and a flag set on the materials that came before is simply gone.
     */
    private applyWireframe(): void {
        this.forEachDrawnMesh(mesh => {
            for (const material of Array.isArray(mesh.material)
                ? mesh.material
                : [mesh.material]) {
                if (material instanceof THREE.Material && 'wireframe' in material) {
                    (material as THREE.Material & { wireframe: boolean }).wireframe =
                        this.wireframe;
                }
            }
        });
    }

    /**
     * Tints the shadow the ground catches.
     *
     * Scoped, and the control says so. Alamo tints a stencil shadow VOLUME, which covers the hull's
     * own self-shadowing too; three's shadow mapping has no colour to set at all. A dedicated
     * catcher plane over the floor is the one place the tint can be expressed, so that is what this
     * colours - and with the ground switched off there is nothing to catch it.
     */
    setShadowColour(colour: THREE.ColorRepresentation): void {
        this.shadowMaterial.color.set(colour);

        // The same colour drives the stencil darken, which is where it reaches the MODEL as well as
        // the ground - the engine's one global shadow colour.
        this.shadowVolumes.setColour(colour);
    }

    // ── animation ─────────────────────────────────────────────────────────────

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

        if (subject === undefined || subject.mixer === null || subject.action !== null) {
            return;
        }

        const name = deathClip(subject.clips.map(c => c.name));
        const clip = subject.clips.find(c => c.name === name);

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
            if (subjectId === ACTIVE_SUBJECT || subject.action === null || subject.mixer === null) {
                continue;
            }

            subject.mixer.update(dt);

            const action = subject.action;
            const track = subject.visibility.get(action.getClip().name);

            if (track !== undefined) {
                // Every bone the track names, proxies included: a proxy carries only the marker
                // mesh the file already hides, and no effect hangs under it to be taken with it.
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
            this.applyEmitterGating(subjectId, track, action);

            // Gone at the end of it. A death clone is what dying LOOKS like, not what is left
            // afterwards - the engine takes the object away when the clip is done.
            if (action.time >= action.getClip().duration) {
                // Its effects are released FIRST, and they outlive it. The Calamari clone's three
                // `p_explosion_huge00` ignite at 59.7s of a 60.1s clip - the detonation the whole
                // minute builds to - and hiding the part they hang under cut them 0.4s in, so the
                // end of a death was 719 particles alive and NONE of them on screen.
                this.releaseSubjectEffects(subjectId);

                for (const part of this.partsOf(subjectId)) {
                    this.setPartHidden(part.id, true);
                }
            }
        }
    }


    get animationNames(): string[] {
        return this.active.clips.map(c => c.name);
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
    play(name: string | null): void {
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
            return;
        }

        const clip = this.active.clips.find(c => c.name === name);
        if (clip === undefined) {
            return;
        }

        this.active.action = this.active.mixer.clipAction(clip);
        this.active.action.setLoop(this.animationLoop ? THREE.LoopRepeat : THREE.LoopOnce, Infinity);
        // Without this a non-looping action snaps back to frame 0 the instant it ends, so the last
        // pose - often the whole point of a death or a deploy - is never on screen.
        this.active.action.clampWhenFinished = true;
        this.active.action.timeScale = this.animationSpeed;
        this.active.action.paused = this.animationPaused;
        this.active.action.play();
    }

    setPaused(paused: boolean): void {
        this.animationPaused = paused;

        if (this.active.action !== null) {
            this.active.action.paused = paused;
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
        const track = active.action === null
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

        this.applyEmitterGating(ACTIVE_SUBJECT, track, active.action);
    }

    /**
     * Holds the emitters whose proxy bone ONE SUBJECT's animation has switched off.
     *
     * Hiding the bone NODE is not enough for a particle system: the system's mesh is parented to the
     * bone, so it would stop being drawn, but it would go on emitting the whole time and burst into
     * view fully populated the moment the bone came back. 2285 of the shipped visibility tracks are
     * particle proxies, so this is the case the whole feature exists for - the rancor's death
     * explosion must not already be a fireball when frame 26 arrives.
     *
     * SCOPED to one subject, which it was not. It swept every system in the scene and gated them all
     * on the ACTIVE subject's clip - the last scene-wide map driven by the active subject that the
     * multi-subject work had not reached. With no active clip running, `bits` is undefined for
     * everything and this set `setEmitting(true)` on every system in the scene, every frame, which
     * also overrode any answer a passive subject had reached for its own.
     *
     * What that cost: a death clone's `_die` clip is a CHOREOGRAPHY - the Mon Calamari Cruiser's
     * times four debris bursts to 0.3-2.0s, ten big explosions across the minute, and three
     * `p_explosion_huge00` to 59.7s as the hull's bones go dark at 59.9s. All 39 of its systems ran
     * continuously from the moment it spawned instead, and the clip only alternated whether you
     * could see them.
     */
    private applyEmitterGating(
        subjectId: string,
        track: VisibilityTrack | undefined,
        action: THREE.AnimationAction | null,
    ): void {
        for (const entry of this.particleSystems.values()) {
            if (entry.attachBone === undefined || entry.attachBoneIndex === undefined) {
                continue;
            }

            if (this.subjectOfSystem(entry) !== subjectId) {
                continue;
            }

            const bits = track?.bones.get(`${entry.attachBone}#${entry.attachBoneIndex}`);

            // A clip that says nothing about this proxy lifts only the CLIP's hold. It is not a
            // permission to spawn: the part this system hangs off may be hidden, and this runs
            // every frame, so voting yes here re-armed a held emitter one frame after it was held.
            entry.instance.hold('clip', bits !== undefined && action !== null
                && hiddenAt(bits, track!.fps, action.time));
        }
    }

    /**
     * Hands one subject's effects over to finish on their own.
     *
     * Each goes through {@link removeParticleSystem}, so the file's `leaveParticles` still decides
     * whether it drains or is cut - an engine glow goes out with the ship, an explosion does not.
     */
    private releaseSubjectEffects(subjectId: string): void {
        for (const [id, entry] of [...this.particleSystems]) {
            if (this.subjectOfSystem(entry) === subjectId) {
                this.removeParticleSystem(id);
            }
        }
    }

    /**
     * The subject a particle system belongs to: the subject of the part it hangs off.
     *
     * A system with no part named is the scene's own - a particle file opened directly - and that is
     * the active subject by definition.
     */
    private subjectOfSystem(entry: ParticleEntry): string {
        return entry.attachToPartId === undefined
            ? ACTIVE_SUBJECT
            : this.parts.get(entry.attachToPartId)?.subjectId ?? ACTIVE_SUBJECT;
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
        for (const entry of this.particleSystems.values()) {
            entry.instance.hold('clip', false);
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
            this.active.action.setLoop(loop ? THREE.LoopRepeat : THREE.LoopOnce, Infinity);
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

        this.active.action.time = Math.max(0, Math.min(seconds, this.active.action.getClip().duration));
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
            running: this.active.action.isRunning() && !this.active.action.paused,
        };
    }

    // ── frame loop ────────────────────────────────────────────────────────────

    stats(): ViewportStats {
        let meshes = 0;
        let triangles = 0;

        this.modelRoot.traverse(node => {
            if (node instanceof THREE.Mesh && this.drawn(node)) {
                meshes++;
                const index = node.geometry.getIndex();
                triangles += (index?.count ?? node.geometry.getAttribute('position')?.count ?? 0) / 3;
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
     * Whether a mesh actually reaches the screen: its own flag AND every ancestor's.
     *
     * A mesh with no ALT or LOD tag is always drawn unless it is hidden itself or hangs off a hidden
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

        for (let at: THREE.Object3D | null = mesh; at !== null; at = at.parent) {
            if (!at.visible) {
                return false;
            }

            if (at === this.modelRoot) {
                break;
            }
        }

        return true;
    }

    /**
     * Where a bone sits in world space, or null when it is not loaded.
     *
     * Thin on purpose: the blast arithmetic is pure and lives in `blast.ts`, and all it needs from
     * a scene graph is a point per mount. Anything more here would be geometry the tests cannot
     * reach.
     */
    bonePosition(partId?: string, bone?: string): { x: number; y: number; z: number } | null {
        if (partId === undefined) {
            return vectorOf(this.modelRoot.getWorldPosition(new THREE.Vector3()));
        }

        // Recorded even though this refuses to place anything: the request was made, and it is the
        // REQUEST that gets reported. The early return below used to happen before `attachmentFor`
        // was ever reached, so a mount that had not loaded left no trace anywhere.
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
        // mount at the same spot, which is a blast radius that catches all of them.
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
        const all: MaterialExtras[] = [];

        this.modelRoot.traverse(node => {
            if (node instanceof THREE.Mesh && node.userData.alamo !== undefined) {
                all.push(node.userData.alamo as MaterialExtras);
            }
        });

        return all;
    }

    // ── particles ──────────────────────────────────────────────────────

    /**
     * Starts a particle system, optionally hung off a bone of a loaded part.
     *
     * Parented rather than positioned: a damaged hardpoint's smoke has to follow the hull as it
     * animates, and parenting gets that for nothing.
     */
    addParticleSystem(
        id: string,
        system: AlamoParticleContent,
        attachToPartId?: string,
        attachBone?: string,
        levels: LevelTagged = { alt: null, lod: null, altDecreaseStayHidden: false },
        attachBoneIndex?: number,
        /**
         * The owning object's `Scale_Factor`, a uniform render scale on the whole system.
         *
         * Only a `<Particle>` GAME OBJECT can carry one; a model's proxy names the asset directly
         * and gets 1. Six shipped objects declare one - 20.0 on the four hero powerup effects, 2.0
         * on the two bombing-run explosions - and nothing read it, so all six drew far too small.
         */
        scaleFactor = 1,
    ): void {
        // Immediate: the id is about to name something else, and a fading twin under it would be
        // orphaned in the scene for good.
        this.removeParticleSystem(id, true);

        const entry: ParticleEntry = {
            id,
            system,
            attachToPartId,
            attachBone,
            attachBoneIndex,
            gateVisible: true,
            levels,
            levelVisible: proxyVisibleAt(levels, this.alt, this.lod, this.altDescending),
            scaleFactor,
            instance: this.instantiate(
                id, system, attachToPartId, attachBone, attachBoneIndex, scaleFactor),
        };

        // Held from birth when it hangs off something already off screen: a death clone's effects
        // attach while the clone is still hidden, and a one-shot that runs there is spent before
        // anyone sees it.
        if (attachToPartId !== undefined && this.hiddenParts.has(attachToPartId)) {
            entry.instance.hold('part', true);
        }

        this.particleSystems.set(id, entry);
        this.refreshParticleVisibility(entry);

        // The depth range again, because the effects reach further than the model and they arrive
        // AFTER the camera was fitted to it. Without this, Boba Fett's flamethrower - 206 units of
        // throw on a seven-unit model - was culled at 156 and looked like a flame that stops.
        this.applyClipPlanes(this.boundingSphere(), this.fitDistance);
    }

    private instantiate(
        id: string, system: AlamoParticleContent, attachToPartId?: string, attachBone?: string,
        attachBoneIndex?: number, scaleFactor = 1,
    ): ParticleSystemInstance {
        const attachment = this.attachmentFor(attachToPartId, attachBone, attachBoneIndex);

        // Read once, here, rather than per spawn: the walk up the graph and the transform into the
        // attachment's space are the same answer every frame, and this runs thousands of times a
        // second on a `EveryVertex` emitter. `restartParticles` re-reads it, which is what picks up
        // a level switch that swapped the geometry underneath.
        const source = emissionMeshFor(attachment, this.partRootOf(attachToPartId));

        // The model root is the space the particles are simulated in, so an effect the file does
        // not link to its emitter is genuinely left behind rather than dragged along by the bone
        // it is anchored on.
        const instance = new ParticleSystemInstance(
            id, system, this.particleTextures, 1, source, this.modelRoot, scaleFactor);
        instance.attachTo(attachment);
        instance.setVisible(this.particlesVisible);

        // A system built after the answers came back still has to show the marker: a hardpoint
        // blowing up spawns one long after the textures were asked for.
        for (const name of this.missingTextures) {
            void this.markMissing(name);
        }

        for (const key of this.hiddenEmitters) {
            const [systemId, index] = splitEmitterKey(key);
            if (systemId === id) {
                instance.setEmitterVisible(index, false);
            }
        }

        // Hung off the SIMULATION SPACE rather than off the bone it is anchored on. A proxy bone
        // is a child of the piece it belongs to - `p_explosion_big00#12` sits under `Busted_00#11`
        // on the Nebulon-B - and the death clip hides that piece on the very frame the blast
        // fires. three prunes a hidden subtree, so parenting the effect there cut it one frame in.
        // Anchoring is what makes it follow the bone; see `ParticleSystemInstance.attachTo`.
        //
        // Flagged all the same, so the model-mesh walk prunes it wherever it is reached from.
        instance.root.userData.aetParticleSystem = true;
        this.modelRoot.add(instance.root);
        return instance;
    }

    /** Where the climb for an emission mesh has to stop: the model the system was attached to. */
    private partRootOf(partId?: string): THREE.Object3D | null {
        return partId === undefined ? this.modelRoot : this.parts.get(partId)?.root ?? null;
    }

    /**
     * Takes a system out of the scene.
     *
     * What it does with the particles ALREADY IN THE AIR is the file's word: see
     * {@link fadingParticles}. `immediate` overrides it, and only two things may - replacing a
     * system under the same id, and a wholesale reset of the scene or the renderer.
     */
    removeParticleSystem(id: string, immediate = false): void {
        const entry = this.particleSystems.get(id);

        if (entry !== undefined) {
            if (!immediate && entry.system.leaveParticles && !entry.instance.empty) {
                // Stop spawning and let the air clear. Nothing sweeps it after this, which is why
                // it leaves `particleSystems` - a gate re-enabling it would refill it forever.
                entry.instance.hold('released', true);

                // And cut loose from the bone it was anchored on, which is usually about to be
                // hidden or disposed. Nothing moves: the simulation already runs in the model's
                // space with the bone cancelled out, so the particles stay exactly where they are -
                // but a bone still being read after its part is gone would report its last stale
                // matrix as this step's movement and fling every linked particle with it.
                entry.instance.detach();

                this.fadingParticles.push(entry.instance);
            } else {
                entry.instance.dispose();
            }
        }

        this.particleSystems.delete(id);

        // And back in again once the effect is gone, so a one-off explosion does not leave the
        // depth range stretched for the rest of the session.
        this.applyClipPlanes(this.boundingSphere(), this.fitDistance);
    }

    /** Drops every fading system at once, for a reset that takes the whole scene with it. */
    private clearFadingParticles(): void {
        for (const instance of this.fadingParticles) {
            instance.dispose();
        }

        this.fadingParticles = [];
    }

    /** Whether a part's geometry has arrived, so something can be hung off its bones. */
    hasPart(partId: string): boolean {
        return this.parts.has(partId);
    }

    /** Names of the running systems, for the emitter list in the dock. */
    particleSystemIds(): string[] {
        return [...this.particleSystems.keys()].sort();
    }

    /** Every texture the running systems want but may not have yet. */
    particleTextureNames(): string[] {
        return [...new Set(
            [...this.particleSystems.values()].flatMap(e => e.instance.textureNames()))].sort();
    }

    /** Hands a fetched texture to whichever emitters named it, now and after a restart. */
    setParticleTexture(name: string, texture: THREE.Texture): void {
        this.particleTextures.set(name.toLowerCase(), texture);
        this.missingTextures.delete(name.toLowerCase());

        for (const entry of this.particleSystems.values()) {
            entry.instance.setTexture(name, texture);
        }
    }

    /**
     * Says a texture will never arrive, so every emitter naming it shows the marker.
     *
     * Remembered, not just applied: a system can be built after the answer came back - a hardpoint
     * blowing up spawns one - and it has to show the marker too rather than sitting on a blank quad
     * because it happened to be late.
     */
    setParticleTextureMissing(name: string): void {
        this.missingTextures.add(name.toLowerCase());

        void this.markMissing(name);
    }

    private async markMissing(name: string): Promise<void> {
        const marker = await missingTexture();

        // Still missing? A real texture can land while the marker is being decoded, and the answer
        // that arrived last is the one that is true.
        if (!this.missingTextures.has(name.toLowerCase())) {
            return;
        }

        for (const entry of this.particleSystems.values()) {
            entry.instance.setMissingTexture(name, marker);
        }
    }

    /** Textures the server has told us do not resolve. */
    private readonly missingTextures = new Set<string>();

    setParticlesVisible(visible: boolean): void {
        this.particlesVisible = visible;

        // A system switched off individually, or by its level, stays off when the global toggle
        // comes back on.
        for (const entry of this.particleSystems.values()) {
            this.refreshParticleVisibility(entry);
        }
    }

    /**
     * What the game's rules say about a system: the opening rule and the current damage state.
     *
     * The RULES path, called on attach and again whenever a hardpoint is destroyed or repaired -
     * never in response to a click. A hand-set override survives this, so switching one effect on
     * does not get undone by an unrelated mount blowing up.
     *
     * What the rules say is `inFile` for this row - the model's own word - and the chain weighs it
     * against everything else. It does NOT clear a hand-set override: the reader's word is taken
     * back only by the reader or by a clip starting, and a row standing against the rules is marked
     * as one the model does not draw, so nothing here is claimed that is not true.
     */
    setParticleSystemVisible(id: string, visible: boolean): void {
        const entry = this.particleSystems.get(id);
        if (entry === undefined) {
            return;
        }

        entry.gateVisible = visible;
        this.refreshRows();
    }

    /** The emitters of one system, in file order. */
    emitterNames(systemId: string): string[] {
        return this.particleSystems.get(systemId)?.instance.emitterNames() ?? [];
    }

    /** Shows or hides one emitter of one system, by its position in that system's file. */
    setEmitterVisible(systemId: string, index: number, visible: boolean): void {
        const key = `${systemId}#${index}`;

        if (visible) {
            this.hiddenEmitters.delete(key);
        } else {
            this.hiddenEmitters.add(key);
        }

        this.particleSystems.get(systemId)?.instance.setEmitterVisible(index, visible);
    }

    setParticlesPaused(paused: boolean): void {
        this.particlesPaused = paused;
    }

    /** Multiplier on particle time. 1 is real time. */
    setParticleSpeed(speed: number): void {
        this.particleSpeed = Math.max(0, speed);
    }

    /**
     * Replays every system from nothing.
     *
     * Rebuilt rather than rewound: a burst emitter's whole point is the first half second, and there
     * is no way back to it from the middle of a run short of discarding the state.
     */
    restartParticles(): void {
        // The other override: every system is being rebuilt from scratch, and a fading one has no
        // rebuilt twin to belong to.
        this.clearFadingParticles();

        for (const [id, entry] of this.particleSystems) {
            entry.instance.dispose();
            entry.instance =
                this.instantiate(id, entry.system, entry.attachToPartId, entry.attachBone,
                    entry.attachBoneIndex, entry.scaleFactor);
            this.refreshParticleVisibility(entry);
        }
    }

    // ── skeleton ───────────────────────────────────────────────────────

    /**
     * The hull's bones, flattened.
     *
     * Read off the loaded scene rather than tracked separately, so the tree can never describe a
     * skeleton that is not the one on screen. Only the FIRST part's bones: a mounted turret carries
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
                name: alamoBoneName(node.name)?.name ?? node.name,
                parent: boneAncestor(node),
                visible: node.visible,
            }))
            .sort((a, b) => a.index - b.index);
    }

    /** What hangs off each bone of the hull, keyed by bone index. */
    attachmentsByBone(): Map<number, BoneAttachment[]> {
        const attachments = new Map<number, BoneAttachment[]>();
        const hull = this.hull();
        if (hull === null) {
            return attachments;
        }

        // Which bone each object belongs to, so a mesh can be placed even when it is not a direct
        // child of one. A SKINNED mesh hangs off the skeleton root rather than any single bone -
        // it spans many - and listing only direct children left most of a skinned model with no row
        // at all, which is no use for a control whose whole point is reaching every mesh.
        const boneOf = new Map<THREE.Object3D, number>();
        for (const [index, node] of hull.bonesByIndex) {
            boneOf.set(node, index);
        }

        const rootBone = [...hull.bonesByIndex.keys()].sort((a, b) => a - b)[0] ?? 0;
        const meshesByBone = new Map<number, THREE.Mesh[]>();

        hull.root.traverse(object => {
            if (!(object instanceof THREE.Mesh) || object.name === '') {
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

            meshesByBone.set(owner, [...(meshesByBone.get(owner) ?? []), object]);
        });

        for (const [index, node] of hull.bonesByIndex) {
            const found: BoneAttachment[] = [];

            for (const mesh of meshesByBone.get(index) ?? []) {
                found.push({ kind: 'mesh', label: mesh.name });
            }

            // A mounted part is a separate root parented onto the bone, so it reads as a hardpoint.
            for (const part of this.parts.values()) {
                if (part.root.parent === node) {
                    found.push({ kind: 'hardpoint', label: part.id });
                }
            }

            if (found.length > 0) {
                attachments.set(index, found);
            }
        }

        return attachments;
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
        return [...this.parts.values()].filter(part => part.subjectId === subjectId);
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
    private applyOwnFileVisibility(subjectId: string, root: THREE.Object3D): void {
        const subject = this.subjectAnimation(subjectId);
        const action = subject.action;
        const track = action === null
            ? undefined
            : subject.visibility.get(action.getClip().name);

        root.traverse(node => {
            if (!(node instanceof THREE.Mesh) || node.userData.aetShadowVolume === true) {
                return;
            }

            if (track?.bones.has(node.name) === true) {
                return;
            }

            const extras = (node.userData.alamo ?? {}) as MaterialExtras;

            setMeshDrawn(node, !resolveMaterial(extras).hidden
                && isVisibleAt(extras, this.alt, this.lod));
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
     * The subject the panel speaks for.
     *
     * Every animation control - the picker, the speed, the scrub bar, the loop switch - describes
     * the model that was opened, and only that one. A passive subject's clip is not something the
     * reader chose; it is what dying looks like, and it runs on its own terms.
     */
    private get active(): SubjectAnimation {
        return this.subjectAnimation(ACTIVE_SUBJECT);
    }

    setSkeletonVisible(visible: boolean): void {
        this.skeletonVisible = visible;
        this.joints.visible = visible;
        this.jointLines.visible = visible;

        if (!visible) {
            this.clearLabels();
        }
    }

    setLabelMode(mode: LabelMode): void {
        this.labelMode = mode;
        // Immediately rather than on the next throttled tick: a mode change the user just made must
        // not appear to have been ignored.
        this.lastLabelUpdate = 0;
    }

    /**
     * Which bones a weapon fires from, so the triad can mark the aim axis.
     *
     * By NAME, because that is what a weapon declares. The axis itself is local X and that is
     * measured rather than assumed - see `AlamoFireBone.AimDirection`.
     */
    setFireBones(names: ReadonlySet<string>): void {
        this.fireBones = names;
        this.updateBoneAxes();
    }

    setSelectedBone(index: number | null): void {
        this.selectedBone = index;
        this.lastLabelUpdate = 0;
        this.updateBoneAxes();
    }

    /**
     * Draws the selected bone's own axes at its own orientation.
     *
     * Scaled to the SUBJECT, like everything else that has to be legible from a two-unit trooper to
     * a thousand-unit command centre. Rebuilt per selection rather than per frame: a bone only moves
     * under an animation, and the box beside it is already rebuilt on that path.
     */
    private updateBoneAxes(): void {
        const hull = this.hull();
        const node = this.selectedBone === null
            ? undefined
            : hull?.bonesByIndex.get(this.selectedBone);

        if (node === undefined) {
            this.boneAxes.visible = false;
            return;
        }

        const origin = node.getWorldPosition(new THREE.Vector3());
        const basis = new THREE.Matrix4().extractRotation(node.matrixWorld);

        // The SUBJECT's size, cached. This runs per frame while a clip plays, and
        // `Box3.setFromObject` walks every vertex of the hull - recomputing it there was a full
        // geometry traversal at sixty frames a second.
        const length = Math.max(1, this.modelSpan() * 0.06);

        const positions: number[] = [];
        const colours: number[] = [];

        // X red, Y green, Z blue. X first and drawn no differently: it is the one that matters most
        // here - an Alamo fire bone aims along its local X - but colouring it specially would make
        // the triad mean something other than what every other tool means by it.
        const axes: [THREE.Vector3, [number, number, number]][] = [
            [new THREE.Vector3(1, 0, 0), [1, 0.25, 0.25]],
            [new THREE.Vector3(0, 1, 0), [0.25, 1, 0.25]],
            [new THREE.Vector3(0, 0, 1), [0.35, 0.5, 1]],
        ];

        // Whether THIS bone is somewhere a weapon fires from. A fire bone aims along its local X -
        // measured, see `AlamoFireBone.AimDirection` - so that axis gets an arrowhead and a longer
        // reach. Nothing else on the triad says which way the gun points, which was the complaint.
        const firing = this.fireBones.has(
            (alamoBoneName(node.name)?.name ?? node.name).toLowerCase());

        for (const [direction, colour] of axes) {
            const aim = firing && direction.x === 1;
            const reach = aim ? length * 2.2 : length;
            const tip = direction.clone().applyMatrix4(basis).multiplyScalar(reach).add(origin);

            positions.push(origin.x, origin.y, origin.z, tip.x, tip.y, tip.z);
            colours.push(...colour, ...colour);

            if (!aim) {
                continue;
            }

            // A four-barb head, drawn back down the shaft from the tip. Lines rather than a cone so
            // it stays one draw with the rest of the triad and cannot be occluded differently.
            const back = direction.clone().applyMatrix4(basis).multiplyScalar(-reach * 0.22);
            const spread = reach * 0.09;

            for (const side of [new THREE.Vector3(0, 1, 0), new THREE.Vector3(0, 0, 1)]) {
                const out = side.clone().applyMatrix4(basis).multiplyScalar(spread);

                for (const sign of [1, -1]) {
                    const barb = tip.clone().add(back).addScaledVector(out, sign);

                    positions.push(tip.x, tip.y, tip.z, barb.x, barb.y, barb.z);
                    colours.push(...colour, ...colour);
                }
            }
        }

        setPositions(this.boneAxes.geometry, positions);
        this.boneAxes.geometry.setAttribute(
            'color', new THREE.Float32BufferAttribute(colours, 3));
        this.boneAxes.visible = true;
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
        this.selectedRows = [...ids];
        this.updateSelectionBoxes();
    }

    /**
     * Rebuilds the boxes from where the geometry currently is.
     *
     * Per frame while something is selected, because an animation moves the meshes underneath the
     * box and a box left where the mesh used to be is worse than none at all.
     */
    private updateSelectionBoxes(): void {
        const points: number[] = [];

        for (const root of boxRoots(this.selectedRows, this.rowParents)) {
            const box = this.boxAround(root);

            if (box !== null) {
                appendBoxEdges(points, box);
            }
        }

        setPositions(this.selectionBoxes.geometry, points);
        this.selectionBoxes.visible = points.length > 0;
    }

    /** What one box encloses, in world space, or null when the row owns nothing locatable. */
    private boxAround(root: string): THREE.Box3 | null {
        const box = new THREE.Box3();
        const hull = this.hull();
        let found = false;

        for (const row of rowsInBox(root, this.rowParents)) {
            const target = this.rowTargets.get(row);

            if (target?.mesh !== undefined) {
                // `setFromObject` walks the world matrices, which is what puts the box where an
                // animated mesh is NOW rather than where it was authored.
                box.union(new THREE.Box3().setFromObject(target.mesh));
                found = true;
            }

            const node = target?.boneIndex === undefined
                ? undefined
                : hull?.bonesByIndex.get(target.boneIndex);

            if (node !== undefined) {
                box.expandByPoint(node.getWorldPosition(new THREE.Vector3()));
                found = true;
            }
        }

        if (!found || box.isEmpty()) {
            return null;
        }

        // A bone with no geometry under it collapses to a point, and a box with no volume draws
        // nothing at all. Sized against the SUBJECT so the marker reads the same on a trooper and
        // on a command centre.
        const size = box.getSize(new THREE.Vector3());

        if (Math.max(size.x, size.y, size.z) < 1e-4) {
            box.expandByScalar(Math.max(this.boundingSphere().radius * 0.02, 1e-3));
        }

        return box;
    }

    /** Rebuilds the joint and line buffers from where the bones currently are. */
    private updateSkeletonGeometry(): void {
        const hull = this.hull();
        if (hull === null) {
            return;
        }

        const indexOf = new Map<THREE.Object3D, number>();
        for (const [index, node] of hull.bonesByIndex) {
            indexOf.set(node, index);
        }

        const points: number[] = [];
        const lines: number[] = [];
        const colours: number[] = [];
        const world = new THREE.Vector3();
        const parentWorld = new THREE.Vector3();

        for (const [index, node] of hull.bonesByIndex) {
            node.getWorldPosition(world);
            points.push(world.x, world.y, world.z);

            const selected = index === this.selectedBone;
            colours.push(selected ? 1 : 0.44, selected ? 0.62 : 0.7, selected ? 0.16 : 1);

            if (node.parent !== null && indexOf.has(node.parent)) {
                node.parent.getWorldPosition(parentWorld);
                lines.push(parentWorld.x, parentWorld.y, parentWorld.z, world.x, world.y, world.z);
            }
        }

        setPositions(this.joints.geometry, points);
        this.joints.geometry.setAttribute('color', new THREE.Float32BufferAttribute(colours, 3));
        setPositions(this.jointLines.geometry, lines);
    }

    /**
     * Places the bone labels over the canvas.
     *
     * Nearest-first with a minimum pixel gap. A Star Destroyer has 71 bones and many project within a
     * few pixels of each other, so drawing every label turns the viewport into unreadable soup;
     * dropping the further of an overlapping pair keeps what is closest to the camera, which is what
     * the user is looking at.
     */
    private updateLabels(): void {
        const hull = this.hull();
        if (hull === null || !this.skeletonVisible) {
            this.clearLabels();
            return;
        }

        const wanted = labelCandidates(this.skeleton(), this.labelMode, this.selectedBone);
        if (wanted.size === 0) {
            this.clearLabels();
            return;
        }

        const width = this.canvas.clientWidth;
        const height = this.canvas.clientHeight;
        const world = new THREE.Vector3();

        const projected: {
            x: number; y: number; depth: number; text: string; selected: boolean;
        }[] = [];

        for (const [index, node] of hull.bonesByIndex) {
            if (!wanted.has(index)) {
                continue;
            }

            node.getWorldPosition(world);
            const ndc = world.clone().project(this.camera);

            // Behind the camera or off screen: projecting those puts labels on the wrong edge.
            if (ndc.z > 1 || ndc.x < -1 || ndc.x > 1 || ndc.y < -1 || ndc.y > 1) {
                continue;
            }

            projected.push({
                x: (ndc.x * 0.5 + 0.5) * width,
                y: (-ndc.y * 0.5 + 0.5) * height,
                depth: ndc.z,
                text: alamoBoneName(node.name)?.name ?? node.name,
                selected: index === this.selectedBone,
            });
        }

        projected.sort((a, b) => a.depth - b.depth);

        const placed: { x: number; y: number }[] = [];
        let used = 0;

        for (const label of projected) {
            // The selection always gets its label, whatever it overlaps: it is the one the user asked
            // about, and silently dropping it looks like the click did nothing.
            const crowded = !label.selected && placed.some(
                p => Math.abs(p.x - label.x) < LABEL_SPACING
                    && Math.abs(p.y - label.y) < LABEL_SPACING);

            if (crowded) {
                continue;
            }

            placed.push(label);

            const element = this.labelAt(used++);
            element.textContent = label.text;
            element.style.transform =
                'translate(' + Math.round(label.x) + 'px, ' + Math.round(label.y) + 'px)';
            element.className = label.selected ? 'bone-label selected' : 'bone-label';
            element.style.display = '';
        }

        for (let i = used; i < this.labelPool.length; i++) {
            this.labelPool[i].style.display = 'none';
        }
    }

    /**
     * The targeting marks to draw, and how big.
     *
     * Replaced wholesale rather than diffed: the list is one per targetable mount - ten on the Star
     * Destroyer, and never more than a few hundred - and it changes only when the scene, the state
     * or the damage does.
     */
    setReticles(marks: readonly ReticleMark[], screenSize: number | null | undefined): void {
        this.reticles = [...marks];
        // The FRACTION, not a pixel count. Resizing the panel has to change the mark's size, and a
        // number computed in React would be stale until something else happened to re-render.
        this.reticleScreenSize = screenSize;

        if (this.reticles.length === 0) {
            this.clearReticles();
        }
    }

    /**
     * Projects each mark and places its image over the canvas.
     *
     * DOM rather than scene geometry, exactly like the bone labels above. A reticle is HUD: the game
     * sizes it as a fraction of the SCREEN and draws it over the hull rather than in it, so a quad in
     * the scene would have to fight both perspective and the depth buffer to end up here anyway.
     *
     * Anchored on the middle of the attached model, not on the attach bone. The bone sits at the
     * mount's base - a mark there hangs below the turret rather than on it - and what a player puts
     * their cursor over is the thing standing on the bone. A mount with no model of its own has only
     * its bone, and that bone is the hull's.
     */
    private updateReticles(): void {
        const width = this.canvas.clientWidth;
        const height = this.canvas.clientHeight;
        const size = reticleSizePx(this.reticleScreenSize, width, height);
        const world = new THREE.Vector3();
        let used = 0;

        for (const mark of this.reticles) {
            const anchor = this.reticleAnchor(mark, world);
            if (anchor === null) {
                continue;
            }

            const ndc = RETICLE_NDC.copy(anchor).project(this.camera);

            // Behind the camera or off screen. Projecting those puts the mark on the wrong edge, and
            // a reticle in the corner of a ship you are looking past is worse than none.
            if (ndc.z > 1 || ndc.x < -1 || ndc.x > 1 || ndc.y < -1 || ndc.y > 1) {
                continue;
            }

            const element = this.reticleAt(used++);
            const half = size / 2;

            element.style.width = `${size}px`;
            element.style.height = `${size}px`;
            element.style.transform = 'translate('
                + `${Math.round((ndc.x * 0.5 + 0.5) * width - half)}px, `
                + `${Math.round((-ndc.y * 0.5 + 0.5) * height - half)}px)`;

            // MASKED rather than drawn, so the shape can be tinted by health. The game colours a
            // reticle bright green at full health through yellow and orange to red at nothing, and
            // an <img> cannot be recoloured - a mask plus a background can.
            const colour = healthColour(mark.healthFraction);

            // The tracked art while the pointer is on it, which is what the game does - and what
            // replaced a seven-way dropdown asking the reader to pick a state by hand.
            const art = this.hoveredReticle === mark.hardpointId ? mark.trackedUri : mark.iconUri;

            element.dataset.hardpoint = mark.hardpointId;

            if (element.dataset.icon !== art) {
                element.dataset.icon = art;
                element.style.maskImage = `url(${art})`;
                element.style.webkitMaskImage = `url(${art})`;
            }

            if (element.style.backgroundColor !== colour) {
                element.style.backgroundColor = colour;
            }

            element.title = `${mark.hardpointId}${mark.type === '' ? '' : ` - ${mark.type}`}`;
            element.style.display = '';
        }

        for (let i = used; i < this.reticlePool.length; i++) {
            this.reticlePool[i].style.display = 'none';
        }
    }

    /**
     * Where one mark stands, in world space, or null when its bone is not loaded.
     *
     * The attachment BONE, and nothing else. Centring on the attached model's bounds was both wrong
     * - the game draws the mark on the bone - and slow: it asked three.js for a whole part's
     * bounding box per mark per tick, which walks every vertex of the geometry. Ten mounts at twelve
     * ticks a second made the viewport crawl.
     */
    private reticleAnchor(mark: ReticleMark, into: THREE.Vector3): THREE.Vector3 | null {
        if (mark.bone === null) {
            return null;
        }

        // Through `bonePosition`, which answers null for anything it cannot actually find.
        // Comparing against `this.modelRoot` caught only HALF of it: that is the fallback for a
        // missing PART, and a missing BONE falls back to the owning part's root instead - so a
        // mark whose bone was absent was drawn at that model's origin rather than dropped, and
        // every such mark piled up in the same place.
        const at = this.bonePosition(mark.partId, mark.bone);

        return at === null ? null : into.set(at.x, at.y, at.z);
    }

    /**
     * How big the subject is, across its diagonal, cached until the scene changes.
     *
     * `Box3.setFromObject` walks every vertex, so anything asking per frame - the bone axes did -
     * turns a size lookup into a full geometry traversal. The hull does not change size while it is
     * loaded; an animation moves it, and a scale that mattered would arrive as a new scene.
     */
    private modelSpan(): number {
        if (this.cachedSpan !== null) {
            return this.cachedSpan;
        }

        const hull = this.hull();
        if (hull === null) {
            return 100;
        }

        const bounds = new THREE.Box3().setFromObject(hull.root);
        this.cachedSpan = bounds.isEmpty()
            ? 100
            : bounds.getSize(new THREE.Vector3()).length();

        return this.cachedSpan;
    }

    /** Pooled, like the labels: a moving camera must not churn the DOM every tick. */
    private reticleAt(index: number): HTMLElement {
        while (this.reticlePool.length <= index) {
            const element = document.createElement('div');
            element.className = 'reticle-mark';

            // The marks are the one thing in the label layer that IS interactive: hovering shows
            // the tracked art the game would show under a cursor, and clicking picks the mount.
            // The layer itself keeps `pointer-events: none` so nothing else steals the drag that
            // orbits the camera.
            element.style.pointerEvents = 'auto';
            element.style.cursor = 'pointer';

            element.addEventListener('pointerenter', () => {
                const id = element.dataset.hardpoint ?? '';
                this.hoveredReticle = id === '' ? null : id;
            });

            element.addEventListener('pointerleave', () => {
                this.hoveredReticle = null;
            });

            element.addEventListener('click', event => {
                event.stopPropagation();
                const id = element.dataset.hardpoint ?? '';

                if (id !== '') {
                    this.onReticleClicked?.(id);
                }
            });
            this.labelLayer.appendChild(element);
            this.reticlePool.push(element);
        }

        return this.reticlePool[index];
    }

    private clearReticles(): void {
        for (const element of this.reticlePool) {
            element.style.display = 'none';
        }
    }

    /** Pooled, so a moving camera does not churn the DOM on every tick. */
    private labelAt(index: number): HTMLElement {
        while (this.labelPool.length <= index) {
            const element = document.createElement('div');
            element.className = 'bone-label';
            this.labelLayer.appendChild(element);
            this.labelPool.push(element);
        }

        return this.labelPool[index];
    }

    private clearLabels(): void {
        for (const element of this.labelPool) {
            element.style.display = 'none';
        }
    }

    /** Picks the nearest joint to the pointer, so clicking a bone selects it. */
    private onPointerDown = (event: PointerEvent): void => {
        if (!this.skeletonVisible || this.onBoneSelected === null) {
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
                (-ndc.y * 0.5 + 0.5) * rect.height - y);

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
            grid: this.grid.visible,
            floor: this.floor.visible,
            catcher: this.shadowCatcher.visible,
            particles: this.particlesVisible,
            axes: this.axes.visible,
            joints: this.joints.visible,
            jointLines: this.jointLines.visible,
            selection: this.selectionBoxes.visible,
            arcs: this.arcsVisible,
        };

        try {
            // One device pixel per requested pixel: a capture is measured in pixels, not in the
            // reader's display scaling, or the same request would give a different file on a
            // different monitor.
            this.renderer.setPixelRatio(1);
            this.renderer.setSize(options.width, options.height, false);
            this.camera.aspect = options.width / Math.max(options.height, 1);
            this.camera.updateProjectionMatrix();

            // Null is TRANSPARENT, which is what an icon wants: the alpha the renderer was created
            // with is what makes it possible at all.
            this.scene.background = options.background === null
                ? null
                : new THREE.Color(options.background);

            this.grid.visible = options.grid;
            this.floor.visible = options.floor;
            this.shadowCatcher.visible = options.floor && before.catcher;
            this.setParticlesVisible(options.particles);

            // The axes, the skeleton and the firing arcs are things drawn to help someone READ the
            // model. They are not part of it, and an icon with a coloured cross through it is not
            // an icon - which is exactly what the first capture came out as.
            this.axes.visible = options.annotations && before.axes;
            this.joints.visible = options.annotations && before.joints;
            this.jointLines.visible = options.annotations && before.jointLines;

            // A selection box is a thing drawn to help someone READ the model, exactly like the
            // axes and the skeleton, and an icon with an amber cage around it is not an icon.
            this.selectionBoxes.visible = options.annotations && before.selection;

            if (!options.annotations) {
                this.setFireArcsVisible(false);
            }

            this.prepareForCamera();

            // A sprite is oriented against the VIEW matrix - a streak's length is a screen-space
            // measurement - so it has to be re-aimed for this camera. A zero step re-aims without
            // advancing the simulation: a capture must not move the effects on.
            if (this.particlesVisible) {
                for (const entry of this.particleSystems.values()) {
                    entry.instance.update(0, this.camera.matrixWorldInverse, this.windVector);
                }
            }

            this.renderFrame();

            return this.canvas.toDataURL('image/png');
        } finally {
            // Always put the room back, including when the render throws: a failed capture must not
            // leave the reader looking at a 64-pixel viewport with the grid switched off.
            this.renderer.setPixelRatio(before.pixelRatio);
            this.scene.background = before.background;
            this.grid.visible = before.grid;
            this.floor.visible = before.floor;
            this.shadowCatcher.visible = before.catcher;
            this.setParticlesVisible(before.particles);
            this.axes.visible = before.axes;
            this.joints.visible = before.joints;
            this.jointLines.visible = before.jointLines;
            this.selectionBoxes.visible = before.selection;
            this.setFireArcsVisible(before.arcs);
            this.camera.aspect = before.aspect;
            this.camera.updateProjectionMatrix();
            this.resize();
            this.prepareForCamera();
            this.renderFrame();
        }
    }

    resize(): void {
        const width = this.canvas.clientWidth || 1;
        const height = this.canvas.clientHeight || 1;

        this.renderer.setSize(width, height, false);
        this.camera.aspect = width / height;
        this.camera.updateProjectionMatrix();
    }

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
        this.advanceTurretSweep(dt);
        this.advanceBreakoffs(dt);

        this.advancePassiveSubjects(dt);
        this.controls.update();

        if (this.particlesVisible && !this.particlesPaused) {
            for (const [id, entry] of [...this.particleSystems]) {
                // The camera goes in because a streak's length is a screen-space measurement:
                // a spark flying at the viewer has no tail to draw.
                entry.instance.update(
                    dt * this.particleSpeed, this.camera.matrixWorldInverse, this.windVector);

                // A death explosion is over once it stops emitting and its last spark dies.
                if (this.transient.has(id) && entry.instance.exhausted) {
                    this.transient.delete(id);
                    this.removeParticleSystem(id);
                }
            }

            // The ones already killed, finishing. Stepped in the SAME gate as the live systems, so
            // the global particle switch and the pause govern them identically - the reader turning
            // effects off must not leave a wreck's fire burning on.
            for (let at = this.fadingParticles.length - 1; at >= 0; at--) {
                const instance = this.fadingParticles[at];

                instance.update(
                    dt * this.particleSpeed, this.camera.matrixWorldInverse, this.windVector);

                // `empty`, not `exhausted`: it has been told to stop spawning, so "finished" will
                // never come for a continuous emitter. What is left is the only question.
                if (instance.empty) {
                    instance.dispose();
                    this.fadingParticles.splice(at, 1);
                }
            }
        }

        if (this.selectedRows.length > 0) {
            this.updateSelectionBoxes();
        }

        // An animation moves the bone under the triad, so it follows on the same terms as the box.
        if (this.selectedBone !== null && this.active.mixer !== null) {
            this.updateBoneAxes();
        }

        if (this.skeletonVisible) {
            // Rebuilt per frame because animation moves the bones; cheap for the hundreds a model has.
            this.updateSkeletonGeometry();

            const now = performance.now();
            if (now - this.lastLabelUpdate > LABEL_INTERVAL_MS) {
                this.lastLabelUpdate = now;
                this.updateLabels();
            }
        }

        this.prepareForCamera();

        // Outside the skeleton branch: a reticle is a targeting mark, not a bone label, and it has to
        // stay on screen when the skeleton is off - which is how anyone actually looks at a ship.
        //
        // EVERY frame, and AFTER `prepareForCamera`. Both halves of that are the fix for marks that
        // visibly lagged the ship while the camera moved, and neither was a performance problem -
        // the work per mark is two map lookups and a projection. Sharing the labels' 80ms throttle
        // ran them at 12Hz under a 60Hz canvas, so an orbiting camera left them up to five frames
        // behind the hull they are painted on; running before the camera's matrix was updated added
        // one more frame on top, because they were projected through the PREVIOUS frame's view.
        // A bone label a few pixels behind is invisible. A reticle is drawn on the thing it marks,
        // so the same error reads as the mark sliding off the mount.
        if (this.reticles.length > 0) {
            this.updateReticles();
        }

        this.renderFrame();
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
        this.updateAlamoUniforms();
        this.updateBillboards();

        // The sky dome travels with the camera and keeps its own orientation, so the horizon stays
        // level and infinitely far however the camera is turned.
        if (this.sky !== null && this.sky.visible) {
            this.sky.position.copy(this.camera.position);
            this.sky.scale.setScalar(Math.max(this.camera.near * 4, this.camera.far * 0.4));
        }

        // The stars need the same treatment, and for a reason that took a regression to notice:
        // parenting them to the CAMERA exempts them from frustum CULLING but not from the far
        // plane, which clips their vertices like anything else's. The far plane is fitted to the
        // model, so the moment the hardpoint rotation was corrected and the bounds shrank, the
        // whole sky fell outside it and the stars simply went out.
        if (this.stars !== null && this.stars.visible) {
            this.stars.scale.setScalar(
                Math.max(this.camera.near * 4, this.camera.far * 0.4));
        }
    }

    /**
     * The frame's pass chain: scene, then heat, then bloom, then the canvas.
     *
     * Written out in one place on purpose. Heat already owns a composite of its own, so a second
     * pass that also believed it drew the final image would simply erase whichever ran first. Each
     * step here either draws to the canvas because it is last, or hands its result on as a texture.
     *
     * With both effects off this is one `renderer.render` to the canvas - the same call and the
     * same cost as before the chain existed.
     */
    private renderFrame(): void {
        const bending = this.distorting();
        const bloom = this.bloomEnabled ? (this.bloom ??= new BloomPass()) : null;

        // Everything draws to the CANVAS and the next step copies it, which is the structure the
        // reference renderer uses for both effects - `StretchRect(pBackBuffer -> pBloomSurface)` in
        // AloViewer's `Render.cpp`, and the same for heat. Nothing is presented until the frame
        // ends, so the canvas holding an intermediate picture for a moment is invisible.
        //
        // This used to render the scene into an sRGB render target for bloom to read. That target
        // gets LINEAR values - three only applies its output encoding when it draws to the canvas -
        // so its multisampling resolved in linear light while the canvas resolves encoded, and
        // switching bloom on lifted every antialiased edge in the picture. The engine has no colour
        // management at all: every surface it composites through is a plain `D3DFMT_A8R8G8B8`.
        if (bending) {
            this.heat ??= new HeatPass(HEAT_LAYER);
            this.heat.render(this.renderer, this.scene, this.camera, this.heatDebug);
        } else {
            this.renderer.setRenderTarget(null);
            this.renderer.render(this.scene, this.camera);
        }

        if (bloom !== null) {
            const frame = this.frameBuffer();

            this.renderer.copyFramebufferToTexture(frame);
            bloom.render(this.renderer, frame);
        }
    }

    /**
     * The copy of the finished frame a post pass reads, built on first use.
     *
     * A `FramebufferTexture` rather than a render target: what goes in it is the canvas exactly as
     * drawn - resolved and encoded - so it wants neither samples nor a colour space. Telling the
     * sampler it is sRGB would have the hardware decode pixels that are already where they belong.
     */
    private frameBuffer(): THREE.FramebufferTexture {
        this.renderer.getDrawingBufferSize(FRAME_SIZE);
        const width = Math.max(1, FRAME_SIZE.x);
        const height = Math.max(1, FRAME_SIZE.y);

        if (this.frameCopy === null) {
            this.frameCopy = new THREE.FramebufferTexture(width, height);
        } else if (this.frameCopy.image.width !== width || this.frameCopy.image.height !== height) {
            // It cannot be resized in place - the copy would land in a buffer of the wrong shape.
            this.frameCopy.dispose();
            this.frameCopy = new THREE.FramebufferTexture(width, height);
        }

        return this.frameCopy;
    }

    /** Whether heat sprites bend the frame. */
    setHeat(on: boolean): void {
        this.heatEnabled = on;
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
        this.heatDebug = on;
    }

    setBloom(on: boolean): void {
        this.bloomEnabled = on;

        if (!on) {
            // Give the buffer back rather than holding a full-screen copy of the canvas for a
            // switch the reader has turned off.
            this.frameCopy?.dispose();
            this.frameCopy = null;
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
        const light = BILLBOARD_LIGHT
            .copy(lightDirection(this.lightAzimuth, this.lightElevation))
            .negate();

        for (const node of this.billboards) {
            const type = billboardTypeOf(node);
            if (type === null) {
                continue;
            }

            node.updateWorldMatrix(true, false);
            node.getWorldPosition(BILLBOARD_AT);
            node.getWorldQuaternion(BILLBOARD_BONE);

            const world = billboardRotation(type, this.camera, light, BILLBOARD_AT);

            for (const child of node.children) {
                if (child instanceof THREE.Mesh) {
                    // Local, so that the bone's own world rotation composes back out to exactly the
                    // orientation the billboard asked for.
                    child.quaternion.copy(BILLBOARD_BONE).invert().multiply(world);
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
        const planes = clipPlanes(sphere, distance, this.effectReach(), this.drawDistance);

        this.camera.near = planes.near;
        this.camera.far = planes.far;
        this.camera.updateProjectionMatrix();
    }

    /** How far the running effects get from the model's centre. */
    private effectReach(): number {
        let reach = 0;

        for (const entry of this.particleSystems.values()) {
            reach = Math.max(reach, entry.instance.extent());
        }

        return reach;
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

    /** Whether any running system has a heat emitter on screen this frame. */
    private distorting(): boolean {
        // The debug view has to draw the buffer whether or not anything is currently bending it -
        // an empty neutral field IS the answer when nothing is.
        if (this.heatDebug && this.heatEnabled) {
            return true;
        }

        if (!this.heatEnabled || !this.particlesVisible) {
            return false;
        }

        for (const entry of this.particleSystems.values()) {
            if (entry.instance.hasVisibleHeat) {
                return true;
            }
        }

        return false;
    }

    dispose(): void {
        this.disposed = true;
        this.canvas.removeEventListener('pointerdown', this.onPointerDown);
        cancelAnimationFrame(this.frameHandle);
        this.clear();
        this.controls.dispose();
        this.heat?.dispose();
        this.bloom?.dispose();
        this.shadowVolumes.dispose();
        this.frameCopy?.dispose();
        this.renderer.dispose();
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

/** A mesh the exporter marked as a shadow volume, rather than part of the model's own shape. */
function isShadowVolume(mesh: THREE.Mesh): boolean {
    const extras = (mesh.userData.alamo ?? {}) as MaterialExtras;

    return resolveMaterial(extras).archetype === 'shadow-volume';
}

/** The twelve edges of one box, as line-segment pairs, appended to a position buffer. */
function appendBoxEdges(into: number[], box: THREE.Box3): void {
    const { min, max } = box;
    const corner = (x: number, y: number, z: number): [number, number, number] =>
        [x === 0 ? min.x : max.x, y === 0 ? min.y : max.y, z === 0 ? min.z : max.z];

    // Each edge joins two corners differing in exactly one coordinate, which is what this walk
    // enumerates - four edges along each axis, twelve in all, no duplicates.
    for (let axis = 0; axis < 3; axis++) {
        for (let pair = 0; pair < 4; pair++) {
            const others = [pair & 1, (pair >> 1) & 1];
            const at = (end: number): [number, number, number] => {
                const coords = [...others];
                coords.splice(axis, 0, end);

                return corner(coords[0], coords[1], coords[2]);
            };

            into.push(...at(0), ...at(1));
        }
    }
}

/** Replaces a buffer geometry's positions, reallocating only when the length changes. */
function setPositions(geometry: THREE.BufferGeometry, values: number[]): void {
    const existing = geometry.getAttribute('position') as THREE.BufferAttribute | undefined;

    if (existing !== undefined && existing.array.length === values.length) {
        (existing.array as Float32Array).set(values);
        existing.needsUpdate = true;
        return;
    }

    geometry.setAttribute('position', new THREE.Float32BufferAttribute(values, 3));
}

/** Releases GPU memory for a subtree; the renderer does not do it on removal. */
function disposeTree(root: THREE.Object3D): void {
    root.traverse(node => {
        if (!(node instanceof THREE.Mesh)) {
            return;
        }

        node.geometry.dispose();
        for (const material of Array.isArray(node.material) ? node.material : [node.material]) {
            material.dispose();
        }
    });
}

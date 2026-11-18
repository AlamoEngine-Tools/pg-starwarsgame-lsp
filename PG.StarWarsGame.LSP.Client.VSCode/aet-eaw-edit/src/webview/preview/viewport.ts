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

import { FIELD_OF_VIEW } from "./framing";
import type {
    AlamoParticleContent,
    PreviewBreakoffProp,
    PreviewSpinAway,
    PreviewTurret,
} from "../../protocol/modelPreview";
import type { NormalisedColour } from "./colour";
import type { FxMaterialState } from "./fx/renderState";
import { type DefinedLevels, type LevelCost } from "./levels";
import type { TranslatedEffect } from "./fx/effect";
import { type MaterialExtras } from "./materials";
import { type Vector3 as Vector3Like } from "./lighting";
import { type TreeItem } from "./previewTree";
import { type ReticleMark } from "./reticles";
import { type BreakoffAnchor } from "./breakoff";
import { type TurretAim, type TurretHandle } from "./turretHandles";
import { type Inspection } from "./inspector";
import { type AbilityOwnership } from "./abilityRows";
import { type OverrideChange, type RowOverride } from "./visibility";
import {
    type BackgroundKind,
    type LightRig,
} from "../../shared/viewerSettings";
import { type EmitterProbe } from "./particleRenderer";
import { type BoneAttachment, type FlatBone, type LabelMode } from "./skeleton";
import { OverlayLayer } from "./overlayLayer";
import {
    ACTIVE_SUBJECT,
    BREAKOFF_ATTACHMENT,
    type LoadedPart,
} from "./loadedPart";

export { ACTIVE_SUBJECT, BREAKOFF_ATTACHMENT };
import { VIEWPORT_BACKGROUND } from "./backdrop";
export { VIEWPORT_BACKGROUND };
import { TurretRig } from "./turretRig";
import { ParticleRig } from "./particleRig";
import { MaterialBinder } from "./materialBinder";
import { EnvironmentRig } from "./environmentRig";
import { ModelRows } from "./modelRows";
import { CameraRig } from "./cameraRig";
import { PartAssembly } from "./partAssembly";
import { DestructionRig } from "./destructionRig";
import { AnimationRig } from "./animationRig";
import {
    type AddPartOptions,
    type ParticlePlacement,
    type PresetView,
    type ViewportStats,
} from "./viewportTypes";
import { CaptureRig } from "./captureRig";
import { LevelGates } from "./levelGates";
import { ModelQueries } from "./modelQueries";
import { type ViewportContext } from "./viewportContext";

export type {
    AddPartOptions,
    ParticlePlacement,
    PresetView,
    ViewportStats,
} from "./viewportTypes";

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
    /** The particle systems: what is attached where, what is fading out, and the step that runs them. See `ParticleRig`. */
    private readonly particles: ParticleRig;
    private readonly parts = new Map<string, LoadedPart>();
    private readonly modelRoot = new THREE.Group();
    /**
     * The subject's GEOMETRY, carrying the object's `Scale_Factor`.
     *
     * Between `modelRoot` and the parts rather than on `modelRoot` itself, and the split is the
     * engine's own: its transform update puts no scale in the object's world matrix and applies the
     * scale to the model instead. So the scale belongs to the MODEL: geometry and bones are model
     * units and reach the world multiplied by it, while positions - and every range the XML
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
    /** The subject's `Scale_Factor`, or 1. The layers read it through the context. */
    private modelScale = 1;
    /** The materials: what every mesh is drawn with, the textures and effects bound to them, and the team tint. See `MaterialBinder`. */
    private readonly materials: MaterialBinder;
    /** The model tree's rows: what each row stands for, its facts, and the reader's overrides. See `ModelRows`. */
    private readonly rows: ModelRows;
    /** Seconds since the viewport opened, for the effects that read the clock. */
    private elapsed = 0;
    /** The clips: each subject's mixer, the clip playing, its speed and loop, and the visibility it drives. See `AnimationRig`. */
    private readonly animation: AnimationRig;
    /** The still capture: one frame at a chosen size, with what it hides, then everything put back. See `CaptureRig`. */
    private readonly capturer: CaptureRig;
    private frameHandle = 0;
    private disposed = false;
    /** The framing: how far the camera sits, what it is fitted to, the model's own cameras and the clip planes. See `CameraRig`. */
    private readonly cameraRig: CameraRig;
    /** The detail and damage level, and the gates on what is drawn at it. See `LevelGates`. */
    private readonly levels: LevelGates;
    /** What the model holds, asked of it: the skeleton, a bone's place, the stats and the levels it defines. See `ModelQueries`. */
    private readonly queries: ModelQueries;
    /** Labels, reticles, selection boxes, the skeleton and the bone axes. See `OverlayLayer`. */
    private readonly overlay: OverlayLayer;
    /** The firing arcs, the turret handles, the sweeps and the hand-set aims. See `TurretRig`. */
    private readonly turret: TurretRig;
    /** The wreckage in the air and the death run of a ship spinning away. See `DestructionRig`. */
    private readonly destruction: DestructionRig;
    /** The parts: loading one into the scene, attaching it to its bone, hiding and removing it, and walking their meshes. See `PartAssembly`. */
    private readonly assembly: PartAssembly;

    constructor(
        private readonly canvas: HTMLCanvasElement,
        /** Absolutely-positioned container the bone labels are written into. */
        labelLayer: HTMLElement,
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

        const viewport = this;
        // One context for every layer: the scene objects, made once and never replaced, and the layers
        // themselves, so one layer asks another directly. The layers are getters because they are built
        // below one after another. What a layer still asks of the viewport itself - its clock, its scale,
        // its callbacks - is that layer's own host, read live at the moment the layer needs it.
        const context: ViewportContext = {
            scene: this.scene,
            camera: this.camera,
            renderer: this.renderer,
            canvas,
            controls: this.controls,
            loader: this.loader,
            modelRoot: this.modelRoot,
            scaledRoot: this.scaledRoot,
            parts: this.parts,
            labelLayer,
            get modelScale() {
                return viewport.modelScale;
            },
            get levels() {
                return viewport.levels;
            },
            get queries() {
                return viewport.queries;
            },
            get assembly() {
                return viewport.assembly;
            },
            get animation() {
                return viewport.animation;
            },
            get cameraRig() {
                return viewport.cameraRig;
            },
            get environment() {
                return viewport.environment;
            },
            get rows() {
                return viewport.rows;
            },
            get materials() {
                return viewport.materials;
            },
            get particles() {
                return viewport.particles;
            },
            get overlay() {
                return viewport.overlay;
            },
            get turret() {
                return viewport.turret;
            },
        };
        this.levels = new LevelGates(context);
        this.queries = new ModelQueries(context);
        this.assembly = new PartAssembly(context);
        this.destruction = new DestructionRig(context, {
            get onBreakoffExpired() {
                return viewport.onBreakoffExpired;
            },
        });
        this.animation = new AnimationRig(context);
        this.capturer = new CaptureRig(context, {
            resize: () => viewport.resize(),
        });
        // Before the environment, which places its key light against the framing sphere as it is built.
        this.cameraRig = new CameraRig(context);
        this.environment = new EnvironmentRig(context, {
            get elapsed() {
                return viewport.elapsed;
            },
        });
        this.rows = new ModelRows(context);
        this.overlay = new OverlayLayer(context, {
            get onReticleClicked() {
                return viewport.onReticleClicked;
            },
        });
        this.turret = new TurretRig(context, {
            get onTurretAimed() {
                return viewport.onTurretAimed;
            },
        });
        this.materials = new MaterialBinder(context);
        this.particles = new ParticleRig(context);

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
     * stands. See `ParticleRig.particleProbe`.
     */
    particleProbe(): {
        camera: Vector3Like;
        systems: { id: string; emitters: EmitterProbe[] }[];
    } {
        return this.particles.particleProbe();
    }

    /**
     * Renders one frame and counts the canvas pixels that differ from its dominant colour, for the
     * UI tests. See `CameraRig.canvasCoverage`.
     */
    canvasCoverage(): { lit: number; total: number } {
        return this.cameraRig.canvasCoverage();
    }

    /** The bounding sphere a preset is resolved against, so the caller sizes the shot correctly. */
    get subjectSphere(): { center: Vector3Like; radius: number } {
        return this.cameraRig.subjectSphere;
    }

    get animationNames(): string[] {
        return this.animation.animationNames;
    }

    /**
     * Moves the key light, in the same azimuth/elevation terms as the camera presets. See
     * `EnvironmentRig.setLightAngles`.
     */
    setLightAngles(azimuthDegrees: number, elevationDegrees: number): void {
        this.environment.setLightAngles(azimuthDegrees, elevationDegrees);
    }

    /**
     * Takes the whole rig: three directionals with their own colours, plus the global terms. See
     * `EnvironmentRig.setLightRig`.
     */
    setLightRig(rig: LightRig): void {
        this.environment.setLightRig(rig);
    }

    /** Chooses what is behind the model. See `EnvironmentRig.setBackground`. */
    setBackground(kind: BackgroundKind): void {
        this.environment.setBackground(kind);
    }

    /** Sets the one wind the scene has. See `ParticleRig.setWind`. */
    setWind(headingDegrees: number, speed: number): void {
        this.particles.setWind(headingDegrees, speed);
    }

    /** Sets the weather phase's intensity. See `ParticleRig.setWeatherIntensity`. */
    setWeatherIntensity(intensity: number): void {
        this.particles.setWeatherIntensity(intensity);
    }

    /** Whether the scene holds a weather field, for the control that only reaches one. */
    hasWeather(): boolean {
        return this.particles.hasWeather();
    }

    /**
     * Adds a model, attached to a bone of an already-loaded part when asked. See
     * `PartAssembly.addPart`.
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
     * Every named attachment that still does not resolve, in words. See
     * `PartAssembly.unresolvedAttachments`.
     */
    unresolvedAttachments(): string[] {
        return this.assembly.unresolvedAttachments();
    }

    /**
     * Applies a shader's declared render state to every material using it. See
     * `MaterialBinder.applyShaderState`.
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
     * Draws with a translated effect instead of its archetype, wherever that effect is used. See
     * `MaterialBinder.applyTranslatedEffect`.
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
     * there are. See `MaterialBinder.translatedCoverage`.
     */
    translatedCoverage(): { translated: number; total: number } {
        return this.materials.translatedCoverage();
    }

    /**
     * Samplers on translated materials that are reading nothing, in words. See
     * `MaterialBinder.shaderTextureProblems`.
     */
    shaderTextureProblems(): string[] {
        return this.materials.shaderTextureProblems();
    }

    /** Applies a decoded texture wherever a material asked for it by name. */
    setTexture(name: string, texture: THREE.Texture): void {
        this.materials.setTexture(name, texture);
    }

    /**
     * Binds every texture already decoded into a subtree that has only just been added. See
     * `MaterialBinder.bindLoadedTextures`.
     */
    bindLoadedTextures(root: THREE.Object3D): void {
        this.materials.bindLoadedTextures(root);
    }

    /** Removes everything, so a new subject starts from an empty scene. */
    clear(): void {
        this.particles.forgetSystems();

        // BEFORE the sweep below, not after: a wreck is one of those parts now, and taking it
        // apart a second time would dispose a tree that has already gone.
        this.clearBreakoffs();

        // BEFORE the parts are disposed. The pass holds its counting meshes by their SOURCE mesh,
        // and those sources are inside the trees about to go - so letting them be disposed first
        // leaves the map keyed by objects nobody else can reach.
        //
        // Nothing else takes them: `removePart` unregisters through
        // `PartAssembly.dropRegistrations`, but this sweep does not go through `removePart`, so every
        // subject switch left its volumes behind.
        // Measured: the palace alone registers 4 sources and 8 counting meshes; opened after the
        // Executor it reported 8 and 16, half of them belonging to a model no longer in the scene.
        // The stencil pass then counts geometry that is not there, which is why a handful of models
        // rendered differently depending on what had been looked at before them.
        this.environment.forgetShadowVolumes();

        this.assembly.disposeParts();

        this.turret.clearFireArcs();
        this.turret.clearTurretHandles();

        // The scale belongs to the SUBJECT. Left standing, the next one would be drawn at the last
        // one's - and a 0.5 ship followed by a 2.0 one is a four-fold error that looks like a
        // broken model rather than stale state.
        this.modelScale = 1;
        this.scaledRoot.scale.setScalar(1);

        this.cameraRig.forgetSpan();
        // The marks name parts that are about to stop existing; leaving them would draw a reticle
        // over the next model's geometry at whatever the last one's bounds happened to be.
        this.overlay.dropReticles();
        this.assembly.forgetParts();
        this.cameraRig.forgetBillboards();
        this.levels.forgetModel();
        // Every subject's clips, mixer, tracks, rest pose and node names at once. They hold nodes
        // of a model nobody is looking at any more, and keeping them would leak the whole graph.
        this.animation.forgetSubjects();
    }

    /** Which decal meshes exist, and which of them are showing. See `LevelGates.setDecals`. */
    setDecals(known: ReadonlySet<string>, shown: ReadonlySet<string>): void {
        this.levels.setDecals(known, shown);
    }

    /**
     * Which meshes the hardpoints claim as collision hulls. See `LevelGates.setCollisionMeshes`.
     */
    setCollisionMeshes(names: ReadonlySet<string>): void {
        this.levels.setCollisionMeshes(names);
    }

    /**
     * Shield meshes the model names but does not draw with a shield shader. See
     * `MaterialBinder.shieldMeshesOffShader`.
     */
    shieldMeshesOffShader(): string[] {
        return this.materials.shieldMeshesOffShader();
    }

    /** Shows or hides the shield mesh, for the abilities that reveal it. */
    setShieldRevealed(revealed: boolean): void {
        this.levels.setShieldRevealed(revealed);
    }

    /** Cloaks the unit, or uncloaks it. See `LevelGates.setStealthed`. */
    setStealthed(on: boolean): void {
        this.levels.setStealthed(on);
    }

    /** Whether a cloak is engaged, which every other visibility rule defers to. */
    isStealthed(): boolean {
        return this.levels.isStealthed();
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

    /** Draws the firing arcs, replacing whatever was there. See `TurretRig.setFireArcs`. */
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

    /** Poses turrets at their rest angle. See `TurretRig.setTurretRestAngles`. */
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
     * Sweeps the turrets through the traverse their XML declares. See `TurretRig.setTurretSweep`.
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
     * Puts drag handles on the turrets whose arcs are shown, and takes them off the rest. See
     * `TurretRig.setTurretHandles`.
     */
    setTurretHandles(handles: readonly TurretHandle[]): void {
        this.turret.setTurretHandles(handles);
    }

    /** Where the reader has pointed each turret, for the shell to read back out. */
    turretAim(id: string): TurretAim {
        return this.turret.turretAim(id);
    }

    /** Puts one turret back where the model authored it. See `TurretRig.resetTurretAim`. */
    resetTurretAim(id: string): void {
        this.turret.resetTurretAim(id);
    }

    /** Drops a piece of wreckage where a hardpoint used to be. See `DestructionRig.addBreakoff`. */
    async addBreakoff(
        key: string,
        glbBase64: string,
        at: BreakoffAnchor,
        prop: PreviewBreakoffProp,
    ): Promise<void> {
        return this.destruction.addBreakoff(key, glbBase64, at, prop);
    }

    /** Clears the wreckage ONE hardpoint shed. See `DestructionRig.clearBreakoff`. */
    clearBreakoff(key: string): void {
        this.destruction.clearBreakoff(key);
    }

    /** Clears every piece of wreckage - a repair puts the hardpoints back, so the debris goes. */
    clearBreakoffs(): void {
        this.destruction.clearBreakoffs();
    }

    /** Whether a piece of wreckage for this key is already in the air. */
    hasBreakoff(key: string): boolean {
        return this.destruction.hasBreakoff(key);
    }

    /** Starts the wreck on its way. See `DestructionRig.startSpinAway`. */
    startSpinAway(
        partId: string,
        spin: PreviewSpinAway,
        onDone: () => void,
    ): void {
        this.destruction.startSpinAway(partId, spin, onDone);
    }

    /** Puts the wreck back where it started, whether it is still flying or already exploded. */
    clearSpinAway(): void {
        this.destruction.clearSpinAway();
    }

    /** Breaks an attached part off, or puts it back. */
    setPartHidden(partId: string, hidden: boolean): void {
        this.assembly.setPartHidden(partId, hidden);
    }

    /** Removes one loaded part and frees its geometry. See `PartAssembly.removePart`. */
    removePart(partId: string): void {
        this.assembly.removePart(partId);
    }

    /** Plays a system once and forgets it. See `ParticleRig.playOnce`. */
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
     * Sets the damage state and detail level, the way the engine gates geometry. See
     * `LevelGates.setLevels`.
     */
    setLevels(alt: number, lod: number): void {
        this.levels.setLevels(alt, lod);
    }

    /** The ALT and LOD levels the loaded parts and effects actually define. */
    definedLevels(): DefinedLevels {
        return this.queries.definedLevels();
    }

    /**
     * What each detail level draws, in meshes and triangles, at the current damage stage. See
     * `ModelQueries.costByLod`.
     */
    costByLod(levels: readonly number[]): Map<number, LevelCost> {
        return this.queries.costByLod(levels);
    }

    /** For the harness: drives controls that have no UI yet. Costs nothing when unused. */
    expose(): void {
        (globalThis as unknown as { __aetViewport?: unknown }).__aetViewport =
            this;
    }

    /** Everything the model is made of, as flat items for ONE tree. See `ModelRows.treeItems`. */
    treeItems(): TreeItem[] {
        return this.rows.treeItems();
    }

    /** The row that owns one effect. See `ModelRows.rowForSystem`. */
    rowForSystem(systemId: string): string {
        return this.rows.rowForSystem(systemId);
    }

    /** What a tree row actually is, for the inspector. See `ModelRows.inspectionOf`. */
    inspectionOf(id: string): Inspection[] {
        return this.rows.inspectionOf(id);
    }

    /** The rows the reader has switched off by hand. See `ModelRows.hiddenItemIds`. */
    hiddenItemIds(): string[] {
        return this.rows.hiddenItemIds();
    }

    /** The rows the reader has switched ON by hand. See `ModelRows.shownItemIds`. */
    shownItemIds(): string[] {
        return this.rows.shownItemIds();
    }

    /**
     * Shows or hides any mix of bones, meshes and particle systems at once. See
     * `ModelRows.setItemsVisible`.
     */
    setItemsVisible(ids: readonly string[], visible: boolean): void {
        this.rows.setItemsVisible(ids, visible);
    }

    /**
     * Records the reader's word on a set of rows and re-runs the chain. See
     * `ModelRows.applyRowOverrides`.
     */
    applyRowOverrides(changes: readonly OverrideChange[]): void {
        this.rows.applyRowOverrides(changes);
    }

    /** What the reader has said about each row, for carrying across a reopen. */
    rowOverrideEntries(): { row: string; override: RowOverride }[] {
        return this.rows.rowOverrideEntries();
    }

    /** Hands every row back to the model. See `ModelRows.clearRowOverrides`. */
    clearRowOverrides(): void {
        this.rows.clearRowOverrides();
    }

    /**
     * Hands the rows ONE ability speaks for back to the model. See `ModelRows.releaseAbilityRows`.
     */
    releaseAbilityRows(ownership: AbilityOwnership): void {
        this.rows.releaseAbilityRows(ownership);
    }

    /**
     * Puts the camera where the model's own authored camera stands, looking where it looks. See
     * `CameraRig.applyModelCamera`.
     */
    applyModelCamera(name: string): boolean {
        return this.cameraRig.applyModelCamera(name);
    }

    /** Points the camera at a place, from a place. See `CameraRig.applyCameraPose`. */
    applyCameraPose(position: Vector3Like, target: Vector3Like): void {
        this.cameraRig.applyCameraPose(position, target);
    }

    /** Frames whatever is currently loaded. See `CameraRig.frameAll`. */
    frameAll(preset: PresetView = "threeQuarter"): void {
        this.cameraRig.frameAll(preset);
    }

    setGridVisible(visible: boolean): void {
        this.environment.setGridVisible(visible);
    }

    /** Shows or hides the ground plane. See `EnvironmentRig.setFloorVisible`. */
    setFloorVisible(visible: boolean): void {
        this.environment.setFloorVisible(visible);
    }

    /**
     * Moves the ground, and the grid measuring it, to a height in model units. See
     * `EnvironmentRig.setFloorLevel`.
     */
    setFloorLevel(level: number): void {
        this.environment.setFloorLevel(level);
    }

    /** Draws every part mesh as its edges. See `MaterialBinder.setWireframe`. */
    setWireframe(on: boolean): void {
        this.materials.setWireframe(on);
    }

    /**
     * Whether the engine's stencil shadows are being cast right now. See
     * `EnvironmentRig.castsStencilShadows`.
     */
    castsStencilShadows(): boolean {
        return this.environment.castsStencilShadows();
    }

    /** Draws the extruded volumes themselves, for diagnosing what the stencil is counting. */
    setShadowVolumeDebug(on: boolean): void {
        this.environment.setShadowVolumeDebug(on);
    }

    /** Whether the engine's stencil shadows are drawn at all. See `EnvironmentRig.setShadowVolumes`. */
    setShadowVolumes(on: boolean): void {
        this.environment.setShadowVolumes(on);
    }

    /** Tints the shadow, wherever it can be seen. See `EnvironmentRig.setShadowColour`. */
    setShadowColour(colour: THREE.ColorRepresentation): void {
        this.environment.setShadowColour(colour);
    }

    /** Starts a passive subject's own death clip, once. See `AnimationRig.startDeathClip`. */
    startDeathClip(subjectId: string): void {
        this.animation.startDeathClip(subjectId);
    }

    /** Starts a clip by name, answering whether one actually started. See `AnimationRig.play`. */
    play(name: string | null): boolean {
        return this.animation.play(name);
    }

    setPaused(paused: boolean): void {
        this.animation.setPaused(paused);
    }

    /** Puts every animated node back where the model file had it. See `AnimationRig.resetPose`. */
    resetPose(): void {
        this.animation.resetPose();
    }

    /** How fast the clip runs, as a multiple of the rate its file declares. */
    setAnimationSpeed(speed: number): void {
        this.animation.setAnimationSpeed(speed);
    }

    setAnimationLoop(loop: boolean): void {
        this.animation.setAnimationLoop(loop);
    }

    /** Moves the playhead, in seconds. See `AnimationRig.seekAnimation`. */
    seekAnimation(seconds: number): void {
        this.animation.seekAnimation(seconds);
    }

    /**
     * Where the playhead is and how long the clip runs, for the scrubber. See
     * `AnimationRig.animationProgress`.
     */
    animationProgress(): { time: number; duration: number; running: boolean } {
        return this.animation.animationProgress();
    }

    stats(): ViewportStats {
        return this.queries.stats();
    }

    /**
     * Where a bone sits in world space, or null when it is not loaded. See
     * `ModelQueries.bonePosition`.
     */
    bonePosition(
        partId?: string,
        bone?: string,
    ): { x: number; y: number; z: number } | null {
        return this.queries.bonePosition(partId, bone);
    }

    /** The Alamo extras of every loaded material. See `MaterialBinder.materialExtras`. */
    materialExtras(): MaterialExtras[] {
        return this.materials.materialExtras();
    }

    /**
     * Starts a particle system, optionally attached to a bone of a loaded part. See
     * `ParticleRig.addParticleSystem`.
     */
    addParticleSystem(
        id: string,
        system: AlamoParticleContent,
        placement: ParticlePlacement,
    ): void {
        this.particles.addParticleSystem(id, system, placement);
    }

    /** Takes a system out of the scene. See `ParticleRig.removeParticleSystem`. */
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
     * Says a texture will never arrive, so every emitter naming it shows the marker. See
     * `ParticleRig.setParticleTextureMissing`.
     */
    setParticleTextureMissing(name: string): void {
        this.particles.setParticleTextureMissing(name);
    }

    setParticlesVisible(visible: boolean): void {
        this.particles.setParticlesVisible(visible);
    }

    /**
     * What the game's rules say about a system: the opening rule and the current damage state. See
     * `ParticleRig.setParticleSystemVisible`.
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

    /** Multiplier on particle time. See `ParticleRig.setParticleSpeed`. */
    setParticleSpeed(speed: number): void {
        this.particles.setParticleSpeed(speed);
    }

    /** Replays every system from nothing. See `ParticleRig.restartParticles`. */
    restartParticles(): void {
        this.particles.restartParticles();
    }

    /** The hull's bones, flattened. See `ModelQueries.skeleton`. */
    skeleton(): FlatBone[] {
        return this.queries.skeleton();
    }

    /** What is attached to each bone of the hull, keyed by bone index. */
    attachmentsByBone(): Map<number, BoneAttachment[]> {
        return this.queries.attachmentsByBone();
    }

    setSkeletonVisible(visible: boolean): void {
        this.overlay.setSkeletonVisible(visible);
    }

    setLabelMode(mode: LabelMode): void {
        this.overlay.setLabelMode(mode);
    }

    /**
     * Which bones a weapon fires from, so the triad can mark the aim axis. See
     * `OverlayLayer.setFireBones`.
     */
    setFireBones(names: ReadonlySet<string>): void {
        this.overlay.setFireBones(names);
    }

    setSelectedBones(indices: ReadonlySet<number>): void {
        this.overlay.setSelectedBones(indices);
    }

    /** Draws a box round what the tree has selected. See `OverlayLayer.setSelectedRows`. */
    setSelectedRows(ids: readonly string[]): void {
        this.overlay.setSelectedRows(ids);
    }

    /**
     * Parts to outline whole, beside whatever rows are boxed. See
     * `OverlayLayer.setSelectedHardpoint`.
     */
    setSelectedHardpoint(partId: string | null, boneRow: string | null): void {
        this.overlay.setSelectedHardpoint(partId, boneRow);
    }

    /** The targeting marks to draw, and how big. See `OverlayLayer.setReticles`. */
    setReticles(
        marks: readonly ReticleMark[],
        screenSize: number | null | undefined,
    ): void {
        this.overlay.setReticles(marks, screenSize);
    }

    /**
     * Renders one frame at a chosen size and hands back the PNG bytes. See `CaptureRig.capture`.
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
        return this.capturer.capture(options);
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

    /** Draws the heat buffer rather than the bent picture. See `EnvironmentRig.setHeatDebug`. */
    setHeatDebug(on: boolean): void {
        this.environment.setHeatDebug(on);
    }

    setBloom(on: boolean): void {
        this.environment.setBloom(on);
    }

    /** The reader's draw-distance multiplier. See `CameraRig.setDrawDistance`. */
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
        this.environment.dispose();
        this.renderer.dispose();
    }

    private onPointerDown = (event: PointerEvent): void => {
        if (this.turret.grabKnob(event)) {
            return;
        }

        if (!this.overlay.skeletonShown || this.onBoneSelected === null) {
            return;
        }

        const index = this.overlay.jointAt(event);
        if (index !== null) {
            this.onBoneSelected(index);
        }
    };

    private loop = (): void => {
        if (this.disposed) {
            return;
        }

        this.frameHandle = requestAnimationFrame(this.loop);

        const dt = this.clock.getDelta();
        this.elapsed += dt;
        this.animation.advance(dt);
        this.animation.applyAnimatedVisibility();
        // After the mixer, so a clip that drives the same bone wins: an animation is the model's own
        // word about where the turret is, and the sweep is the reader asking a question about it.
        this.turret.advanceTurretSweep(dt);
        this.destruction.advanceBreakoffs(dt);
        this.destruction.advanceSpinAway(dt);
        // LAST among the things that move bones: the clip, the sweep and the spin-away have all had
        // their say, so the muzzle this reads is the one that will be drawn.
        this.turret.followHullArcs();
        // Same reason and same place: the turret bones have been moved for this frame, so the
        // tracks can now be put back in the frames their angles are measured against.
        this.turret.placeTurretAnchors();

        this.animation.advancePassiveSubjects(dt);
        this.controls.update();

        this.particles.step(dt, this.camera.matrixWorldInverse);

        this.overlay.beforeCamera(this.animation.active.mixer !== null);
        this.cameraRig.prepareForCamera();
        // After the camera's matrix is updated, so the marks are projected through THIS frame's view.
        // See `OverlayLayer.afterCamera`.
        this.overlay.afterCamera();

        this.environment.renderFrame();
    };
}

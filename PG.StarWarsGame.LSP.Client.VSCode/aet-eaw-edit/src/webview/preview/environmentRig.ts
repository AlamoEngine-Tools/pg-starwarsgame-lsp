// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The viewport's room and light: the key light, the ambient and the light rig, the sky and the stars, the
// grid, the axes, the floor and its shadow catcher, the stencil shadow volumes, the lighting frame every
// Alamo material reads, and the pass chain - scene, heat, bloom - that draws a frame. It reads the camera,
// the scene and the meshes; nothing else.

import * as THREE from "three";
import { concreteNoise, GROUND_TEXTURE_SIZE } from "./groundTexture";
import {
    type LightRig,
    DEFAULT_LIGHTS,
    type BackgroundKind,
} from "../../shared/viewerSettings";
import {
    DEFAULT_LIGHT_AZIMUTH_DEGREES,
    DEFAULT_LIGHT_ELEVATION_DEGREES,
    lightDirection,
    shadowFrustum,
} from "./lighting";
import {
    sphericalHarmonics,
    type ProbeLight,
    type AlamoFrame,
    engineLight,
} from "./fx/uniforms";
import { HeatPass } from "./heatPass";
import { ShadowVolumePass } from "./shadowVolumePass";
import { BloomPass } from "./bloomPass";
import { shadowCatcherOpacity } from "./materialState";
import { starPositions, skyGradient, VIEWPORT_BACKGROUND } from "./backdrop";
import { type BoundingSphere } from "./framing";
import {
    type MaterialExtras,
    resolveMaterial,
    castsShadowMap,
} from "./materials";
import { HEAT_LAYER } from "./particleRenderer";
import { type ViewportContext } from "./viewportContext";
import { type CaptureShot } from "./viewportTypes";

/** Fixed, so the sky is the same one every session and a screenshot can be compared to another. */
const STAR_SEED = 20260817;

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

/** What EnvironmentRig still asks of the viewport itself: what only the viewport has. */
export interface EnvironmentHost {
    readonly elapsed: number;
}

export class EnvironmentRig {
    readonly grid: THREE.GridHelper;
    readonly floor: THREE.Mesh;
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
            concreteNoise(),
            GROUND_TEXTURE_SIZE,
            GROUND_TEXTURE_SIZE,
        );

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
    /** The starfield and the sky dome, built on first use and then just shown or hidden. */
    private stars: THREE.Points | null = null;
    private sky: THREE.Mesh | null = null;
    lightAzimuth = DEFAULT_LIGHT_AZIMUTH_DEGREES;
    lightElevation = DEFAULT_LIGHT_ELEVATION_DEGREES;
    readonly axes: THREE.AxesHelper;
    // Held on the viewport rather than read off the action, because they have to survive the action
    // being torn down and rebuilt - which happens on every clip change. Setting the loop mode on the
    /** The directional rig, in the order the engine numbers its lights. */
    private alamoLights: THREE.DirectionalLight[] = [];
    /**
     * The light probe the translated shaders read, kept in step with the rig.
     *
     * ALL of their diffuse lighting, not an ambient on top of it - `MeshAlpha.fx` sets the whole
     * vertex colour from the spherical-harmonic probe and has no N.L anywhere. So the sun and
     * both fills go in here or they do not reach a Game-mode surface at all, which is what a flat
     * probe built from the ambient alone was doing: 0.1 grey over the entire model, unshaded.
     *
     * Rebuilt whenever the rig changes rather than fixed at construction. It used to be a hard 0.6
     * that nothing ever updated, so Game mode washed out under six times the engine's own 0.1 and
     * the reader's Ambient slider moved the scene light while the translated shaders ignored it.
     */
    private harmonics = sphericalHarmonics([], DEFAULT_LIGHTS.ambient.colour);
    /** The same probe over the two fills alone, which is what `m_sphFill` means. */
    private fillHarmonics = sphericalHarmonics(
        [],
        DEFAULT_LIGHTS.ambient.colour,
    );
    /** Built the first time a heat sprite is on screen, and not before - it costs two buffers. */
    private heat: HeatPass | null = null;
    /**
     * The engine's own shadows: stencil volumes cast by the authored shadow mesh.
     *
     * Lives in the scene graph and is ordered after the model, so one `renderer.render` still makes
     * the finished frame whether it is going to the canvas, the heat composite or bloom.
     */
    readonly shadowVolumes = new ShadowVolumePass();
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
    /**
     * A shadow-only plane over the floor.
     *
     * Separate from the floor rather than replacing its material: `ShadowMaterial` draws the shadow
     * and NOTHING else, so using it for the floor itself would trade a lit ground surface for a
     * tinted shadow floating in mid-air. Two coplanar planes, the catcher pushed in front by a
     * polygon offset, gives both.
     */
    readonly shadowCatcher: THREE.Mesh;
    private readonly shadowMaterial: THREE.ShadowMaterial;

    constructor(
        private readonly context: ViewportContext,
        private readonly host: EnvironmentHost,
    ) {
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
            }),
        );
        this.floor.rotation.x = -Math.PI / 2;
        this.floor.visible = false;

        // Rides with the floor and is only ever shown with it: with no ground there is nothing for
        // a shadow to fall on, and a tinted patch hanging in space would be a lie about the scene.
        this.shadowMaterial = new THREE.ShadowMaterial({
            // BLACK, always. The catcher DRAWS its colour rather than multiplying, and the reader's
            // shadow value is a MULTIPLIER - painting that value put a mid grey on a dark floor and
            // made the shadow brighter than the ground. Black at `1 - m` gives `dst * m` instead;
            // `setShadowColour` sets the alpha. It only applies to the 63% of models that author no
            // volume, which is why it showed up on trees.
            color: 0x000000,
            transparent: true,
            // `DEFAULT_LIGHTS.shadow` is 0.5 - half as bright - and `setShadowColour` re-derives
            // this the moment a light rig is applied, which is on mount.
            opacity: 0.5,
            depthWrite: false,
            polygonOffset: true,
            polygonOffsetFactor: -1,
            polygonOffsetUnits: -1,
        });

        this.shadowCatcher = new THREE.Mesh(
            new THREE.PlaneGeometry(100, 100),
            this.shadowMaterial,
        );
        this.shadowCatcher.rotation.x = -Math.PI / 2;
        this.shadowCatcher.receiveShadow = true;
        this.shadowCatcher.visible = false;
        this.floor.receiveShadow = true;

        this.context.scene.add(
            this.grid,
            this.axes,
            this.floor,
            this.shadowCatcher,
        );
        this.context.scene.add(this.shadowVolumes.darken);
    }

    /** Forgets every shadow volume, BEFORE the parts whose meshes they are keyed by are disposed. */
    forgetShadowVolumes(): void {
        this.shadowVolumes.clear();
    }

    /** Stops counting the shadow volumes among these nodes, before their subtree is disposed. */
    dropShadowVolumes(nodes: readonly THREE.Object3D[]): void {
        for (const node of nodes) {
            if (node instanceof THREE.Mesh) {
                this.shadowVolumes.remove(node);
            }
        }
    }

    /** Keeps the sky and the stars with the camera, after it has moved and its planes are set. */
    followCamera(): void {
        // The sky dome travels with the camera and keeps its own orientation, so the horizon stays
        // level and infinitely far however the camera is turned.
        if (this.sky !== null && this.sky.visible) {
            this.sky.position.copy(this.context.camera.position);
            this.sky.scale.setScalar(
                Math.max(
                    this.context.camera.near * 4,
                    this.context.camera.far * 0.4,
                ),
            );
        }

        // The stars need the same treatment, and for a reason that took a regression to notice:
        // parenting them to the CAMERA exempts them from frustum CULLING but not from the far
        // plane, which clips their vertices like anything else's. The far plane is fitted to the
        // model, so the moment the hardpoint rotation was corrected and the bounds shrank, the
        // whole sky fell outside it and the stars simply went out.
        if (this.stars !== null && this.stars.visible) {
            this.stars.scale.setScalar(
                Math.max(
                    this.context.camera.near * 4,
                    this.context.camera.far * 0.4,
                ),
            );
        }
    }

    /** Releases the passes' GPU memory. */
    dispose(): void {
        this.heat?.dispose();
        this.bloom?.dispose();
        this.shadowVolumes.dispose();
        this.frameCopy?.dispose();
    }

    /**
     * Whether the stencil volumes are the thing casting this model's shadow right now.
     *
     * Both halves matter. Weighing only "does the model author a volume" left a model with volumes
     * casting NOTHING in the shading mode that does not draw volumes - it had already handed its
     * shadow over to a pass that was switched off.
     */
    get stencilShadowing(): boolean {
        return this.shadowVolumesEnabled && this.shadowVolumes.active;
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

        // The probe IS the diffuse lighting for every translated shader, so a sun that moves
        // without rebuilding it leaves Game mode lit from where the sun was.
        this.buildProbe();
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
        if (kind === "starfield") {
            this.stars ??= this.buildStars();
        }

        if (kind === "sky") {
            this.sky ??= this.buildSky();
        }

        if (this.stars !== null) {
            this.stars.visible = kind === "starfield";
        }

        if (this.sky !== null) {
            this.sky.visible = kind === "sky";
        }

        // Space is black behind the stars; the flat backdrop keeps the neutral grey that reads as
        // "no background at all" rather than as a choice.
        this.context.scene.background = new THREE.Color(
            kind === "starfield" ? 0x05070c : VIEWPORT_BACKGROUND,
        );
    }

    setGridVisible(visible: boolean): void {
        this.grid.visible = visible;
        this.axes.visible = visible;
    }

    /**
     * Sets the room as a still capture asks - its backdrop, grid, floor and axes - and returns what puts
     * them back as they were. The axes are a reading aid, so they go with the shot's annotations.
     */
    stageForCapture(shot: CaptureShot): () => void {
        const before = {
            background: this.context.scene.background,
            grid: this.grid.visible,
            floor: this.floor.visible,
            catcher: this.shadowCatcher.visible,
            axes: this.axes.visible,
        };

        // Null is TRANSPARENT, which is what an icon wants: the alpha the renderer was created
        // with is what makes it possible at all.
        this.context.scene.background =
            shot.background === null ? null : new THREE.Color(shot.background);
        this.grid.visible = shot.grid;
        this.floor.visible = shot.floor;
        this.shadowCatcher.visible = shot.floor && before.catcher;
        // The axes are drawn to help someone READ the model. They are not part of it, and an icon
        // with a coloured cross through it is not an icon - which is exactly what the first capture
        // came out as.
        this.axes.visible = shot.annotations && before.axes;

        return () => {
            this.context.scene.background = before.background;
            this.grid.visible = before.grid;
            this.floor.visible = before.floor;
            this.shadowCatcher.visible = before.catcher;
            this.axes.visible = before.axes;
        };
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
     * Whether the engine's stencil shadows are being cast right now.
     *
     * Public because the shadow COLOUR control has to know. The tint reaches the hull through this
     * pass with no ground involved, so gating that control on the floor alone disabled it while it
     * was working. Both halves matter here for the same reason they do inside: Game mode with no
     * authored volume casts nothing, and 63% of models author none.
     */
    castsStencilShadows(): boolean {
        return this.stencilShadowing;
    }

    /** Draws the extruded volumes themselves, for diagnosing what the stencil is counting. */
    setShadowVolumeDebug(on: boolean): void {
        this.shadowVolumes.setDebug(on);
    }

    /** Whether the engine's stencil shadows are drawn at all. */
    setShadowVolumes(on: boolean): void {
        this.shadowVolumesEnabled = on;
        this.shadowVolumes.setEnabled(on);
        this.refreshShadowCasting();
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
        // The catcher paints; the value is a multiplier. Only the ALPHA moves - see
        // `shadowCatcherOpacity`, which is also where the tint goes and why.
        this.shadowMaterial.opacity = shadowCatcherOpacity(colour);

        // The same colour drives the stencil darken, which really does multiply - so it takes the
        // value as it stands, tint and all, and reaches the MODEL as well as the ground.
        this.shadowVolumes.setColour(colour);
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
     * Ambient plus two directionals.
     *
     * Not an attempt at the engine's lighting - that is what the shader work is for. This exists so
     * an untextured hull reads as a solid rather than a silhouette, which is what a single light or
     * ambient alone would give.
     */
    private addLighting(): void {
        this.ambient = new THREE.AmbientLight(
            0xffffff,
            DEFAULT_LIGHTS.ambient.intensity,
        );
        this.context.scene.add(this.ambient);

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

        this.context.scene.add(key, fill1, fill2, key.target);

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

        const sphere = this.context.cameraRig.boundingSphere();
        const direction = lightDirection(
            this.lightAzimuth,
            this.lightElevation,
        );
        const frustum = shadowFrustum(sphere.radius);

        // Stood off at the frustum's far edge, so the model sits comfortably inside the depth range
        // rather than against its near plane.
        const distance = frustum.far / 2;

        light.position.set(
            sphere.center.x + direction.x * distance,
            sphere.center.y + direction.y * distance,
            sphere.center.z + direction.z * distance,
        );

        light.target.position.set(
            sphere.center.x,
            sphere.center.y,
            sphere.center.z,
        );
        light.target.updateMatrixWorld();

        // The volumes extrude away from this same light, so the two can never disagree about where
        // the shadow falls - and their reach is fitted to the model for the same reason the shadow
        // camera is.
        this.shadowVolumes.setLight(direction);
        this.shadowVolumes.setScale(sphere.radius, this.context.camera.far);

        const camera = light.shadow.camera;
        camera.left = -frustum.extent;
        camera.right = frustum.extent;
        camera.top = frustum.extent;
        camera.bottom = -frustum.extent;
        camera.near = frustum.near;
        camera.far = frustum.far;
        camera.updateProjectionMatrix();
    }

    // ── camera ────────────────────────────────────────────────────────────────

    private buildStars(): THREE.Points {
        const geometry = new THREE.BufferGeometry();
        geometry.setAttribute(
            "position",
            new THREE.BufferAttribute(starPositions(STAR_SEED), 3),
        );

        const stars = new THREE.Points(
            geometry,
            new THREE.PointsMaterial({
                color: 0xffffff,
                // In pixels, and NOT scaled by distance: these are meant to read as points of light at
                // infinity, and a star that grows as you dolly in is a speck of dust on the lens.
                size: 1.6,
                sizeAttenuation: false,
                depthTest: false,
                depthWrite: false,
            }),
        );

        // Parented to the CAMERA and drawn first: it turns with the view like a sky should and
        // never occludes anything. It is NOT exempt from the far plane, though - `frustumCulled`
        // only stops object-level culling - so `prepareForCamera` scales it to sit inside whatever
        // plane the model has been fitted with.
        stars.frustumCulled = false;
        stars.renderOrder = -1;
        this.context.camera.add(stars);
        this.context.scene.add(this.context.camera);

        return stars;
    }

    private buildSky(): THREE.Mesh {
        const geometry = new THREE.SphereGeometry(1, 32, 24);
        const colours = new Float32Array(
            geometry.attributes.position.count * 3,
        );
        const position = geometry.attributes.position;

        for (let i = 0; i < position.count; i++) {
            const [r, g, b] = skyGradient(position.getY(i));
            colours.set([r, g, b], i * 3);
        }

        geometry.setAttribute("color", new THREE.BufferAttribute(colours, 3));

        const sky = new THREE.Mesh(
            geometry,
            new THREE.MeshBasicMaterial({
                vertexColors: true,
                // Seen from the inside, and never in the depth buffer.
                side: THREE.BackSide,
                depthTest: false,
                depthWrite: false,
            }),
        );

        // Follows the camera's POSITION but not its rotation - the horizon has to stay level and at
        // eye height however the camera turns, which is what makes it read as a horizon rather than
        // as a painted backdrop stuck to the screen.
        sky.frustumCulled = false;
        sky.renderOrder = -1;
        this.context.scene.add(sky);

        return sky;
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
                const direction = lightDirection(
                    setting.azimuth,
                    setting.elevation,
                );
                light.position.set(direction.x, direction.y, direction.z);
            }
        });

        if (this.ambient !== null) {
            this.ambient.color.setRGB(...rig.ambient.colour);
            this.ambient.intensity =
                rig.ambient.intensity * ENGINE_LIGHT_TO_THREE;
        }

        this.buildProbe();
    }

    /**
     * Projects the rig into the two spherical-harmonic probes the translated shaders read.
     *
     * The sun's angles come off `lightAzimuth`/`lightElevation` rather than off `rig.sun`, because
     * those are the pair `placeKeyLight` uses and `setLightAngles` moves the light without going
     * through the rig - reading the rig here would leave the probe pointing at where the sun used
     * to be. Without the PI, which is three's BRDF correction and not the engine's.
     */
    private buildProbe(): void {
        const rig = this.rig;
        const toward = (
            azimuth: number,
            elevation: number,
        ): [number, number, number] => {
            const direction = lightDirection(azimuth, elevation);

            return [direction.x, direction.y, direction.z];
        };

        const sun: ProbeLight = {
            toLight: toward(this.lightAzimuth, this.lightElevation),
            colour: rig.sun.colour,
            intensity: rig.sun.intensity,
        };

        const fills: ProbeLight[] = [rig.fill1, rig.fill2].map((fill) => ({
            toLight: toward(fill.azimuth, fill.elevation),
            colour: fill.colour,
            intensity: fill.intensity,
        }));

        const ambient: [number, number, number] = [
            rig.ambient.colour[0] * rig.ambient.intensity,
            rig.ambient.colour[1] * rig.ambient.intensity,
            rig.ambient.colour[2] * rig.ambient.intensity,
        ];

        this.harmonics = sphericalHarmonics([sun, ...fills], ambient);
        this.fillHarmonics = sphericalHarmonics(fills, ambient);
    }

    /** The engine's per-frame contract, as it stands for one mesh. */
    frameFor(
        mesh: THREE.Mesh,
        view: THREE.Matrix4,
        projection: THREE.Matrix4,
        viewProjection: THREE.Matrix4,
        eyeWorld: THREE.Vector3,
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
            const rotationOnly = new THREE.Matrix4().extractRotation(
                worldInverse,
            );
            const inObject = direction
                .clone()
                .applyMatrix4(rotationOnly)
                .normalize();

            return engineLight(
                settings[index] ?? this.rig.sun,
                this.rig.specular,
                [direction.x, direction.y, direction.z],
                [inObject.x, inObject.y, inObject.z],
            );
        });

        // The Alamo shaders do their own bone lookup, so the palette is handed over rather than
        // left to three's vertex shader - but not raw. `Skeleton.boneMatrices` maps BIND WORLD
        // space to current world space, while the shader applies it to a mesh-local vertex and then
        // multiplies by WORLDVIEWPROJECTION, which carries the world transform a second time.
        // Conjugating by the bind matrices is what three's own skinning does, and it lands the
        // result back in mesh-local space where the rest of the shader expects it. Without it the
        // turret's barrel flew off and its dome collapsed.
        const skinMatrices =
            mesh instanceof THREE.SkinnedMesh
                ? this.context.animation.skinPalette(mesh)
                : null;

        return {
            wind: this.context.particles.wind,
            matrices: {
                world: world.elements,
                worldInverse: worldInverse.elements,
                worldView: worldView.elements,
                worldViewInverse: new THREE.Matrix4().copy(worldView).invert()
                    .elements,
                worldViewProjection: new THREE.Matrix4().multiplyMatrices(
                    viewProjection,
                    world,
                ).elements,
                view: view.elements,
                viewInverse: this.context.camera.matrixWorld.elements,
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
            time: this.host.elapsed,
            resolution: [
                this.context.canvas.clientWidth,
                this.context.canvas.clientHeight,
                0,
                0,
            ],
            sphericalHarmonics: this.harmonics,
            sphericalHarmonicsFill: this.fillHarmonics,
            skinMatrices,
        };
    }

    rebuildGrid(sphere: BoundingSphere): void {
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
     * Adopts this part's authored shadow meshes as stencil volumes.
     *
     * The source meshes stay exactly as they were - switched off, drawn only when the reader ticks
     * one on to look at it. What this adds is a pair of counting meshes per volume, sharing the
     * geometry.
     */
    collectShadowVolumes(root: THREE.Object3D): void {
        const found: THREE.Mesh[] = [];

        root.traverse((node) => {
            if (
                !(node instanceof THREE.Mesh) ||
                node.userData.aetShadowVolume === true
            ) {
                return;
            }

            const extras = (node.userData.alamo ?? {}) as MaterialExtras;

            if (resolveMaterial(extras).archetype === "shadow-volume") {
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

        // Which volumes count is not decided here: each casts by its own switches, written by the
        // rows of the subject and by a passive subject's own sweep - see `castsShadow`. The part
        // that brought these runs both before it is drawn.

        // The model can only be known to have a volume AFTER one is found, and the materials were
        // built before that - so the casting decision has to be taken again over everything.
        this.refreshShadowCasting();
    }

    /** Re-decides which meshes cast into the shadow map, now that the volumes are known. */
    private refreshShadowCasting(): void {
        this.context.assembly.forEachDrawnMesh((mesh) => {
            if (mesh.userData.aetShadowVolume === true) {
                return;
            }

            const extras = (mesh.userData.alamo ?? {}) as MaterialExtras;
            const spec = resolveMaterial(extras);

            mesh.castShadow = castsShadowMap({
                hasVolumes: this.stencilShadowing,
                hidden: spec.hidden,
                blend: spec.blend,
            });
        });
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
    renderFrame(): void {
        const bending = this.distorting();
        const bloom = this.bloomEnabled
            ? (this.bloom ??= new BloomPass())
            : null;

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
            this.heat.render(
                this.context.renderer,
                this.context.scene,
                this.context.camera,
                this.heatDebug,
            );
        } else {
            this.context.renderer.setRenderTarget(null);
            this.context.renderer.render(
                this.context.scene,
                this.context.camera,
            );
        }

        if (bloom !== null) {
            const frame = this.frameBuffer();

            this.context.renderer.copyFramebufferToTexture(frame);
            bloom.render(this.context.renderer, frame);
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
        this.context.renderer.getDrawingBufferSize(FRAME_SIZE);
        const width = Math.max(1, FRAME_SIZE.x);
        const height = Math.max(1, FRAME_SIZE.y);

        if (this.frameCopy === null) {
            this.frameCopy = new THREE.FramebufferTexture(width, height);
        } else if (
            this.frameCopy.image.width !== width ||
            this.frameCopy.image.height !== height
        ) {
            // It cannot be resized in place - the copy would land in a buffer of the wrong shape.
            this.frameCopy.dispose();
            this.frameCopy = new THREE.FramebufferTexture(width, height);
        }

        return this.frameCopy;
    }

    /** Whether any running system has a heat emitter on screen this frame. */
    private distorting(): boolean {
        // The debug view has to draw the buffer whether or not anything is currently bending it -
        // an empty neutral field IS the answer when nothing is.
        if (this.heatDebug && this.heatEnabled) {
            return true;
        }

        if (!this.heatEnabled || !this.context.particles.particlesVisible) {
            return false;
        }

        for (const entry of this.context.particles.particleSystems.values()) {
            if (entry.instance.hasVisibleHeat) {
                return true;
            }
        }

        return false;
    }
}

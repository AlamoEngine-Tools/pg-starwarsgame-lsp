// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Draws what particleSim.ts simulates.
//
// Kept apart from the simulation so the arithmetic stays testable: nothing here decides where a
// particle goes, only how it is put on screen.

import * as THREE from "three";

import type {
    AlamoEmitter,
    AlamoParticleContent,
    AlamoVector3,
} from "../../protocol/modelPreview";
import {
    type EmissionCursor,
    type EmissionMesh,
    emissionMultiplier,
    emissionPoint,
    offsetAlongNormal,
} from "./meshEmission";
import {
    accelerationIn,
    appearanceOf,
    atlasColumns,
    cubePhase,
    type Particle,
    type Random,
    seededRandom,
    SpawnClock,
    spawnParticle,
    stepParticle,
    systemExtent,
    weatherFade,
    wrapAxis,
} from "./particleSim";
import { AlamoMaterial } from "./alamoMaterial";
import type { TranslatedEffect } from "./fx/effect";
import type { FxMaterialState } from "./fx/renderState";
import { applyBlend, applyDepth } from "./materialState";
import { particleEffectName } from "./particleEffects";

/**
 * Ceiling on live particles per emitter.
 *
 * Measured in the game: an emitter's particle count - rate times lifetime, or the rate outright for
 * a weather field - is clamped to 5003 before its buffers are sized. The same ceiling here, so a
 * field authored for a whole battlefield draws as many drops as the game draws and no more. Oldest
 * are retired first on an ordinary emitter so the effect keeps running.
 */
const MAX_PARTICLES = 5003;

/** Reused so the per-frame ground lookup allocates nothing. */
const GROUND_SCRATCH = new THREE.Vector3();
const MOTION_SCRATCH = new THREE.Vector3();
const SPAWN_SCRATCH = new THREE.Vector3();
const SPACE_SCRATCH = new THREE.Matrix4();
const VIEW_SCRATCH = new THREE.Matrix4();
const IDENTITY = new THREE.Matrix4();
const CAMERA_SCRATCH = new THREE.Vector3();
const CAMERA_MATRIX_SCRATCH = new THREE.Matrix4();
const WEATHER_FORWARD_SCRATCH = new THREE.Vector3();
/** Measured: the scene wind enters a weather field's step at ten times the half it enters everything else at. */
const WEATHER_WIND_FACTOR = 5;
/** Where a weather cube sits when nothing is looking. */
const ORIGIN: AlamoVector3 = { x: 0, y: 0, z: 0 };

/**
 * Longest step the simulation will take.
 *
 * A backgrounded tab resumes with a multi-second delta; integrating that in one go teleports every
 * particle somewhere absurd. Clamping makes the effect briefly slow instead, which nobody notices.
 */
const MAX_STEP_SECONDS = 0.1;

/**
 * The step the game pre-simulates an emitter with, measured: at an emitter's first tick it is run
 * forward in tenths of a second until its clock is past its skip time.
 */
const PRESIMULATE_STEP_SECONDS = 0.1;

/** The blend state one Alamo mode needs, as three.js expresses it. */
export interface ParticleBlend {
    blending: THREE.Blending;
    depthWrite: boolean;
    /**
     * Whether the blend factors scale this mode's output by its own alpha.
     *
     * Only the `SRCALPHA` family does. The shader needs to know because it decides when a pixel
     * contributes nothing and can be discarded - on an additive sprite that is a question about
     * BRIGHTNESS, and testing alpha there throws away the whole effect.
     */
    alphaGated: boolean;
    /** `AlphaRef`, as a 0..1 threshold. Zero when the mode does not test alpha. */
    alphaTest: number;
    blendSrc?: THREE.BlendingSrcFactor;
    blendDst?: THREE.BlendingDstFactor;
    blendSrcAlpha?: THREE.BlendingSrcFactor;
    blendDstAlpha?: THREE.BlendingDstFactor;
}

/** `PrimParticleBumpAlpha.fx`: `AlphaTestEnable = TRUE; AlphaRef = 0x08`. */
const BUMP_ALPHA_REF = 8 / 255;

/**
 * How an Alamo blend mode maps onto what three.js can express.
 *
 * Read straight off the shipped `Engine/Prim*.fx` passes, which are the only authority on this - the
 * emitter names a blend MODE and the engine picks the shader from a table
 * (`engine_render.cpp:28-41`). Guessing from the mode's name cost three visible defects:
 *
 *   - Additive is `ONE / ONE`. Alpha does not scale it at all, and the emitters know it: Boba Fett's
 *     jetpack authors an alpha track of ZERO and is meant to be a bright orange flame. Blending it
 *     `SRC_ALPHA / ONE` drew nothing whatsoever, and his flamethrower (alpha 0.118) came out as a
 *     ghost. That is 588 of the corpus's 1019 emitters reading their brightness off the wrong
 *     channel.
 *   - Inverse is `ZERO / SRCCOLOR`, a multiply. It was mapped to three's SubtractiveBlending.
 *   - The alpha family is `SRCALPHA / INVSRCALPHA`, and the fragment shader was ALSO premultiplying
 *     by alpha, applying it twice.
 *
 * The alpha factors are spelled out everywhere rather than left to three's presets, which apply the
 * colour factors to alpha too. The canvas is created with `alpha: true` and composited over the
 * page, so accumulating alpha saturates the region to opaque and reveals the renderer's black clear
 * colour - a bright fireball came out ringed by dark squares, one per sprite quad.
 */
export function blendingFor(mode: string): ParticleBlend {
    const base = {
        blending: THREE.CustomBlending,
        depthWrite: false,
        alphaTest: 0,
    } as const;

    switch (mode) {
        // PrimAdditive.fx, PrimDepthSpriteAdditive.fx
        case "Additive":
        case "DepthAdditive":
            return {
                ...base,
                alphaGated: false,
                blendSrc: THREE.OneFactor,
                blendDst: THREE.OneFactor,
                blendSrcAlpha: THREE.ZeroFactor,
                blendDstAlpha: THREE.OneFactor,
            };

        // PrimModulate.fx, PrimDepthSpriteModulate.fx - a multiply, which darkens.
        case "Inverse":
        case "DepthInverse":
        case "StencilDarken":
        case "StencilDarkenBlur":
            return {
                ...base,
                alphaGated: false,
                blendSrc: THREE.ZeroFactor,
                blendDst: THREE.SrcColorFactor,
                blendSrcAlpha: THREE.ZeroFactor,
                blendDstAlpha: THREE.OneFactor,
            };

        // PrimDecalBumpAlpha.fx - `DESTCOLOR / SRCCOLOR`, a decal multiply.
        case "DecalBump":
            return {
                ...base,
                alphaGated: false,
                blendSrc: THREE.DstColorFactor,
                blendDst: THREE.SrcColorFactor,
                blendSrcAlpha: THREE.ZeroFactor,
                blendDstAlpha: THREE.OneFactor,
            };

        // PrimOpaque.fx - the one mode that is not blended at all, and the one that writes depth.
        case "None":
            return {
                blending: THREE.NoBlending,
                depthWrite: true,
                alphaGated: false,
                alphaTest: 0,
            };

        /*
         * A stand-in. `PrimHeat.fx` renders a DISTORTION - it writes the sprite's normals into a
         * buffer that warps the frame behind it, and contributes no colour of its own. There is no
         * cheap equivalent here, so the sprite is added as a faint shimmer scaled by its own alpha
         * rather than drawn as the grey-blue normal map it actually samples. No shipped emitter
         * selects this mode; the 127 heat particles in the corpus set the `isHeatParticle` FLAG on
         * an ordinary Transparent or Additive mode instead.
         */
        case "Heat":
            return {
                ...base,
                alphaGated: false,
                blendSrc: THREE.SrcAlphaFactor,
                blendDst: THREE.OneFactor,
                blendSrcAlpha: THREE.ZeroFactor,
                blendDstAlpha: THREE.OneFactor,
            };

        default:
            // PrimAlpha.fx and its siblings - Transparent, DepthTransparent, DiffuseTransparent,
            // Scanlines, Bump - plus anything this build does not model, which is likelier to be an
            // ordinary sprite than any of the exotic operators above.
            return {
                ...base,
                alphaGated: true,
                alphaTest: mode === "Bump" ? BUMP_ALPHA_REF : 0,
                // The one blended mode the engine lets write depth - `PrimParticleBumpAlpha.fx`
                // declares `ZWriteEnable = true` where every other Prim shader turns it off. It
                // can afford to, because it is alpha TESTED at `AlphaRef = 8`, so the depth it
                // writes belongs to the solid part of the sprite rather than to its whole quad.
                depthWrite: mode === "Bump",
                blendSrc: THREE.SrcAlphaFactor,
                blendDst: THREE.OneMinusSrcAlphaFactor,
                blendSrcAlpha: THREE.OneFactor,
                blendDstAlpha: THREE.OneMinusSrcAlphaFactor,
            };
    }
}

/**
 * The billboard shader.
 *
 * Quads rather than point sprites. Point sprites looked like the obvious fit - one vertex for a
 * camera-facing square - but they cannot do this job: `gl_PointSize` is clamped to the driver's
 * maximum, commonly a few hundred pixels, and Alamo sizes are world units that routinely exceed it.
 * p_explosion_huge01's smoke reaches a half-extent of 224, which clamps to a small grey square and
 * nothing else. Quads also rotate and clip properly, both of which point sprites fudge.
 *
 * Corners sit at +/- the size, matching the engine: alo-viewer builds its quad at `+/-p.size`
 * (`ParticleRenderers.cpp:70`), so size is a half-extent and the sprite is twice that across.
 */
const QUAD_VERTEX_SHADER = `
attribute vec2 corner;
attribute vec3 iOffset;
attribute vec3 iColor;
attribute float iAlpha;
attribute float iSize;
attribute float iRotation;
attribute float iFrame;
attribute float iStretch;

uniform float uWorldOriented;

varying vec2 vUv;
varying vec3 vColor;
varying float vAlpha;
varying float vFrame;

void main() {
    vUv = corner * 0.5 + 0.5;
    vColor = iColor;
    vAlpha = iAlpha;
    vFrame = iFrame;

    // A streak is this same quad with ONE corner pulled out behind the particle - the one on the
    // 135 degree diagonal, which the rotation below then swings opposite the direction of travel.
    // Pulled BEFORE the rotation, as the engine does, or the tail would point the wrong way.
    float trailing = step(corner.x, -0.5) * step(0.5, corner.y);
    vec2 pulled = corner * mix(1.0, iStretch, trailing);

    // Rotation is in turns.
    float angle = iRotation * 6.2831853;
    float s = sin(angle);
    float c = cos(angle);
    vec2 spun = vec2(pulled.x * c - pulled.y * s, pulled.x * s + pulled.y * c);

    if (uWorldOriented > 0.5) {
        // Flat on the ground rather than facing the camera - a shockwave ring, a scorch, the
        // wash under a repulsorlift. Alamo lays these in its own XY plane, which is XZ here
        // because the exporter turns its Z-up world into glTF's Y-up one.
        vec3 lying = iOffset + vec3(spun.x, 0.0, spun.y) * iSize;
        gl_Position = projectionMatrix * modelViewMatrix * vec4(lying, 1.0);
        return;
    }

    // Offset in VIEW space, which is what makes the quad face the camera whatever it does.
    vec4 mvPosition = modelViewMatrix * vec4(iOffset, 1.0);
    mvPosition.xy += spun * iSize;

    gl_Position = projectionMatrix * mvPosition;
}
`;

/**
 * The layer heat sprites draw on.
 *
 * A layer rather than a flag, because the ordinary pass has to skip them and the distortion pass
 * has to draw ONLY them - and a camera renders one layer mask, which is exactly that question. A
 * camera left at its default (layer 0) therefore never draws a heat sprite by accident, which is
 * what keeps every other code path here unchanged.
 */
export const HEAT_LAYER = 3;

/**
 * What a heat sprite contributes to the heat buffer.
 *
 * Not colour: `PrimHeat.fx` reads the sprite's texture as a NORMAL map and uses it to bend where
 * the frame behind is sampled from. The buffer therefore carries the normal in rgb and the
 * strength of the bend in alpha, and `heatPass.ts` does the bending.
 */
const HEAT_FRAGMENT_SHADER = `
precision mediump float;

varying vec2 vUv;
varying float vAlpha;
varying float vFrame;

uniform sampler2D uMap;
uniform float uHasMap;
uniform float uColumns;

void main() {
    if (uHasMap < 0.5) {
        discard;
    }

    float cell = 1.0 / uColumns;
    float index = mod(vFrame, uColumns * uColumns);
    vec2 origin = vec2(mod(index, uColumns) * cell, floor(index / uColumns) * cell);
    vec4 texel = texture2D(uMap, origin + vUv * cell);

    // attenuation = base_texel.a * In.Tex1.z, where that last is the particle's own alpha.
    gl_FragColor = vec4(texel.rgb, texel.a * vAlpha);
}
`;

const QUAD_FRAGMENT_SHADER = `
precision mediump float;

varying vec2 vUv;
varying vec3 vColor;
varying float vAlpha;
varying float vFrame;

uniform sampler2D uMap;
uniform float uHasMap;
uniform float uMissing;
uniform float uColumns;
uniform float uAlphaGated;
uniform float uAlphaTest;

void main() {
    vec4 texel = vec4(1.0);

    if (uHasMap > 0.5) {
        // The sheet is a grid of frames indexed top-down from 0, which is both how the engine
        // indexes them and how a DDS stores its rows.
        float cell = 1.0 / uColumns;
        float index = mod(vFrame, uColumns * uColumns);
        vec2 origin = vec2(mod(index, uColumns) * cell, floor(index / uColumns) * cell);
        texel = texture2D(uMap, origin + vUv * cell);
    }

    // MODULATE, exactly as the engine's texture stage: the sheet times the tracked colour, with NO
    // premultiplication by alpha. Whether alpha scales what lands on screen is the BLEND's business
    // - and on the additive modes it does not scale it at all.
    //
    // The missing-texture marker is the one thing that does NOT modulate. It has to read the same
    // whatever the emitter was going to tint it, and an emitter whose tracked colour reaches black
    // - which plenty do, as they fade out - would multiply the warning away to nothing.
    vec3 rgb = mix(vColor * texel.rgb, texel.rgb, uMissing);
    float alpha = texel.a * vAlpha;

    if (alpha < uAlphaTest) {
        discard;
    }

    // What this pixel would actually contribute. Testing alpha on an additive sprite would throw
    // away the effect: those emitters routinely author an alpha track of zero.
    float contribution = uAlphaGated > 0.5 ? alpha : max(max(rgb.r, rgb.g), rgb.b);

    if (contribution < 0.004) {
        discard;
    }

    gl_FragColor = vec4(rgb, alpha);
}
`;

/**
 * Where the emitter is this step, and how it got there.
 *
 * Particles are simulated in the MODEL's space rather than the bone's, because that is the only
 * space in which "did not follow" means anything: a sprite parented to a bone follows it whatever
 * the file says. Everything the simulation needs to know about the bone therefore arrives here.
 */
export interface EmitterFrame {
    /** Emitter space to simulation space, for placing a newborn particle. */
    matrix: THREE.Matrix4;
    /**
     * The same transform for a DIRECTION.
     *
     * Its own field because `Vector3.transformDirection` normalises, which would throw away every
     * particle's speed and leave a whole emitter drifting at one unit a second.
     */
    rotation: THREE.Matrix3;
    /** How far the emitter moved since the last step. */
    delta: THREE.Vector3;
    /** That movement as a speed, for the particles that inherit it. */
    velocity: THREE.Vector3;
    /** Simulation space to VIEW space, for the streak maths, which is a screen-space question. */
    toView: THREE.Matrix3;
    /**
     * Where the camera stands, in simulation space, or null when nothing is looking. A frozen
     * batch is sorted by distance from it.
     */
    camera: THREE.Vector3 | null;
    /** Where the scene's ground plane falls in simulation space. */
    groundY: number;
    /**
     * The wind, in simulation space: the viewer's wind setting. An emitter that declares itself
     * affected by it accelerates by half of it every frame; a weather field drifts at five times it.
     */
    wind: AlamoVector3;
}

/**
 * The plane a world-oriented quad lies in, as a basis for the streak maths.
 *
 * Alamo lays these in its world XY plane; the exporter turns its Z-up world into glTF's Y-up one,
 * so that plane is XZ here. The second row picks out Z as the quad's own vertical, matching the
 * `vec3(spun.x, 0.0, spun.y)` the vertex shader builds - if the two disagreed the tail would trail
 * off at right angles to the direction of travel.
 */
const GROUND_BASIS = new THREE.Matrix3().set(1, 0, 0, 0, 0, 1, 0, 0, 0);

/**
 * How much of the emitter's movement a particle is carried along by.
 *
 * Two independent settings, and the shipped data never uses both - across 1019 emitters, 168 set
 * `linkToSystem` and 316 a non-zero `parentLinkStrength`, with no overlap - so summing them cannot
 * double-count. `EmitterTranslaterPlugin` is the first (all or nothing); `EmitterInstance.cpp:545`
 * is the second, a fraction.
 */
export function followFactor(properties: AlamoEmitter["properties"]): number {
    return (properties.linkToSystem ? 1 : 0) + properties.parentLinkStrength;
}

/** What a streak's trailing corner does this frame. */
export interface TailGeometry {
    /** Multiplier on the trailing corner's distance from the centre. Never below 1. */
    stretch: number;
    /** Where to point it, in turns - the whole quad rotates by this. */
    turns: number;
}

/**
 * The kite a tailed emitter draws instead of a square.
 *
 * 52 of the corpus's emitters set `hasTail`: sparks, tracer fire, the debris off an interdictor.
 * The engine keeps the quad and pulls ONE corner out behind the particle rather than building
 * different geometry (`EmitterInstance.cpp:625-640`), which is why the same four vertices can do
 * both jobs.
 *
 * `velocity` arrives in VIEW space and `speed` is its length in the space it was measured in - the
 * ratio between the two is what shortens the streak of a particle flying at the camera to nothing.
 * A tailed emitter also ignores its rotation track: the direction of travel decides the angle.
 */
export function tailGeometry(
    properties: AlamoEmitter["properties"],
    velocity: AlamoVector3,
    speed: number,
): TailGeometry | null {
    if (!properties.hasTail) {
        return null;
    }

    if (speed <= 0) {
        return { stretch: 1, turns: 0 };
    }

    // An inherited-motion emitter measures its tail against a kilometre a second, so a spark that
    // is merely keeping up with its hull barely streaks at all.
    const mult = properties.parentLinkStrength !== 0 ? speed / 1000 : 1;
    const planar = Math.hypot(velocity.x, velocity.y);
    const length = (properties.tailSize * mult * planar) / speed;

    return {
        stretch: Math.max(1, length / Math.SQRT2),
        // The corner being pulled sits on the quad's 135 degree diagonal, so the extra eighth of a
        // turn lands it opposite the direction of travel.
        turns:
            (Math.atan2(velocity.y, velocity.x) + Math.PI / 4) / (Math.PI * 2),
    };
}

/** The four corners of a sprite, in the index order the quad's two triangles read them. */
const QUAD_CORNERS = [-1, -1, 1, -1, 1, 1, -1, 1];

/**
 * The sampler names the engine's particle effects read the colour sheet and the second texture
 * through. The second texture is one chunk of the file: the bump effects read it as the normal map,
 * the depth sprites as the depth map.
 */
const COLOUR_SAMPLERS = ["TextureSampler", "BaseSampler"];
const SECOND_TEXTURE_SAMPLERS = ["NormalSampler", "DepthSampler"];

/**
 * An emitter drawn through the engine's own effect: the translated program, the pass's render
 * state, and a vertex stream of CPU-built quads sized for the particle ceiling.
 */
class EngineDraw {
    readonly geometry = new THREE.BufferGeometry();
    readonly material: AlamoMaterial;
    readonly positions = new Float32Array(MAX_PARTICLES * 4 * 3);
    readonly colours = new Float32Array(MAX_PARTICLES * 4 * 4);
    readonly uvs = new Float32Array(MAX_PARTICLES * 4 * 2);

    constructor(
        effect: TranslatedEffect,
        state: FxMaterialState,
        noDepthTest: boolean,
    ) {
        // Position, colour, uv: the engine's own particle vertex. The bump effects declare a normal
        // input their vertex shader never reads; it is fed zeros so the attribute is not missing.
        this.geometry.setAttribute(
            "position",
            new THREE.BufferAttribute(this.positions, 3),
        );
        this.geometry.setAttribute(
            "color",
            new THREE.BufferAttribute(this.colours, 4),
        );
        this.geometry.setAttribute(
            "uv",
            new THREE.BufferAttribute(this.uvs, 2),
        );
        this.geometry.setAttribute(
            "normal",
            new THREE.BufferAttribute(
                new Float32Array(MAX_PARTICLES * 4 * 3),
                3,
            ),
        );
        const index = new Uint32Array(MAX_PARTICLES * 6);
        for (let i = 0; i < MAX_PARTICLES; i++) {
            const v = i * 4;
            index.set([v, v + 1, v + 2, v, v + 2, v + 3], i * 6);
        }
        this.geometry.setIndex(new THREE.BufferAttribute(index, 1));
        this.geometry.setDrawRange(0, 0);

        // No sub-mesh behind a particle, so no parameters of its own: the effect's declared
        // defaults stand, which for the bump effect is white diffuse and no emissive.
        this.material = new AlamoMaterial(effect, {});
        applyDepth(this.material, state);
        applyBlend(this.material, state.blend);
        this.material.setAlphaTest(state.alphaTest);
        // Measured: the particle draw passes cull NONE whatever the pass declares, and the
        // no-depth-test flag turns the depth test off on the live path.
        this.material.side = THREE.DoubleSide;
        this.material.depthTest = state.depthTest && !noDepthTest;
        this.material.needsUpdate = true;
    }

    /** Hands the colour sheet and the second texture to whichever samplers the effect declares. */
    bindTextures(
        colour: THREE.Texture | null,
        second: THREE.Texture | null,
    ): void {
        for (const sampler of COLOUR_SAMPLERS) {
            this.material.uniforms[sampler] = { value: colour };
        }
        for (const sampler of SECOND_TEXTURE_SAMPLERS) {
            this.material.uniforms[sampler] = { value: second };
        }
    }

    /** Marks the first `count` quads written and ready to draw. */
    written(count: number): void {
        this.geometry.setDrawRange(0, count * 6);
        for (const name of ["position", "color", "uv"]) {
            this.geometry.attributes[name].needsUpdate = true;
        }
    }

    dispose(): void {
        this.geometry.dispose();
        this.material.dispose();
    }
}

/** An emitter that neither moves nor is moved, for a system standing on its own. */
function stillFrame(): EmitterFrame {
    return {
        matrix: new THREE.Matrix4(),
        rotation: new THREE.Matrix3(),
        toView: new THREE.Matrix3(),
        camera: null,
        delta: new THREE.Vector3(),
        velocity: new THREE.Vector3(),
        groundY: 0,
        wind: { x: 0, y: 0, z: 0 },
    };
}

/** One emitter's live particles and the geometry drawing them. */
class EmitterRenderer {
    readonly mesh: THREE.Mesh;

    private readonly particles: Particle[] = [];
    private readonly clock: SpawnClock;
    private readonly geometry: THREE.InstancedBufferGeometry;
    private readonly material: THREE.ShaderMaterial;

    /**
     * The engine's own effect, once translated and switched on: CPU-built quads through the
     * effect's program, as the game draws them. Null draws with the preview's shader above.
     */
    private engine: EngineDraw | null = null;
    private normalTexture: THREE.Texture | null = null;

    private readonly offsets = new Float32Array(MAX_PARTICLES * 3);
    private readonly colors = new Float32Array(MAX_PARTICLES * 3);
    private readonly alphas = new Float32Array(MAX_PARTICLES);
    private readonly sizes = new Float32Array(MAX_PARTICLES);
    private readonly rotations = new Float32Array(MAX_PARTICLES);
    private readonly frames = new Float32Array(MAX_PARTICLES);
    private readonly stretches = new Float32Array(MAX_PARTICLES).fill(1);

    /** Reused per particle by the kite maths, so a thousand sparks allocate nothing. */
    private readonly viewVelocity = new THREE.Vector3();

    /** Seconds this emitter has run, the clock its freeze time is measured on. */
    private age = 0;

    /**
     * Set once the freeze time has passed. From then on the particles are a baked batch: nothing
     * steps, spawns, ages or dies, and a parent drives no more trails off them.
     */
    private frozen = false;
    /**
     * Set by a material switch: the buffer now drawn is empty or stale and the next tick writes
     * it, even for a frozen bake that would otherwise never be written again.
     */
    private rewrite = false;
    /**
     * The view basis the effect buffer's corners were last built against, or null before any
     * were.
     *
     * Measured in the game: the corner table is rotated into camera space once per camera change,
     * and a frozen particle's corners are built from it at every draw. The bake is the particle
     * STATE, never the vertex stream - a frozen sprite faces the camera wherever it moves. The
     * effect buffer holds world positions with the basis already applied, so it is written again
     * whenever the basis moves, at the engine's own cadence: once per camera change, nothing while
     * the camera rests. The preview's own buffer needs none of this: it billboards in the shader.
     */
    private bakedBasis: THREE.Matrix3 | null = null;

    /** The frozen batch's draw order and the depths it was sorted on, reused every frame. */
    private readonly drawIndex: number[] = [];
    private readonly depths = new Float32Array(MAX_PARTICLES);

    /** Where `EveryVertex` has got to. Kept across ticks, because that walk is ordered. */
    private readonly cursor: EmissionCursor = { subMesh: 0, vertex: 0 };

    /**
     * Half the declared cube edge for a weather emitter, zero for every other kind.
     *
     * Measured in the game: a weather field lives in a periodic space around the emitter's origin
     * with the cube edge as its period, every particle born at once and none ever dying; what is
     * drawn is the copy inside the cube around the point the camera offset in front of the camera.
     */
    private readonly weatherHalf: number;

    /** Whether the field has had its one and only burst. */
    private weatherBorn = false;

    /** The cube's centre and its phase within the period cell, computed once per write. */
    private readonly weatherCentre = { x: 0, y: 0, z: 0 };
    private weatherPhase = { x: 0, y: 0, z: 0 };

    /** The rendered copy of the particle being written, reused so a field allocates nothing. */
    private readonly weatherDrawn = { x: 0, y: 0, z: 0 };

    constructor(
        private readonly emitter: AlamoEmitter,
        private readonly random: Random,
        private texture: THREE.Texture | null,
        /** The geometry this emitter is born from, when it emits from a mesh rather than a shape. */
        private readonly emissionMesh: EmissionMesh | null = null,
        /** The owning object's uniform render scale. See {@link ParticleSystemInstance}. */
        private readonly scaleFactor = 1,
    ) {
        this.clock = new SpawnClock(emitter);
        // The file carries the full edge; the game halves it on the emitter's reset.
        this.weatherHalf = emitter.properties.isWeatherParticle
            ? Math.max(0, emitter.properties.weatherCubeSize) * 0.5
            : 0;

        this.geometry = new THREE.InstancedBufferGeometry();
        this.geometry.setAttribute(
            "corner",
            new THREE.BufferAttribute(
                new Float32Array([-1, -1, 1, -1, 1, 1, -1, 1]),
                2,
            ),
        );
        this.geometry.setIndex([0, 1, 2, 0, 2, 3]);

        this.geometry.setAttribute(
            "iOffset",
            new THREE.InstancedBufferAttribute(this.offsets, 3),
        );
        this.geometry.setAttribute(
            "iColor",
            new THREE.InstancedBufferAttribute(this.colors, 3),
        );
        this.geometry.setAttribute(
            "iAlpha",
            new THREE.InstancedBufferAttribute(this.alphas, 1),
        );
        this.geometry.setAttribute(
            "iSize",
            new THREE.InstancedBufferAttribute(this.sizes, 1),
        );
        this.geometry.setAttribute(
            "iRotation",
            new THREE.InstancedBufferAttribute(this.rotations, 1),
        );
        this.geometry.setAttribute(
            "iFrame",
            new THREE.InstancedBufferAttribute(this.frames, 1),
        );
        this.geometry.setAttribute(
            "iStretch",
            new THREE.InstancedBufferAttribute(this.stretches, 1),
        );
        this.geometry.instanceCount = 0;

        const heat = emitter.properties.isHeatParticle;

        // A heat sprite writes a normal and a strength into the heat buffer, where sprites simply
        // blend over one another - the blend mode the emitter names describes the colour it would
        // have contributed, and it contributes none.
        const { alphaGated, alphaTest, ...blendState } = heat
            ? blendingFor("Transparent")
            : blendingFor(emitter.properties.blendMode);

        this.material = new THREE.ShaderMaterial({
            ...blendState,
            uniforms: {
                uMap: { value: texture },
                uHasMap: { value: texture === null ? 0 : 1 },
                uMissing: { value: 0 },
                uColumns: {
                    value: atlasColumns(emitter.properties.textureSize),
                },
                uAlphaGated: { value: alphaGated ? 1 : 0 },
                uAlphaTest: { value: alphaTest },
                uWorldOriented: {
                    value: emitter.properties.worldOriented ? 1 : 0,
                },
            },
            vertexShader: QUAD_VERTEX_SHADER,
            fragmentShader: heat ? HEAT_FRAGMENT_SHADER : QUAD_FRAGMENT_SHADER,
            transparent: true,
            depthTest: !emitter.properties.noDepthTest,
            side: THREE.DoubleSide,
        });

        this.mesh = new THREE.Mesh(this.geometry, this.material);

        if (heat) {
            // Off the ordinary pass entirely and onto the distortion one. `set` rather than
            // `enable`: a sprite drawn in both passes would add its normal map to the picture as
            // well as bending it, which is the flat violet sheet this replaced.
            this.mesh.layers.set(HEAT_LAYER);
        }

        // The quads are built in the vertex shader, so the geometry's own bounds describe a unit
        // square at the origin and would cull the whole cloud the moment the camera looked away.
        this.mesh.frustumCulled = false;
        this.mesh.name = emitter.name;
    }

    /** The emitter this renderer draws, for callers that need its numbers rather than its pixels. */
    get description(): AlamoEmitter {
        return this.emitter;
    }

    /** The texture this emitter is waiting for, so the caller knows what to fetch. */
    get textureName(): string {
        return this.emitter.colorTexture;
    }

    /** True once the emitter has finished and its last particle has died. */
    get exhausted(): boolean {
        return this.clock.finished && this.particles.length === 0;
    }

    /** The live particles, for the tests and diagnostics that need to read the simulation. */
    get live(): readonly Particle[] {
        return this.particles;
    }

    /** True once the emitter's freeze time has passed and its particles are baked. */
    get isFrozen(): boolean {
        return this.frozen;
    }

    /**
     * How fast a child emits, per parent particle.
     *
     * The engine gives every parent particle its OWN emitter instance, so the rate is per particle
     * and not shared - a hundred sparks each drag a full trail.
     */
    get rate(): number {
        const properties = this.emitter.properties;

        return properties.useBursts
            ? properties.particlesPerBurst /
                  Math.max(properties.burstDelay, 1e-3)
            : properties.particlesPerSecond;
    }

    /** A whole burst of it, for a death child, which the engine fires as one. */
    get burstSize(): number {
        return Math.max(1, this.emitter.properties.particlesPerBurst);
    }

    /**
     * Supplies the texture once it arrives.
     *
     * Textures are fetched after the system, because the system is what names them. Until then the
     * emitter draws untextured rather than not at all - an untextured puff still shows the motion,
     * which is most of what the preview is for.
     */
    setTexture(texture: THREE.Texture): void {
        this.texture = texture;
        this.material.uniforms.uMap.value = texture;
        this.material.uniforms.uHasMap.value = 1;
        this.material.uniforms.uMissing.value = 0;
        this.material.needsUpdate = true;
        this.engine?.bindTextures(texture, this.normalTexture);
    }

    /** The normal map a bump emitter names, or an empty string. */
    get normalTextureName(): string {
        return this.emitter.normalTexture ?? "";
    }

    /** Supplies the normal map once it arrives; only the engine's bump effects read it. */
    setNormalTexture(texture: THREE.Texture): void {
        this.normalTexture = texture;
        this.engine?.bindTextures(this.texture, texture);
    }

    /**
     * The engine effect this emitter is drawn with, to be fetched like any mesh effect - or null
     * for a heat sprite, which the distortion pass draws with the preview's own program.
     */
    get effectName(): string | null {
        return this.emitter.properties.isHeatParticle
            ? null
            : particleEffectName(this.emitter.properties.blendMode);
    }

    /**
     * Draws through the engine's own effect, or goes back to the preview's shader with null.
     *
     * Measured in the game: the particle renderer binds the effect its render mode names and
     * submits CPU-built quads - position, a packed colour, uv - which the effect's vertex shader
     * only projects. Two kinds keep the preview's shader: a heat sprite (its own pass) and a
     * tailed emitter (the streak is built in the preview's vertex shader). A depth sprite's effect
     * reads the emitter's own depth map through its second sampler and writes the offset depth.
     */
    applyEffect(
        effect: TranslatedEffect | null,
        state: FxMaterialState | null,
    ): void {
        const properties = this.emitter.properties;
        const eligible = !properties.isHeatParticle && !properties.hasTail;

        // Whichever buffer the switch lands on is empty or stale, and a frozen bake is not written
        // again on its own: the next tick writes it, sorted or not.
        this.rewrite = true;

        if (effect === null || state === null || !eligible) {
            this.engine?.dispose();
            this.engine = null;
            this.mesh.geometry = this.geometry;
            this.mesh.material = this.material;
            return;
        }

        this.engine?.dispose();
        this.engine = new EngineDraw(effect, state, properties.noDepthTest);
        this.engine.bindTextures(this.texture, this.normalTexture);
        this.mesh.geometry = this.engine.geometry;
        this.mesh.material = this.engine.material;
    }

    /**
     * Whether the effect buffer's corners face a camera that is no longer there.
     *
     * Asked of the effect buffer only - the preview's own billboards in the shader - and never of
     * a world-oriented emitter, whose corners lie in the ground plane whatever the camera does.
     * Compared exactly: a resting camera gives the same basis to the bit, so nothing is written
     * while it rests.
     */
    private basisMoved(frame: EmitterFrame): boolean {
        if (this.engine === null || this.emitter.properties.worldOriented) {
            return false;
        }

        const baked = this.bakedBasis;
        if (baked === null) {
            return true;
        }

        const a = baked.elements;
        const b = frame.toView.elements;
        for (let i = 0; i < 9; i++) {
            if (a[i] !== b[i]) {
                return true;
            }
        }

        return false;
    }

    /**
     * The engine's vertex stream: four corners per particle, built the way the engine builds them.
     *
     * Measured: corner = position + the camera-rotated corner offset times the size, the colour
     * packed to bytes from the tracked colour and alpha - and for the bump effect the RGB replaced
     * by the sprite's rotation tangent in VIEW space, encoded `0.5 + 0.5 * t`, the alpha kept. The
     * uv is the frame's cell of the sheet. The effect's vertex shader projects and nothing more.
     */
    private writeQuads(
        engine: EngineDraw,
        frame: EmitterFrame,
        order: number[] | null,
    ): void {
        const properties = this.emitter.properties;
        const bump =
            properties.blendMode === "Bump" ||
            properties.blendMode === "DecalBump";
        const columns = atlasColumns(properties.textureSize);
        const cell = 1 / columns;
        const scale = this.scaleFactor;
        const weather = this.weatherHalf > 0;
        const fadeOnColour = properties.blendMode === "Additive";
        const basis = properties.worldOriented ? GROUND_BASIS : frame.toView;
        // Copied, not kept: the frame is one object the system rewrites every tick.
        this.bakedBasis = (this.bakedBasis ?? new THREE.Matrix3()).copy(basis);
        // Simulation space to view space is a rotation, so the view axes in simulation space are
        // its rows - which is what the engine's camera-rotated corner table holds.
        const e = basis.elements;
        const rightX = e[0];
        const rightY = e[3];
        const rightZ = e[6];
        const upX = e[1];
        const upY = e[4];
        const upZ = e[7];
        const { positions, colours, uvs } = engine;

        for (let i = 0; i < this.particles.length; i++) {
            const particle = this.particles[order === null ? i : order[i]];
            const look = appearanceOf(particle, this.emitter);
            const size = look.size * scale;
            const angle = look.rotation * 6.2831853;
            const c = Math.cos(angle);
            const s = Math.sin(angle);
            // A weather particle is drawn as its copy in the cube around the camera, faded by the
            // game's distance on the colour of an additive emitter and the alpha otherwise.
            const fade = weather ? this.weatherDrawOf(particle) : 1;
            const drawn = weather ? this.weatherDrawn : particle.position;
            const colourFade = fadeOnColour ? fade : 1;
            const alphaFade = fadeOnColour ? 1 : fade;
            const px = drawn.x * scale;
            const py = drawn.y * scale;
            const pz = drawn.z * scale;
            const index = look.frame % (columns * columns);
            const originU = (index % columns) * cell;
            const originV = Math.floor(index / columns) * cell;
            const r = bump ? 0.5 + 0.5 * c : look.r * colourFade;
            const g = bump ? 0.5 + 0.5 * s : look.g * colourFade;
            const b = bump ? 0.5 : look.b * colourFade;
            const a = look.a * alphaFade;

            for (let corner = 0; corner < 4; corner++) {
                const cx = QUAD_CORNERS[corner * 2];
                const cy = QUAD_CORNERS[corner * 2 + 1];
                const spunX = cx * c - cy * s;
                const spunY = cx * s + cy * c;
                const vertex = i * 4 + corner;
                positions[vertex * 3] =
                    px + (rightX * spunX + upX * spunY) * size;
                positions[vertex * 3 + 1] =
                    py + (rightY * spunX + upY * spunY) * size;
                positions[vertex * 3 + 2] =
                    pz + (rightZ * spunX + upZ * spunY) * size;
                colours[vertex * 4] = r;
                colours[vertex * 4 + 1] = g;
                colours[vertex * 4 + 2] = b;
                colours[vertex * 4 + 3] = a;
                uvs[vertex * 2] = originU + (cx * 0.5 + 0.5) * cell;
                uvs[vertex * 2 + 1] = originV + (cy * 0.5 + 0.5) * cell;
            }
        }

        engine.written(this.particles.length);
    }

    /**
     * Shows the marker instead, because this emitter's texture does not resolve.
     *
     * `uColumns` goes back to one as well. The marker is a single image, and an emitter that
     * declares a 4x4 sprite sheet would otherwise sample a sixteenth of it - a corner of a logo,
     * which is not a signal anyone can read.
     */
    setMissingTexture(texture: THREE.Texture): void {
        this.material.uniforms.uMap.value = texture;
        this.material.uniforms.uHasMap.value = 1;
        this.material.uniforms.uMissing.value = 1;
        this.material.uniforms.uColumns.value = 1;
        this.material.needsUpdate = true;
    }

    /**
     * Advances the emitter a step, and reports where its particles died.
     *
     * `dependent` marks an emitter that another one spawns - a trail or a death child. Those must
     * not run their own clock: the engine only ever creates them from the parent, and letting one
     * emit on its own put an explosion's smoke trail at the blast's centre as a puff.
     */
    update(
        dt: number,
        frame: EmitterFrame = stillFrame(),
        dependent = false,
    ): AlamoVector3[] {
        // A weather field runs the game's other update: one burst, no deaths, a wrap each step.
        if (this.weatherHalf > 0) {
            return this.advanceWeather(dt, frame);
        }

        // Measured in the game: on the first tick that would carry the emitter past its freeze
        // time it bakes the particles it has - as they stand BEFORE that step - and never steps,
        // spawns, ages or fades again. A tick landing exactly on the freeze time still steps.
        // Every asteroid field, the junk fields and the galaxy's dust freeze this way; it is what
        // makes them a scene rather than a shower.
        const freezeTime = this.emitter.properties.freezeTime;
        if (this.frozen || (freezeTime > 0 && this.age + dt > freezeTime)) {
            this.frozen = true;
            // Measured: the no-depth-test flag is read on the live path only. A frozen batch is
            // always tested against the depth buffer, whatever the file asked for.
            this.material.depthTest = true;
            if (this.engine !== null) {
                this.engine.material.depthTest = true;
            }
            // The bake is drawn as it was written. It is written again when the camera it faces
            // has moved, when it is depth-sorted and so follows the camera every frame - or once,
            // into a buffer that a material switch has just left empty. Measured on the asteroid
            // field: its effect arrives after the freeze, and a bump field is never sorted, so the
            // fresh effect buffer held nothing and the field vanished.
            if (
                this.rewrite ||
                this.basisMoved(frame) ||
                (frame.camera !== null && this.sortsFrozenByDepth())
            ) {
                this.writeBuffers(frame);
                this.rewrite = false;
            }
            return [];
        }

        // Measured: at its first tick an emitter with a skip time is run forward, in tenths of a
        // second, until its clock is past that time - then the tick's own step follows. Every
        // freezing field sets skip equal to freeze, so the game shows an asteroid field populated
        // from its first frame and frozen on its second: a scene, never a shower filling up.
        // Deaths during the pre-simulation go unreported; nothing is watching yet.
        if (this.age === 0) {
            const skip = this.emitter.properties.skipTime;
            while (skip > 0 && this.age <= skip) {
                this.advance(PRESIMULATE_STEP_SECONDS, frame, dependent, false);
            }
        }

        return this.advance(dt, frame, dependent, true);
    }

    /** One step of the live simulation: age, move, kill, spawn, and write the buffers if asked. */
    private advance(
        dt: number,
        frame: EmitterFrame,
        dependent: boolean,
        write: boolean,
    ): AlamoVector3[] {
        this.age += dt;

        const follow = followFactor(this.emitter.properties);
        const acceleration = this.accelerationIn(frame);
        const deaths: AlamoVector3[] = [];

        for (let i = this.particles.length - 1; i >= 0; i--) {
            const particle = this.particles[i];

            if (
                !stepParticle(
                    particle,
                    this.emitter,
                    dt,
                    frame.groundY,
                    acceleration,
                    frame.wind,
                )
            ) {
                deaths.push(particle.position);

                // Swap-remove: draw order does not matter here, and splice on a few thousand
                // particles every frame does.
                this.particles[i] = this.particles[this.particles.length - 1];
                this.particles.pop();
                continue;
            }

            // Carried along by the emitter, in whole or in part. Adding the step's delta as it
            // happens comes to the same total the engine reaches by holding the position the
            // emitter had at the particle's BIRTH and taking the difference every frame.
            if (follow !== 0) {
                particle.position.x += frame.delta.x * follow;
                particle.position.y += frame.delta.y * follow;
                particle.position.z += frame.delta.z * follow;
            }
        }

        // `EveryVertex` asks for its whole count PER VERTEX, which is what turns one system into
        // an emitter at every point of a mesh - `pe_nebulonengines` is 2 a second across every
        // vertex of the Nebulon-B's engine block, not 2 a second at its origin.
        const mode = this.emitter.properties.emitFromMesh;

        // An emitter nobody is drawing does not spawn, and its clock is held with it - the same
        // rule the system as a whole follows, one level down. Unticking one emitter in the dock
        // otherwise let it fill its buffer unseen and hand the reader a burst that was over.
        const wanted =
            dependent || !this.mesh.visible
                ? 0
                : this.clock.advance(dt) *
                  emissionMultiplier(mode, this.emissionMesh);

        for (let i = 0; i < wanted; i++) {
            if (this.particles.length >= MAX_PARTICLES) {
                this.particles.shift();
            }

            const particle = spawnParticle(this.emitter, this.random);

            // Born ON the mesh rather than inside the emitter's shape volume. The shape still
            // decides the VELOCITY; only the position moves.
            if (mode !== "Disabled" && this.emissionMesh !== null) {
                const point = emissionPoint(
                    mode,
                    this.emissionMesh,
                    this.cursor,
                    this.random,
                );

                if (point !== null) {
                    particle.position = offsetAlongNormal(
                        point,
                        this.emitter.properties.emitFromMeshOffset,
                    );
                }
            }

            this.place(particle, frame);
            this.particles.push(particle);
        }

        if (write) {
            this.writeBuffers(frame);
        }

        return deaths;
    }

    /**
     * Emits from a particle of another emitter, which is how a trail and a death child are made.
     *
     * The birth point is the PARENT particle rather than the emitter's own origin -
     * `ShapeCreatorPlugin::InitializeParticle` adds `parent->position - transform.getTranslation()`
     * for exactly this - so an explosion's sparks each drag their own smoke rather than the system
     * puffing once at the middle.
     */
    emitFrom(at: AlamoVector3, count: number, frame: EmitterFrame): void {
        // Hidden means not running here too: a trail is spawned by its parent rather than by its
        // own clock, so the check in `update` never sees it. Frozen likewise - a baked batch takes
        // nothing more in.
        if (!this.mesh.visible || this.frozen) {
            return;
        }

        for (let i = 0; i < count; i++) {
            if (this.particles.length >= MAX_PARTICLES) {
                this.particles.shift();
            }

            const particle = spawnParticle(this.emitter, this.random);
            this.place(particle, frame);

            // The shape volume still decides the SPREAD; the parent decides where that spread is
            // centred, and it is already in simulation space.
            particle.position = {
                x: particle.position.x + at.x - frame.matrix.elements[12],
                y: particle.position.y + at.y - frame.matrix.elements[13],
                z: particle.position.z + at.z - frame.matrix.elements[14],
            };

            this.particles.push(particle);
        }
    }

    /** Writes what the last few `emitFrom` calls added. Kept apart so a trail writes once a frame. */
    flush(frame: EmitterFrame): void {
        this.writeBuffers(frame);
    }

    dispose(): void {
        this.geometry.dispose();
        this.material.dispose();
        this.engine?.dispose();
        this.engine = null;
    }

    /**
     * This step's acceleration, in the space the simulation runs in.
     *
     * `objectSpaceAcceleration` makes the vector the EMITTER's own rather than the world's, and the
     * engine re-reads it through the emitter's transform every frame
     * (`AccelerationModifierPlugin::ModifyParticle`) rather than fixing it at spawn - so a ship
     * that turns takes its engine wash with it. 31 emitters set the flag, 18 with a real
     * acceleration, and they are almost all `Pte_*engines` pushing out of the nozzle.
     *
     * Gravity stays in world space either way. No shipped object-space emitter sets one, so the
     * question of whether the engine would tilt it never arises in practice.
     */
    private accelerationIn(frame: EmitterFrame): AlamoVector3 {
        const properties = this.emitter.properties;

        if (!properties.objectSpaceAcceleration) {
            return accelerationIn(properties);
        }

        SPAWN_SCRATCH.set(
            properties.acceleration.x,
            properties.acceleration.y,
            properties.acceleration.z,
        ).applyMatrix3(frame.rotation);

        return {
            x: SPAWN_SCRATCH.x,
            y: SPAWN_SCRATCH.y - properties.gravity,
            z: SPAWN_SCRATCH.z,
        };
    }

    /**
     * Moves a newborn particle out of the emitter's frame and into the simulation's.
     *
     * Both the position and the direction it was thrown in: `ShapeCreatorPlugin::InitializeParticle`
     * multiplies each by the emitter's transform, which is what aims an engine wash aft rather than
     * along the model's own axes.
     */
    private place(particle: Particle, frame: EmitterFrame): void {
        SPAWN_SCRATCH.set(
            particle.position.x,
            particle.position.y,
            particle.position.z,
        ).applyMatrix4(frame.matrix);
        particle.position = {
            x: SPAWN_SCRATCH.x,
            y: SPAWN_SCRATCH.y,
            z: SPAWN_SCRATCH.z,
        };

        // The direction it was born in travels with the velocity: the inward pull is measured from
        // it for the particle's whole life, and half the corpus's emitters use one.
        SPAWN_SCRATCH.set(
            particle.inward.x,
            particle.inward.y,
            particle.inward.z,
        ).applyMatrix3(frame.rotation);
        particle.inward = {
            x: SPAWN_SCRATCH.x,
            y: SPAWN_SCRATCH.y,
            z: SPAWN_SCRATCH.z,
        };

        SPAWN_SCRATCH.set(
            particle.velocity.x,
            particle.velocity.y,
            particle.velocity.z,
        ).applyMatrix3(frame.rotation);

        // `parentLinkStrength` again, in its other role: a particle is born already carrying its
        // share of the emitter's own motion, so debris thrown off a moving hull keeps up with it.
        const inherited = this.emitter.properties.parentLinkStrength;

        particle.velocity = {
            x: SPAWN_SCRATCH.x + frame.velocity.x * inherited,
            y: SPAWN_SCRATCH.y + frame.velocity.y * inherited,
            z: SPAWN_SCRATCH.z + frame.velocity.z * inherited,
        };
    }

    /**
     * One step of a weather field.
     *
     * Measured in the game, which runs a weather emitter through an update of its own: every
     * particle is born at once - the file's per-second value is the count outright, lifetime
     * playing no part - uniformly inside the cube, with a random age; no gravity, no inward pull,
     * no ground and no later spawn; an age past the lifetime wraps round, so nothing ever dies;
     * and after the position moves it is wrapped back into the cube, axis by axis. Positions are
     * emitter-local throughout - the emitter's own transform is never applied, the field is placed
     * at draw time around the camera instead.
     */
    private advanceWeather(dt: number, frame: EmitterFrame): AlamoVector3[] {
        this.age += dt;
        const half = this.weatherHalf;

        if (!this.weatherBorn && this.mesh.visible) {
            const count = Math.min(
                MAX_PARTICLES,
                Math.max(
                    0,
                    Math.ceil(this.emitter.properties.particlesPerSecond),
                ),
            );
            for (let i = 0; i < count; i++) {
                // No wind at birth: a field takes the wind in its step, below.
                const particle = spawnParticle(this.emitter, this.random);
                particle.position = {
                    x: (this.random() * 2 - 1) * half,
                    y: (this.random() * 2 - 1) * half,
                    z: (this.random() * 2 - 1) * half,
                };
                particle.age = this.random() * particle.lifetime;
                this.particles.push(particle);
            }
            this.weatherBorn = true;
        }

        // Measured: a field that declares itself affected by wind has FIVE times the scene's
        // wind added to its step, every frame, as a drift - the stored velocity is never touched,
        // and nothing is added at birth. The preview's scene wind is the viewer's wind setting.
        const blown = this.emitter.properties.affectedByWind
            ? frame.wind
            : ORIGIN;
        const driftX = blown.x * WEATHER_WIND_FACTOR;
        const driftY = blown.y * WEATHER_WIND_FACTOR;
        const driftZ = blown.z * WEATHER_WIND_FACTOR;

        for (const particle of this.particles) {
            particle.age += dt;
            if (particle.lifetime > 0 && particle.age > particle.lifetime) {
                particle.age %= particle.lifetime;
            }

            particle.position.x = wrapAxis(
                particle.position.x + (particle.velocity.x + driftX) * dt,
                half,
            );
            particle.position.y = wrapAxis(
                particle.position.y + (particle.velocity.y + driftY) * dt,
                half,
            );
            particle.position.z = wrapAxis(
                particle.position.z + (particle.velocity.z + driftZ) * dt,
                half,
            );
        }

        this.writeBuffers(frame);
        return [];
    }

    /**
     * Fixes the cube's centre for this write: the declared offset in front of the camera, in
     * simulation space, and its phase within the period cell.
     *
     * Row three of simulation-to-view is the view's z axis in simulation space, and the view looks
     * down its negative z, so the point in front of the camera lies the other way along it.
     */
    private placeWeatherCube(frame: EmitterFrame): void {
        const e = frame.toView.elements;
        const forward = WEATHER_FORWARD_SCRATCH.set(
            -e[2],
            -e[5],
            -e[8],
        ).normalize();
        const camera = frame.camera ?? ORIGIN;
        const offset = this.emitter.properties.weatherCubeDistance;

        this.weatherCentre.x = camera.x + forward.x * offset;
        this.weatherCentre.y = camera.y + forward.y * offset;
        this.weatherCentre.z = camera.z + forward.z * offset;
        this.weatherPhase = cubePhase(this.weatherCentre, this.weatherHalf);
    }

    /**
     * The copy of a weather particle that is drawn, in simulation space, and how much of it.
     *
     * Measured: the rendered position is the particle less the centre's phase, wrapped into the
     * cube, placed around the centre. The fade distance is the length of twice the centre minus
     * that local position - as the game computes it, which leaves the fade inert for any centre
     * farther from the origin than the cutoff plus the fade-out plus the half edge. Reproduced as
     * read; the numbers the file declares mean here what they mean in the game.
     */
    private weatherDrawOf(particle: Particle): number {
        const half = this.weatherHalf;
        const centre = this.weatherCentre;
        const phase = this.weatherPhase;
        const localX = wrapAxis(particle.position.x - phase.x, half);
        const localY = wrapAxis(particle.position.y - phase.y, half);
        const localZ = wrapAxis(particle.position.z - phase.z, half);

        this.weatherDrawn.x = centre.x + localX;
        this.weatherDrawn.y = centre.y + localY;
        this.weatherDrawn.z = centre.z + localZ;

        const dx = 2 * centre.x - localX;
        const dy = 2 * centre.y - localY;
        const dz = 2 * centre.z - localZ;
        const properties = this.emitter.properties;
        return weatherFade(
            Math.sqrt(dx * dx + dy * dy + dz * dz),
            properties.weatherCutoffDistance ?? 50,
            properties.weatherFadeoutDistance,
        );
    }

    private writeBuffers(frame: EmitterFrame): void {
        const tailed = this.emitter.properties.hasTail;
        // Measured: the weather fade multiplies the colour of an additive emitter and the alpha of
        // every other kind.
        const fadeOnColour = this.emitter.properties.blendMode === "Additive";
        if (this.weatherHalf > 0) {
            this.placeWeatherCube(frame);
        }
        const order = this.frozen ? this.depthOrder(frame) : null;

        if (this.engine !== null) {
            this.writeQuads(this.engine, frame, order);
            return;
        }

        for (let i = 0; i < this.particles.length; i++) {
            const particle = this.particles[order === null ? i : order[i]];
            const look = appearanceOf(particle, this.emitter);

            // A weather particle is drawn as its copy in the cube around the camera, faded by the
            // game's distance; every other kind is drawn where it is, whole.
            const weather = this.weatherHalf > 0;
            const fade = weather ? this.weatherDrawOf(particle) : 1;
            const drawn = weather ? this.weatherDrawn : particle.position;
            const colourFade = fadeOnColour ? fade : 1;
            const alphaFade = fadeOnColour ? 1 : fade;

            // The object's uniform scale, applied to the POSITION and the SIZE together, which is
            // what a scale on the world matrix does. It has to be here rather than on the system's
            // node because the billboard path adds the half-extent in VIEW space, after the model
            // matrix - a node scale would move every particle without resizing any of it.
            this.offsets[i * 3] = drawn.x * this.scaleFactor;
            this.offsets[i * 3 + 1] = drawn.y * this.scaleFactor;
            this.offsets[i * 3 + 2] = drawn.z * this.scaleFactor;

            this.colors[i * 3] = look.r * colourFade;
            this.colors[i * 3 + 1] = look.g * colourFade;
            this.colors[i * 3 + 2] = look.b * colourFade;

            this.alphas[i] = look.a * alphaFade;
            this.sizes[i] = look.size * this.scaleFactor;
            this.rotations[i] = look.rotation;
            this.frames[i] = look.frame;

            if (tailed) {
                // A streak takes its angle from where it is going, not from the rotation track -
                // the engine zeroes that track outright for a tailed emitter.
                const kite = this.kiteFor(particle, frame);
                this.stretches[i] = kite.stretch;
                this.rotations[i] = kite.turns;
            }
        }

        this.geometry.instanceCount = this.particles.length;

        const written = [
            "iOffset",
            "iColor",
            "iAlpha",
            "iSize",
            "iRotation",
            "iFrame",
        ];
        for (const name of tailed ? [...written, "iStretch"] : written) {
            this.geometry.attributes[name].needsUpdate = true;
        }
    }

    /**
     * Whether this emitter's FROZEN batch is drawn back to front.
     *
     * Measured in the game: the depth-sort flag is read on the frozen path only, and only for the
     * alpha, depth-sprite-alpha and opaque modes - an additive batch sums the same whatever the
     * order. The engine's default is on, which is also what an older server sends by sending
     * nothing.
     */
    private sortsFrozenByDepth(): boolean {
        const properties = this.emitter.properties;
        if (properties.depthSort === false) {
            return false;
        }
        const mode = properties.blendMode;
        return (
            mode === "None" ||
            mode === "Transparent" ||
            mode === "DepthTransparent"
        );
    }

    /**
     * The baked particles' indices, farthest from the camera first, or null to draw them in bake
     * order. Squared distance, as the engine measures it, re-done against the camera as it stands.
     */
    private depthOrder(frame: EmitterFrame): number[] | null {
        const camera = frame.camera;
        if (camera === null || !this.sortsFrozenByDepth()) {
            return null;
        }
        const count = this.particles.length;
        const order = this.drawIndex;
        order.length = count;
        for (let i = 0; i < count; i++) {
            // The position as drawn - the object's uniform scale is applied to it in writeBuffers,
            // and the camera is in the unscaled simulation space.
            const position = this.particles[i].position;
            const dx = position.x * this.scaleFactor - camera.x;
            const dy = position.y * this.scaleFactor - camera.y;
            const dz = position.z * this.scaleFactor - camera.z;
            this.depths[i] = dx * dx + dy * dy + dz * dz;
            order[i] = i;
        }
        order.sort((a, b) => this.depths[b] - this.depths[a]);
        return order;
    }

    /**
     * One particle's streak, measured in the space the camera sees.
     *
     * The velocity has to reach view space for this: how long a streak looks depends on how much
     * of the motion is across the screen rather than towards it, which is a question about where
     * the camera is standing rather than about the particle.
     */
    private kiteFor(particle: Particle, frame: EmitterFrame): TailGeometry {
        const speed = Math.hypot(
            particle.velocity.x,
            particle.velocity.y,
            particle.velocity.z,
        );

        this.viewVelocity
            .set(particle.velocity.x, particle.velocity.y, particle.velocity.z)
            .applyMatrix3(
                this.emitter.properties.worldOriented
                    ? GROUND_BASIS
                    : frame.toView,
            );

        return (
            tailGeometry(this.emitter.properties, this.viewVelocity, speed) ?? {
                stretch: 1,
                turns: 0,
            }
        );
    }
}

/**
 * Why a system's emitters are being held.
 *
 *   `clip`     - the animation's visibility track says this proxy's moment has not come.
 *   `part`     - the part it is anchored on is off screen, so nobody could watch it burn.
 *   `released` - it has been taken out of the scene and is finishing what it already threw.
 */
export type EmissionHold = "clip" | "part" | "released";

/**
 * One running particle system.
 *
 * Anchored on a bone, never parented to one. The bone is read as DATA - where to spawn, how fast
 * the emitter is travelling - and the particles live in the model's space, so following the bone
 * is something the file decides rather than something parenting imposes. Hanging the group off the
 * bone said the same thing about the simulation, but it also handed the effect to the bone's
 * subtree: three prunes a hidden subtree, and an Alamo visibility track hides the very chunk whose
 * destruction the blast covers. See {@link attachTo}.
 */
export class ParticleSystemInstance {
    readonly root = new THREE.Group();

    private readonly emitters: EmitterRenderer[] = [];

    /** Where the emitter was last step, in simulation space. Null until the first one. */
    private previous: THREE.Vector3 | null = null;

    /**
     * The bone whose motion the emitters read, or null for a system standing on its own.
     *
     * Not the root's parent, deliberately - see the class comment. Held as a plain reference, so
     * nothing about the bone except its matrix reaches the simulation.
     */
    private anchor: THREE.Object3D | null = null;

    /**
     * Why the emitters may not spawn - one entry per reason, from whoever placed it.
     *
     * A SET rather than a flag, because the reasons are independent and their owners do not know
     * about each other. The clip holds a proxy whose moment has not come; hiding the part a system
     * is attached to holds it too; releasing a system to finish holds it for good. With one boolean the
     * last writer won, and `applyEmitterGating` - which runs every frame and resolved "the clip
     * says nothing about this" to TRUE - re-armed the emitters of a part that had just been hidden.
     *
     * Only SPAWNING stops either way: the particles already in the air step and die as they would
     * have, so a held effect trails away rather than vanishing mid-flight. That is what
     * `Engine::KillParticleSystem` does with `leaveParticles`, and what `RenderObject::KillProxy`
     * does through `Detach` when a proxy's own bone goes invisible.
     */
    private readonly holds = new Set<EmissionHold>();

    /** Emitters another emitter spawns, which must not also run a clock of their own. */
    private readonly dependent = new Set<number>();
    /** File indices in update and draw order: ascending priority ticket, file order on ties. */
    private readonly order: number[];

    private readonly frame: EmitterFrame = stillFrame();

    constructor(
        readonly name: string,
        system: AlamoParticleContent,
        textures: ReadonlyMap<string, THREE.Texture> = new Map(),
        seed = 1,
        /**
         * The geometry the system is attached to, for the emitters that emit from a mesh.
         *
         * Supplied by the caller rather than looked up here, because only the viewport knows which
         * bone the system is attached to and what geometry that bone carries.
         */
        emissionMesh: EmissionMesh | null = null,
        /**
         * The space the particles live in - the model root.
         *
         * Null for a system standing on its own, which is already its own frame of reference.
         */
        private readonly space: THREE.Object3D | null = null,
        /**
         * The owning object's <c>Scale_Factor</c>, a uniform render scale.
         *
         * `DatabaseMapExport.xml` lists it on the base GameObjectType beside `Mass` and `LOD_Bias`,
         * and the reference applies it as a uniform scale on the object's world matrix - so it
         * scales where a particle spawns as well as how big it draws. Six shipped particle objects
         * declare one: 20.0 on the four hero powerup effects, 2.0 on the two bombing-run
         * explosions. Nothing read it, so all six drew at a fraction of their size.
         */
        scaleFactor = 1,
    ) {
        system.emitters.forEach((emitter, index) => {
            // A distinct stream per emitter, seeded from the index: two emitters of one system must
            // not scatter identically, and the whole thing must still replay the same way twice.
            const renderer = new EmitterRenderer(
                emitter,
                seededRandom(seed + index * 7919),
                textures.get(emitter.colorTexture.toLowerCase()) ?? null,
                emissionMesh,
                scaleFactor,
            );

            this.emitters.push(renderer);
            this.root.add(renderer.mesh);
        });

        // Measured in the game: after loading, the emitters are sorted by priority ticket,
        // ascending, and that is their update and draw order from then on. The file order is kept
        // for everything that names an emitter by index; this is the order they run in.
        const ticketed = this.emitters.some(
            (e) => e.description.properties.priorityTicket !== undefined,
        );
        this.order = this.emitters
            .map((e, index) => ({
                index,
                ticket: ticketed
                    ? (e.description.properties.priorityTicket ?? 0)
                    : index,
            }))
            .sort((a, b) => a.ticket - b.ticket || a.index - b.index)
            .map((e) => e.index);
        this.order.forEach((index, rank) => {
            this.emitters[index].mesh.renderOrder = rank;
        });

        for (const emitter of system.emitters) {
            for (const link of [
                emitter.spawnOnDeath,
                emitter.spawnDuringLife,
            ]) {
                const child = this.childIndexOf(link);
                if (child >= 0) {
                    this.dependent.add(child);
                }
            }
        }

        this.root.name = `particles:${name}`;
    }

    /** True once every emitter has finished and its last particle has died. */
    get exhausted(): boolean {
        return this.emitters.every((emitter) => emitter.exhausted);
    }

    /**
     * True once nothing this system threw is still in the air.
     *
     * NOT {@link exhausted}, which also asks whether the emitter has finished - and a continuous
     * emitter never finishes. For a system that has been told to stop spawning, "finished" will
     * never come, so what decides when it can be disposed is simply whether anything is left.
     */
    get empty(): boolean {
        return this.emitters.every((emitter) => emitter.live.length === 0);
    }

    /** Whether anything here needs the distortion pass, which is not worth its targets otherwise. */
    get hasVisibleHeat(): boolean {
        return (
            this.root.visible &&
            this.emitters.some(
                (emitter) =>
                    emitter.description.properties.isHeatParticle &&
                    emitter.mesh.visible,
            )
        );
    }

    /** Whether anything is currently holding the emitters, and what. */
    get heldBy(): EmissionHold[] {
        return [...this.holds];
    }

    /** The bone this system is anchored on, for the callers that describe where an effect sits. */
    get anchorNode(): THREE.Object3D | null {
        return this.anchor;
    }

    /**
     * Whether the emitters may spawn this step.
     *
     * Every gate resolved HERE rather than by several writers of one flag: the reasons in
     * {@link holds}, and being drawn at all. A system nobody can see is not running - the
     * reader's tick, the effects master, the level gate and the quiet-on-open rule all hold an
     * effect back by not drawing it, and an emitter that goes on firing behind that is spending its
     * life where nobody can watch it. For a burst that is fatal: `p_explosion_empire_atat00` was
     * exhausted seconds before its row could be ticked on, so switching it on showed nothing at all.
     *
     * The clock is held with it - `EmitterRenderer.update` only advances it when it may spawn - so
     * a held system starts from its first frame rather than resuming somewhere in the middle.
     */
    private get spawning(): boolean {
        return this.holds.size === 0 && this.root.visible;
    }

    /** Every texture this system's emitters want, lowercased and de-duplicated - normal maps too. */
    textureNames(): string[] {
        return [
            ...new Set(
                this.emitters
                    .flatMap((emitter) => [
                        emitter.textureName.toLowerCase(),
                        emitter.normalTextureName.toLowerCase(),
                    ])
                    .filter((name) => name !== ""),
            ),
        ];
    }

    /** Every engine effect this system's emitters are drawn with, de-duplicated and sorted. */
    effectNames(): string[] {
        return [
            ...new Set(
                this.emitters
                    .map((emitter) => emitter.effectName)
                    .filter((name): name is string => name !== null),
            ),
        ].sort();
    }

    /**
     * Draws each emitter through its translated engine effect where one is in hand and the
     * translation is on; the preview's own shader otherwise. Keys are lower-cased effect names.
     */
    applyEffects(
        effects: ReadonlyMap<string, TranslatedEffect>,
        states: ReadonlyMap<string, FxMaterialState>,
        enabled: boolean,
    ): void {
        for (const emitter of this.emitters) {
            const name = emitter.effectName?.toLowerCase();
            const effect =
                enabled && name !== undefined ? effects.get(name) : undefined;
            const state = name === undefined ? undefined : states.get(name);
            emitter.applyEffect(effect ?? null, state ?? null);
        }
    }

    /**
     * Roughly how far this system's particles reach, for framing.
     *
     * Taken from the emitter description rather than the geometry: the buffers start full of zeros
     * and fill in over the next seconds, so measuring them frames a point.
     */
    extent(): number {
        return systemExtent(
            this.emitters.map((emitter) => emitter.description),
        );
    }

    /** The emitters in file order. Names repeat - p_explosion_huge01 has three called "debre". */
    emitterNames(): string[] {
        return this.emitters.map((emitter) => emitter.mesh.name);
    }

    /** Whether one emitter lies flat on the ground rather than facing the camera. */
    emitterWorldOriented(index: number): boolean {
        return (
            this.emitters[index]?.description.properties.worldOriented ?? false
        );
    }

    /** Which of them are heat distortions rather than sprites, so the dock can say so. */
    emitterIsHeat(): boolean[] {
        return this.emitters.map(
            (emitter) => emitter.description.properties.isHeatParticle,
        );
    }

    /** Whether one emitter is currently drawn. */
    emitterVisible(index: number): boolean {
        return this.emitters[index]?.mesh.visible ?? false;
    }

    /** One emitter's drawn object, for the callers that need to ask about its layer or bounds. */
    emitterMesh(index: number): THREE.Mesh | null {
        return this.emitters[index]?.mesh ?? null;
    }

    /**
     * One emitter's live particles, in simulation space.
     *
     * Read-only, and here because the questions worth asking about an effect - is it moving with
     * the hull, is anything alive at all - are questions about the simulation rather than the
     * pixels, and reading them off a screenshot is guesswork.
     */
    emitterParticles(index: number): readonly Particle[] {
        return this.emitters[index]?.live ?? [];
    }

    /**
     * Shows or hides one emitter, by its position in the file.
     *
     * By index rather than by name because names are not unique within a system, so a name-keyed
     * toggle would switch all three of that explosion's debris emitters at once.
     */
    setEmitterVisible(index: number, visible: boolean): void {
        const emitter = this.emitters[index];
        if (emitter !== undefined) {
            emitter.mesh.visible = visible;
        }
    }

    /** Routes a texture to whichever emitters name it. */
    setTexture(name: string, texture: THREE.Texture): void {
        const wanted = name.toLowerCase();

        for (const emitter of this.emitters) {
            if (emitter.textureName.toLowerCase() === wanted) {
                emitter.setTexture(texture);
            }
            if (emitter.normalTextureName.toLowerCase() === wanted) {
                emitter.setNormalTexture(texture);
            }
        }
    }

    /** Marks whichever emitters name a texture that turned out not to resolve. */
    setMissingTexture(name: string, texture: THREE.Texture): void {
        const wanted = name.toLowerCase();

        for (const emitter of this.emitters) {
            if (emitter.textureName.toLowerCase() === wanted) {
                emitter.setMissingTexture(texture);
            }
        }
    }

    /**
     * Places or lifts ONE reason to hold the emitters. Live particles are unaffected either way.
     *
     * Lifting a reason says only that this caller no longer objects - every other hold still
     * stands. Nothing here is a permission to spawn.
     */
    hold(reason: EmissionHold, held: boolean): void {
        if (held) {
            this.holds.add(reason);
        } else {
            this.holds.delete(reason);
        }
    }

    /**
     * Names the bone this system is anchored on.
     *
     * The caller parents {@link root} to the SIMULATION SPACE and says which bone the effect
     * belongs to here. Two things follow from the split. The bone's transform is cancelled out of
     * the simulation either way - that was already true - but now no ancestor of the bone can take
     * the effect off screen with it, which is what an explosion covering a chunk that is being
     * hidden needs, and what {@link ParticleSystemInstance.detach} then relies on.
     */
    attachTo(anchor: THREE.Object3D | null): void {
        this.anchor = anchor;

        // A fresh reference point. Carrying the old one across would read the whole distance
        // between two bones as one step's movement and fling every linked particle.
        this.previous = null;
    }

    /**
     * Cuts the system loose from its bone, leaving everything it has thrown exactly where it is.
     *
     * For a system that has been removed but whose particles are allowed to finish: it no longer
     * belongs to anything, and going on reading a bone that is about to be disposed would report
     * its last stale matrix as motion.
     */
    detach(): void {
        this.attachTo(null);
    }

    update(
        dt: number,
        view: THREE.Matrix4 | null = null,
        wind?: AlamoVector3,
    ): void {
        const step = Math.min(Math.max(dt, 0), MAX_STEP_SECONDS);
        const frame = this.frameOf(step, view);

        if (wind !== undefined) {
            frame.wind = wind;
        }

        // Parents first, so a child sees this step's particles and this step's deaths.
        // Gated emission reuses the `dependent` path, which is already "step, but do not spawn".
        const held = !this.spawning;
        const deaths: AlamoVector3[][] = new Array<AlamoVector3[]>(
            this.emitters.length,
        );
        for (const index of this.order) {
            deaths[index] = this.emitters[index].update(
                step,
                frame,
                this.dependent.has(index) || held,
            );
        }

        // The chains are emission too. A trail dragged off a particle that is still in the air
        // while the system is held would keep spawning behind the reader's back, which is the same
        // defect one level down.
        if (held) {
            return;
        }

        for (const index of this.order) {
            this.runChains(this.emitters[index], index, deaths, step, frame);
        }
    }

    /** The emitters' file indices in the order they update and draw: ascending ticket. */
    drawOrder(): number[] {
        return [...this.order];
    }

    setVisible(visible: boolean): void {
        this.root.visible = visible;
    }

    dispose(): void {
        for (const emitter of this.emitters) {
            emitter.dispose();
        }

        this.root.removeFromParent();
    }

    /**
     * Drives the emitters that another emitter spawns.
     *
     * Two kinds, and 21 of the corpus's 409 systems use one: `spawnDuringLife` trails a child off
     * every live particle - an explosion's sparks each dragging their own smoke - and
     * `spawnOnDeath` fires one where a particle died. Neither child runs its own clock, so this is
     * the only thing that makes them emit at all.
     */
    private runChains(
        parent: EmitterRenderer,
        index: number,
        deaths: AlamoVector3[][],
        step: number,
        frame: EmitterFrame,
    ): void {
        // A frozen parent no longer runs the modifiers that spawn its children, so its baked
        // particles trail nothing and die never.
        if (parent.isFrozen) {
            return;
        }
        const trail = this.childOf(parent.description.spawnDuringLife);

        if (trail !== null) {
            for (const particle of parent.live) {
                particle.childDebt += trail.rate * step;
                const whole = Math.floor(particle.childDebt);

                if (whole > 0) {
                    particle.childDebt -= whole;
                    trail.emitFrom(particle.position, whole, frame);
                }
            }

            trail.flush(frame);
        }

        const onDeath = this.childOf(parent.description.spawnOnDeath);

        if (onDeath !== null) {
            for (const at of deaths[index]) {
                onDeath.emitFrom(at, onDeath.burstSize, frame);
            }

            onDeath.flush(frame);
        }
    }

    /** The emitter a spawn link names, or null when the file names none. */
    private childOf(link: number): EmitterRenderer | null {
        const index = this.childIndexOf(link);
        return index >= 0 ? this.emitters[index] : null;
    }

    /**
     * The index of the emitter a spawn link names, or -1.
     *
     * Measured in the game: after loading, a link is resolved by finding the emitter whose
     * PRIORITY TICKET equals the stored value - not the emitter at that position. Every shipped
     * and workshop system numbers its tickets in file order, so both readings agree there; a
     * reordered file is where they part. A system with no tickets at all (an older server, a
     * fixture) still reads the link as a position.
     */
    private childIndexOf(link: number): number {
        if (link < 0) {
            return -1;
        }

        const ticketed = this.emitters.some(
            (e) => e.description.properties.priorityTicket !== undefined,
        );
        if (!ticketed) {
            return link < this.emitters.length ? link : -1;
        }

        return this.emitters.findIndex(
            (e) => e.description.properties.priorityTicket === link,
        );
    }

    /**
     * Puts the group in the simulation's space, and reports what the anchor bone is doing.
     *
     * Two jobs, together because they are the same matrices. The group's own transform is
     * overwritten with whatever cancels its parent out, after which the local coordinates the
     * emitters write are the simulation space's coordinates and a particle that should be left
     * behind can be. Where the group actually hangs is then immaterial - it is the ANCHOR that
     * says what the effect is following, as ordinary data the emitters act on: where to spawn, how
     * far to carry a linked particle, what speed to hand a newborn one.
     */
    private frameOf(dt: number, view: THREE.Matrix4 | null): EmitterFrame {
        const parent = this.root.parent;
        const space = this.space;

        if (space !== null && parent !== null) {
            // Read fresh: the animation mixer has already moved the bones this frame, but their
            // world matrices are only recomputed at render time.
            parent.updateWorldMatrix(true, false);
            space.updateWorldMatrix(true, false);

            this.root.matrixAutoUpdate = false;
            this.root.matrix
                .copy(parent.matrixWorld)
                .invert()
                .multiply(space.matrixWorld);
            this.root.matrixWorldNeedsUpdate = true;
        }

        // Where the emitters' coordinates are read: the space when there is one, and otherwise the
        // group itself, which is then its own frame of reference.
        const simulation = space ?? this.root;
        const anchor = this.anchor;

        if (anchor === null) {
            // A system standing on its own, or one cut loose to finish. Nothing is moving relative
            // to anything, and the frame is simply where the simulation space stands.
            simulation.updateWorldMatrix(true, false);
            this.frame.matrix.identity();
            this.frame.rotation.identity();
            this.frame.delta.set(0, 0, 0);
            this.frame.velocity.set(0, 0, 0);
            this.viewFrom(simulation, view);
            simulation.getWorldPosition(GROUND_SCRATCH);
            this.frame.groundY = -GROUND_SCRATCH.y;

            return this.frame;
        }

        anchor.updateWorldMatrix(true, false);
        simulation.updateWorldMatrix(true, false);

        const toSpace = SPACE_SCRATCH.copy(simulation.matrixWorld).invert();

        this.frame.matrix.multiplyMatrices(toSpace, anchor.matrixWorld);
        this.frame.rotation.setFromMatrix4(this.frame.matrix);

        MOTION_SCRATCH.setFromMatrixPosition(this.frame.matrix);

        // The first step has nothing to compare against, and treating the emitter's whole offset
        // from the origin as one frame's movement would fling every particle across the model.
        this.frame.delta.copy(MOTION_SCRATCH);
        if (this.previous === null) {
            this.previous = new THREE.Vector3();
            this.frame.delta.set(0, 0, 0);
        } else {
            this.frame.delta.sub(this.previous);
        }

        this.previous.copy(MOTION_SCRATCH);
        this.frame.velocity
            .copy(this.frame.delta)
            .divideScalar(Math.max(dt, 1e-6));

        this.viewFrom(simulation, view);

        // Where the scene's ground plane falls in the space the particles live in - it is the
        // SCENE's ground a falling particle lands on, not the model's own height.
        simulation.getWorldPosition(GROUND_SCRATCH);
        this.frame.groundY = -GROUND_SCRATCH.y;

        return this.frame;
    }

    /**
     * Records simulation space to view space, so the streaks do not each have to ask where the
     * camera is standing.
     */
    private viewFrom(space: THREE.Object3D, view: THREE.Matrix4 | null): void {
        if (view === null) {
            this.frame.toView.setFromMatrix4(IDENTITY);
            this.frame.camera = null;
            return;
        }
        VIEW_SCRATCH.multiplyMatrices(view, space.matrixWorld);
        this.frame.toView.setFromMatrix4(VIEW_SCRATCH);
        // The camera's own position, brought into the space the particles live in: the inverse
        // of simulation-to-view takes the view origin back. A frozen batch is sorted by distance
        // from it, so it is a question every frame, not once.
        this.frame.camera = CAMERA_SCRATCH.setFromMatrixPosition(
            CAMERA_MATRIX_SCRATCH.copy(VIEW_SCRATCH).invert(),
        );
    }
}

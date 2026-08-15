// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Draws what particleSim.ts simulates.
//
// Kept apart from the simulation so the arithmetic stays testable: nothing here decides where a
// particle goes, only how it is put on screen.

import * as THREE from 'three';

import type {
    AlamoEmitter, AlamoParticleContent, AlamoVector3,
} from '../../protocol/modelPreview';
import {
    emissionMultiplier, emissionPoint, offsetAlongNormal,
    type EmissionCursor, type EmissionMesh,
} from './meshEmission';
import {
    accelerationIn, appearanceOf, atlasColumns, seededRandom, SpawnClock, spawnParticle,
    stepParticle, systemExtent, type Particle, type Random,
} from './particleSim';

/**
 * Ceiling on live particles per emitter.
 *
 * A weather emitter authored for a whole battlefield will happily ask for tens of thousands, which is
 * a frozen tab rather than a preview. Oldest are retired first so the effect keeps running.
 */
const MAX_PARTICLES = 2000;

/** Reused so the per-frame ground lookup allocates nothing. */
const GROUND_SCRATCH = new THREE.Vector3();
const MOTION_SCRATCH = new THREE.Vector3();
const SPAWN_SCRATCH = new THREE.Vector3();
const SPACE_SCRATCH = new THREE.Matrix4();
const VIEW_SCRATCH = new THREE.Matrix4();
const IDENTITY = new THREE.Matrix4();

/**
 * Longest step the simulation will take.
 *
 * A backgrounded tab resumes with a multi-second delta; integrating that in one go teleports every
 * particle somewhere absurd. Clamping makes the effect briefly slow instead, which nobody notices.
 */
const MAX_STEP_SECONDS = 0.1;

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
    const base = { blending: THREE.CustomBlending, depthWrite: false, alphaTest: 0 } as const;

    switch (mode) {
        // PrimAdditive.fx, PrimDepthSpriteAdditive.fx
        case 'Additive':
        case 'DepthAdditive':
            return {
                ...base, alphaGated: false,
                blendSrc: THREE.OneFactor,
                blendDst: THREE.OneFactor,
                blendSrcAlpha: THREE.ZeroFactor,
                blendDstAlpha: THREE.OneFactor,
            };

        // PrimModulate.fx, PrimDepthSpriteModulate.fx - a multiply, which darkens.
        case 'Inverse':
        case 'DepthInverse':
        case 'StencilDarken':
        case 'StencilDarkenBlur':
            return {
                ...base, alphaGated: false,
                blendSrc: THREE.ZeroFactor,
                blendDst: THREE.SrcColorFactor,
                blendSrcAlpha: THREE.ZeroFactor,
                blendDstAlpha: THREE.OneFactor,
            };

        // PrimDecalBumpAlpha.fx - `DESTCOLOR / SRCCOLOR`, a decal multiply.
        case 'DecalBump':
            return {
                ...base, alphaGated: false,
                blendSrc: THREE.DstColorFactor,
                blendDst: THREE.SrcColorFactor,
                blendSrcAlpha: THREE.ZeroFactor,
                blendDstAlpha: THREE.OneFactor,
            };

        // PrimOpaque.fx - the one mode that is not blended at all, and the one that writes depth.
        case 'None':
            return {
                blending: THREE.NoBlending, depthWrite: true, alphaGated: false, alphaTest: 0,
            };

        /*
         * A stand-in. `PrimHeat.fx` renders a DISTORTION - it writes the sprite's normals into a
         * buffer that warps the frame behind it, and contributes no colour of its own. There is no
         * cheap equivalent here, so the sprite is added as a faint shimmer scaled by its own alpha
         * rather than drawn as the grey-blue normal map it actually samples. No shipped emitter
         * selects this mode; the 127 heat particles in the corpus set the `isHeatParticle` FLAG on
         * an ordinary Transparent or Additive mode instead.
         */
        case 'Heat':
            return {
                ...base, alphaGated: false,
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
                alphaTest: mode === 'Bump' ? BUMP_ALPHA_REF : 0,
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
    /** Where the scene's ground plane falls in simulation space. */
    groundY: number;
    /** The wind, in simulation space. Half the corpus's emitters are thrown by it at birth. */
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
const GROUND_BASIS = new THREE.Matrix3().set(
    1, 0, 0,
    0, 0, 1,
    0, 0, 0);

/**
 * How much of the emitter's movement a particle is carried along by.
 *
 * Two independent settings, and the shipped data never uses both - across 1019 emitters, 168 set
 * `linkToSystem` and 316 a non-zero `parentLinkStrength`, with no overlap - so summing them cannot
 * double-count. `EmitterTranslaterPlugin` is the first (all or nothing); `EmitterInstance.cpp:545`
 * is the second, a fraction.
 */
export function followFactor(properties: AlamoEmitter['properties']): number {
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
    properties: AlamoEmitter['properties'], velocity: AlamoVector3, speed: number,
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
    const length = properties.tailSize * mult * planar / speed;

    return {
        stretch: Math.max(1, length / Math.SQRT2),
        // The corner being pulled sits on the quad's 135 degree diagonal, so the extra eighth of a
        // turn lands it opposite the direction of travel.
        turns: (Math.atan2(velocity.y, velocity.x) + Math.PI / 4) / (Math.PI * 2),
    };
}

/** An emitter that neither moves nor is moved, for a system standing on its own. */
function stillFrame(): EmitterFrame {
    return {
        matrix: new THREE.Matrix4(),
        rotation: new THREE.Matrix3(),
        toView: new THREE.Matrix3(),
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

    private readonly offsets = new Float32Array(MAX_PARTICLES * 3);
    private readonly colors = new Float32Array(MAX_PARTICLES * 3);
    private readonly alphas = new Float32Array(MAX_PARTICLES);
    private readonly sizes = new Float32Array(MAX_PARTICLES);
    private readonly rotations = new Float32Array(MAX_PARTICLES);
    private readonly frames = new Float32Array(MAX_PARTICLES);
    private readonly stretches = new Float32Array(MAX_PARTICLES).fill(1);

    /** Reused per particle by the kite maths, so a thousand sparks allocate nothing. */
    private readonly viewVelocity = new THREE.Vector3();

    /** Where `EveryVertex` has got to. Kept across ticks, because that walk is ordered. */
    private readonly cursor: EmissionCursor = { subMesh: 0, vertex: 0 };

    constructor(
        private readonly emitter: AlamoEmitter,
        private readonly random: Random,
        texture: THREE.Texture | null,
        /** The geometry this emitter is born from, when it emits from a mesh rather than a shape. */
        private readonly emissionMesh: EmissionMesh | null = null,
    ) {
        this.clock = new SpawnClock(emitter);

        this.geometry = new THREE.InstancedBufferGeometry();
        this.geometry.setAttribute('corner', new THREE.BufferAttribute(
            new Float32Array([-1, -1, 1, -1, 1, 1, -1, 1]), 2));
        this.geometry.setIndex([0, 1, 2, 0, 2, 3]);

        this.geometry.setAttribute('iOffset', new THREE.InstancedBufferAttribute(this.offsets, 3));
        this.geometry.setAttribute('iColor', new THREE.InstancedBufferAttribute(this.colors, 3));
        this.geometry.setAttribute('iAlpha', new THREE.InstancedBufferAttribute(this.alphas, 1));
        this.geometry.setAttribute('iSize', new THREE.InstancedBufferAttribute(this.sizes, 1));
        this.geometry.setAttribute(
            'iRotation', new THREE.InstancedBufferAttribute(this.rotations, 1));
        this.geometry.setAttribute('iFrame', new THREE.InstancedBufferAttribute(this.frames, 1));
        this.geometry.setAttribute(
            'iStretch', new THREE.InstancedBufferAttribute(this.stretches, 1));
        this.geometry.instanceCount = 0;

        const heat = emitter.properties.isHeatParticle;

        // A heat sprite writes a normal and a strength into the heat buffer, where sprites simply
        // blend over one another - the blend mode the emitter names describes the colour it would
        // have contributed, and it contributes none.
        const { alphaGated, alphaTest, ...blendState } = heat
            ? blendingFor('Transparent')
            : blendingFor(emitter.properties.blendMode);

        this.material = new THREE.ShaderMaterial({
            ...blendState,
            uniforms: {
                uMap: { value: texture },
                uHasMap: { value: texture === null ? 0 : 1 },
                uMissing: { value: 0 },
                uColumns: { value: atlasColumns(emitter.properties.textureSize) },
                uAlphaGated: { value: alphaGated ? 1 : 0 },
                uAlphaTest: { value: alphaTest },
                uWorldOriented: { value: emitter.properties.worldOriented ? 1 : 0 },
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

    /**
     * Supplies the texture once it arrives.
     *
     * Textures are fetched after the system, because the system is what names them. Until then the
     * emitter draws untextured rather than not at all - an untextured puff still shows the motion,
     * which is most of what the preview is for.
     */
    setTexture(texture: THREE.Texture): void {
        this.material.uniforms.uMap.value = texture;
        this.material.uniforms.uHasMap.value = 1;
        this.material.uniforms.uMissing.value = 0;
        this.material.needsUpdate = true;
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

    /** True once the emitter has finished and its last particle has died. */
    get exhausted(): boolean {
        return this.clock.finished && this.particles.length === 0;
    }

    /** The live particles, for the tests and diagnostics that need to read the simulation. */
    get live(): readonly Particle[] {
        return this.particles;
    }

    /**
     * Advances the emitter a step, and reports where its particles died.
     *
     * `dependent` marks an emitter that another one spawns - a trail or a death child. Those must
     * not run their own clock: the engine only ever creates them from the parent, and letting one
     * emit on its own put an explosion's smoke trail at the blast's centre as a puff.
     */
    update(dt: number, frame: EmitterFrame = stillFrame(), dependent = false): AlamoVector3[] {
        const follow = followFactor(this.emitter.properties);
        const acceleration = this.accelerationIn(frame);
        const deaths: AlamoVector3[] = [];

        for (let i = this.particles.length - 1; i >= 0; i--) {
            const particle = this.particles[i];

            if (!stepParticle(particle, this.emitter, dt, frame.groundY, acceleration)) {
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
        const wanted = dependent || !this.mesh.visible
            ? 0
            : this.clock.advance(dt) * emissionMultiplier(mode, this.emissionMesh);

        for (let i = 0; i < wanted; i++) {
            if (this.particles.length >= MAX_PARTICLES) {
                this.particles.shift();
            }

            const particle = spawnParticle(this.emitter, this.random, frame.wind);

            // Born ON the mesh rather than inside the emitter's shape volume. The shape still
            // decides the VELOCITY; only the position moves.
            if (mode !== 'Disabled' && this.emissionMesh !== null) {
                const point = emissionPoint(mode, this.emissionMesh, this.cursor, this.random);

                if (point !== null) {
                    particle.position = offsetAlongNormal(
                        point, this.emitter.properties.emitFromMeshOffset);
                }
            }

            this.place(particle, frame);
            this.particles.push(particle);
        }

        this.writeBuffers(frame);

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
        // own clock, so the check in `update` never sees it.
        if (!this.mesh.visible) {
            return;
        }

        for (let i = 0; i < count; i++) {
            if (this.particles.length >= MAX_PARTICLES) {
                this.particles.shift();
            }

            const particle = spawnParticle(this.emitter, this.random, frame.wind);
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

    /**
     * How fast a child emits, per parent particle.
     *
     * The engine gives every parent particle its OWN emitter instance, so the rate is per particle
     * and not shared - a hundred sparks each drag a full trail.
     */
    get rate(): number {
        const properties = this.emitter.properties;

        return properties.useBursts
            ? properties.particlesPerBurst / Math.max(properties.burstDelay, 1e-3)
            : properties.particlesPerSecond;
    }

    /** A whole burst of it, for a death child, which the engine fires as one. */
    get burstSize(): number {
        return Math.max(1, this.emitter.properties.particlesPerBurst);
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

        SPAWN_SCRATCH
            .set(properties.acceleration.x, properties.acceleration.y, properties.acceleration.z)
            .applyMatrix3(frame.rotation);

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
        SPAWN_SCRATCH.set(particle.position.x, particle.position.y, particle.position.z)
            .applyMatrix4(frame.matrix);
        particle.position = { x: SPAWN_SCRATCH.x, y: SPAWN_SCRATCH.y, z: SPAWN_SCRATCH.z };

        // The direction it was born in travels with the velocity: the inward pull is measured from
        // it for the particle's whole life, and half the corpus's emitters use one.
        SPAWN_SCRATCH.set(particle.inward.x, particle.inward.y, particle.inward.z)
            .applyMatrix3(frame.rotation);
        particle.inward = { x: SPAWN_SCRATCH.x, y: SPAWN_SCRATCH.y, z: SPAWN_SCRATCH.z };

        SPAWN_SCRATCH.set(particle.velocity.x, particle.velocity.y, particle.velocity.z)
            .applyMatrix3(frame.rotation);

        // `parentLinkStrength` again, in its other role: a particle is born already carrying its
        // share of the emitter's own motion, so debris thrown off a moving hull keeps up with it.
        const inherited = this.emitter.properties.parentLinkStrength;

        particle.velocity = {
            x: SPAWN_SCRATCH.x + frame.velocity.x * inherited,
            y: SPAWN_SCRATCH.y + frame.velocity.y * inherited,
            z: SPAWN_SCRATCH.z + frame.velocity.z * inherited,
        };
    }

    private writeBuffers(frame: EmitterFrame): void {
        const tailed = this.emitter.properties.hasTail;

        for (let i = 0; i < this.particles.length; i++) {
            const particle = this.particles[i];
            const look = appearanceOf(particle, this.emitter);

            this.offsets[i * 3] = particle.position.x;
            this.offsets[i * 3 + 1] = particle.position.y;
            this.offsets[i * 3 + 2] = particle.position.z;

            this.colors[i * 3] = look.r;
            this.colors[i * 3 + 1] = look.g;
            this.colors[i * 3 + 2] = look.b;

            this.alphas[i] = look.a;
            this.sizes[i] = look.size;
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

        const written = ['iOffset', 'iColor', 'iAlpha', 'iSize', 'iRotation', 'iFrame'];
        for (const name of tailed ? [...written, 'iStretch'] : written) {
            this.geometry.attributes[name].needsUpdate = true;
        }
    }

    /**
     * One particle's streak, measured in the space the camera sees.
     *
     * The velocity has to reach view space for this: how long a streak looks depends on how much
     * of the motion is across the screen rather than towards it, which is a question about where
     * the camera is standing rather than about the particle.
     */
    private kiteFor(particle: Particle, frame: EmitterFrame): TailGeometry {
        const speed = Math.hypot(particle.velocity.x, particle.velocity.y, particle.velocity.z);

        this.viewVelocity
            .set(particle.velocity.x, particle.velocity.y, particle.velocity.z)
            .applyMatrix3(this.emitter.properties.worldOriented ? GROUND_BASIS : frame.toView);

        return tailGeometry(this.emitter.properties, this.viewVelocity, speed)
            ?? { stretch: 1, turns: 0 };
    }

    dispose(): void {
        this.geometry.dispose();
        this.material.dispose();
    }
}

/**
 * One running particle system.
 *
 * Hung off a bone so the tree owns it and hiding a limb hides its effects - but NOT simulated in
 * that bone's space. The group's own transform is cancelled out every frame (see `frameOf`), so the
 * particles live in the model's space and following the bone becomes something the file decides
 * rather than something parenting imposes.
 */
export class ParticleSystemInstance {
    readonly root = new THREE.Group();

    private readonly emitters: EmitterRenderer[] = [];

    /** Where the emitter was last step, in simulation space. Null until the first one. */
    private previous: THREE.Vector3 | null = null;

    /**
     * Whether the emitters may spawn this step.
     *
     * Switched off when the animation hides the proxy bone this system hangs from - which is how
     * Alamo times an effect to a clip, and what 2285 of the shipped visibility tracks are for.
     * Only SPAWNING stops: the particles already in the air step and die as they would have, so an
     * effect that switches off trails away rather than vanishing mid-flight.
     */
    private emitting = true;

    /** Emitters another emitter spawns, which must not also run a clock of their own. */
    private readonly dependent = new Set<number>();

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
         * bone the system hangs off and what geometry that bone carries.
         */
        emissionMesh: EmissionMesh | null = null,
        /**
         * The space the particles live in - the model root.
         *
         * Null for a system standing on its own, which is already its own frame of reference.
         */
        private readonly space: THREE.Object3D | null = null,
    ) {
        system.emitters.forEach((emitter, index) => {
            // A distinct stream per emitter, seeded from the index: two emitters of one system must
            // not scatter identically, and the whole thing must still replay the same way twice.
            const renderer = new EmitterRenderer(
                emitter,
                seededRandom(seed + index * 7919),
                textures.get(emitter.colorTexture.toLowerCase()) ?? null,
                emissionMesh);

            this.emitters.push(renderer);
            this.root.add(renderer.mesh);
        });

        for (const emitter of system.emitters) {
            for (const child of [emitter.spawnOnDeath, emitter.spawnDuringLife]) {
                if (child >= 0 && child < system.emitters.length) {
                    this.dependent.add(child);
                }
            }
        }

        this.root.name = `particles:${name}`;
    }

    /** Every texture this system's emitters want, lowercased and de-duplicated. */
    textureNames(): string[] {
        return [...new Set(
            this.emitters
                .map(emitter => emitter.textureName.toLowerCase())
                .filter(name => name !== ''))];
    }

    /**
     * Roughly how far this system's particles reach, for framing.
     *
     * Taken from the emitter description rather than the geometry: the buffers start full of zeros
     * and fill in over the next seconds, so measuring them frames a point.
     */
    extent(): number {
        return systemExtent(this.emitters.map(emitter => emitter.description));
    }

    /** True once every emitter has finished and its last particle has died. */
    get exhausted(): boolean {
        return this.emitters.every(emitter => emitter.exhausted);
    }

    /** The emitters in file order. Names repeat - p_explosion_huge01 has three called "debre". */
    emitterNames(): string[] {
        return this.emitters.map(emitter => emitter.mesh.name);
    }

    /** Whether one emitter lies flat on the ground rather than facing the camera. */
    emitterWorldOriented(index: number): boolean {
        return this.emitters[index]?.description.properties.worldOriented ?? false;
    }

    /** Which of them are heat distortions rather than sprites, so the dock can say so. */
    emitterIsHeat(): boolean[] {
        return this.emitters.map(emitter => emitter.description.properties.isHeatParticle);
    }

    /** Whether one emitter is currently drawn. */
    emitterVisible(index: number): boolean {
        return this.emitters[index]?.mesh.visible ?? false;
    }

    /** One emitter's drawn object, for the callers that need to ask about its layer or bounds. */
    emitterMesh(index: number): THREE.Mesh | null {
        return this.emitters[index]?.mesh ?? null;
    }

    /** Whether anything here needs the distortion pass, which is not worth its targets otherwise. */
    get hasVisibleHeat(): boolean {
        return this.root.visible && this.emitters.some(
            emitter => emitter.description.properties.isHeatParticle && emitter.mesh.visible);
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

    /** Lets the emitters spawn, or holds them. Live particles are unaffected either way. */
    setEmitting(emitting: boolean): void {
        this.emitting = emitting;
    }

    /**
     * Whether the emitters may spawn this step.
     *
     * TWO gates, resolved here rather than by two writers of one flag: the clip's, which
     * `setEmitting` carries, and being drawn at all. A system nobody can see is not running - the
     * reader's tick, the effects master, the level gate and the quiet-on-open rule all hold an
     * effect back by not drawing it, and an emitter that goes on firing behind that is spending its
     * life where nobody can watch it. For a burst that is fatal: `p_explosion_empire_atat00` was
     * exhausted seconds before its row could be ticked on, so switching it on showed nothing at all.
     *
     * The clock is held with it - `EmitterRenderer.update` only advances it when it may spawn - so
     * a held system starts from its first frame rather than resuming somewhere in the middle.
     */
    private get spawning(): boolean {
        return this.emitting && this.root.visible;
    }

    update(dt: number, view: THREE.Matrix4 | null = null, wind?: AlamoVector3): void {
        const step = Math.min(Math.max(dt, 0), MAX_STEP_SECONDS);
        const frame = this.frameOf(step, view);

        if (wind !== undefined) {
            frame.wind = wind;
        }

        // Parents first, so a child sees this step's particles and this step's deaths.
        // Gated emission reuses the `dependent` path, which is already "step, but do not spawn".
        const held = !this.spawning;
        const deaths = this.emitters.map(
            (emitter, index) => emitter.update(
                step, frame, this.dependent.has(index) || held));

        // The chains are emission too. A trail dragged off a particle that is still in the air
        // while the system is held would keep spawning behind the reader's back, which is the same
        // defect one level down.
        if (held) {
            return;
        }

        this.emitters.forEach((parent, index) => this.runChains(parent, index, deaths, step, frame));
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
        parent: EmitterRenderer, index: number, deaths: AlamoVector3[][],
        step: number, frame: EmitterFrame,
    ): void {
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

    /** The emitter at a chain index, or null when the file names none. */
    private childOf(index: number): EmitterRenderer | null {
        return index >= 0 && index < this.emitters.length ? this.emitters[index] : null;
    }

    /**
     * Takes the bone's transform out of the simulation, and reports what the bone is doing.
     *
     * Two jobs, together because they are the same matrices. The group is a CHILD of the bone, so
     * its own transform is overwritten with whatever cancels the bone out - after which the local
     * coordinates the emitters write are the model's coordinates, and a particle that should be
     * left behind can be. What the bone is doing then becomes ordinary data the emitters can act
     * on: where to spawn, how far to carry a linked particle, what speed to hand a newborn one.
     */
    private frameOf(dt: number, view: THREE.Matrix4 | null): EmitterFrame {
        const parent = this.root.parent;

        if (parent === null || this.space === null) {
            // A system standing on its own, or one not yet attached. Its own space is the only one
            // there is, and nothing is moving relative to anything.
            this.root.updateWorldMatrix(true, false);
            this.frame.delta.set(0, 0, 0);
            this.frame.velocity.set(0, 0, 0);
            this.viewFrom(this.root, view);
            this.root.getWorldPosition(GROUND_SCRATCH);
            this.frame.groundY = -GROUND_SCRATCH.y;

            return this.frame;
        }

        // Read fresh: the animation mixer has already moved the bones this frame, but their world
        // matrices are only recomputed at render time.
        parent.updateWorldMatrix(true, false);
        this.space.updateWorldMatrix(true, false);

        const toSpace = SPACE_SCRATCH.copy(this.space.matrixWorld).invert();

        this.root.matrixAutoUpdate = false;
        this.root.matrix.copy(parent.matrixWorld).invert().multiply(this.space.matrixWorld);
        this.root.matrixWorldNeedsUpdate = true;

        this.frame.matrix.multiplyMatrices(toSpace, parent.matrixWorld);
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
        this.frame.velocity.copy(this.frame.delta).divideScalar(Math.max(dt, 1e-6));

        this.viewFrom(this.space, view);

        // Where the scene's ground plane falls in the space the particles live in - it is the
        // SCENE's ground a falling particle lands on, not the model's own height.
        this.space.getWorldPosition(GROUND_SCRATCH);
        this.frame.groundY = -GROUND_SCRATCH.y;

        return this.frame;
    }

    /**
     * Records simulation space to view space, so the streaks do not each have to ask where the
     * camera is standing.
     */
    private viewFrom(space: THREE.Object3D, view: THREE.Matrix4 | null): void {
        this.frame.toView.setFromMatrix4(view === null
            ? IDENTITY
            : VIEW_SCRATCH.multiplyMatrices(view, space.matrixWorld));
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
}

// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The engine's bloom, as the last link of the frame's pass chain.
//
// `Engine/SceneBloom.fx` calls itself "fake-HDR blooming" and describes its own shape at the top:
// "First you have to do a bright filter to pull stuff you want to glow out of the scene. Then you
// do a series of bloom passes with the code ping-ponging between two render targets to blur the
// hotspots out." Three passes, and this runs the same three:
//
//   1. BRIGHT FILTER. Not a threshold - `BloomCutoff` ships at 1.0 and luminance never reaches it,
//      so every pixel takes the else branch, `return pixel*pixel*pixel*pixel*pixel`. A fifth-power
//      roll-off keeps the near-white and crushes everything else, smoothly. This replaced three's
//      `UnrealBloomPass`, whose hard threshold switches on at an edge: with a cutoff a hull at 0.84
//      contributes nothing and at 0.86 contributes fully, where the engine has it fading in from
//      the bottom.
//   2. BLUR, four taps at the corners of a square, averaged - `pixel * 0.25f` - ping-ponged between
//      two buffers with the square widening each time.
//   3. COMBINE, `BloomStrength * texel` blended over the frame with `SrcBlend = ONE,
//      DestBlend = INVSRCCOLOR`. That is D3D's "AddSmooth": `dst + src * (1 - dst)`, which rolls off
//      as the destination approaches white instead of clipping the way a plain add does.
//
// It works in GAMMA space, on the frame exactly as the canvas drew it, because the engine composites
// through plain `D3DFMT_A8R8G8B8` surfaces and has no colour management anywhere. Nothing here
// decodes or encodes: the combine draws OVER the scene already on the canvas, so the base image is
// never resampled and cannot come back subtly different.

import * as THREE from 'three';

/**
 * `BloomCutoff` - above this luminance a pixel passes through the bright filter untouched.
 *
 * Shipped at 1.0, which luminance cannot exceed, so in practice nothing takes that branch. Kept
 * because it IS the shader's parameter and a mod could lower it.
 */
const BLOOM_CUTOFF = 1;

/** `BloomStrength` - how much of the blurred hotspot is added back. */
const BLOOM_STRENGTH = 0.1;

/** `BloomSize` - the blur's reach, in half-texels, before the per-iteration widening. */
export const BLOOM_SIZE = 0.25;

/**
 * How many times the blur is ping-ponged, and how far down the buffers are scaled.
 *
 * OURS, not the engine's. `SceneBloom.fx` says the passes ping-pong but neither the shader nor
 * AloViewer says how many times or at what resolution - AloViewer runs the three passes once each
 * straight onto the back buffer, which cannot be what the engine does with them. So the schedule is
 * a judgement call and is marked as one.
 *
 * The reach follows from the shader's own delta schedule: a tap sits `BloomSize * (1 + 2i)`
 * half-texels out, so eight iterations at quarter resolution carry light tens of screen pixels -
 * a glow around an engine nozzle rather than a haze over the whole hull.
 */
const ITERATIONS = 8;

/** Buffer scale for the blur chain. Quarter resolution, which is also four times the reach. */
const DOWNSAMPLE = 4;

/**
 * The bright filter's response to one channel.
 *
 * Exported for its tests: the curve IS the effect, and it is worth being able to state what a given
 * pixel contributes without a GPU in the room.
 */
export function brightPass(value: number, cutoff = BLOOM_CUTOFF): number {
    return value > cutoff ? value : value ** 5;
}

/**
 * How far one blur tap sits from the centre, in texture coordinates.
 *
 * `delta = BloomSize * (half_pixel + 2.0f * BloomIteration * half_pixel)`, with `half_pixel` being
 * `0.5 / width` - AloViewer fills `m_resolutionConstants.zw` with exactly that.
 */
export function blurDelta(iteration: number, size: number): number {
    return BLOOM_SIZE * (1 + 2 * iteration) * (0.5 / Math.max(1, size));
}

const QUAD_VERTEX_SHADER = `
varying vec2 vUv;

void main() {
    vUv = uv;
    gl_Position = vec4(position.xy, 0.0, 1.0);
}
`;

const BRIGHT_FRAGMENT_SHADER = `
precision mediump float;

varying vec2 vUv;

uniform sampler2D tScene;
uniform float uCutoff;

void main() {
    vec4 texel = texture2D(tScene, vUv);

    // The engine dots against (0.299, 0.587, 0.114, 0.0) - a float4 dot whose alpha weight is zero,
    // so alpha never reaches the luminance however the frame buffer was cleared.
    float luminance = dot(texel.rgb, vec3(0.299, 0.587, 0.114));

    vec3 crushed = texel.rgb * texel.rgb * texel.rgb * texel.rgb * texel.rgb;

    gl_FragColor = vec4(luminance > uCutoff ? texel.rgb : crushed, 1.0);
}
`;

const BLUR_FRAGMENT_SHADER = `
precision mediump float;

varying vec2 vUv;

uniform sampler2D tScene;
uniform vec2 uDelta;

void main() {
    // The four corners of a square, averaged. The engine reaches them through four samplers bound
    // to the same texture, which is a shader-model-1 way of writing one sampler four times.
    vec4 sum = texture2D(tScene, vUv + uDelta);
    sum += texture2D(tScene, vUv - uDelta);
    sum += texture2D(tScene, vUv + vec2(uDelta.x, -uDelta.y));
    sum += texture2D(tScene, vUv - vec2(uDelta.x, -uDelta.y));

    gl_FragColor = sum * 0.25;
}
`;

const COMBINE_FRAGMENT_SHADER = `
precision mediump float;

varying vec2 vUv;

uniform sampler2D tScene;
uniform float uStrength;

void main() {
    gl_FragColor = vec4(uStrength * texture2D(tScene, vUv).rgb, 1.0);
}
`;

/**
 * Bloom over an already-composited frame.
 *
 * Deliberately takes a TEXTURE rather than a scene: what it blooms may be the plain render or the
 * heat-bent composite, and it must not care which. It also assumes that frame is still on the
 * canvas, because the combine pass blends over it rather than redrawing it.
 */
export class BloomPass {
    /** The two buffers the blur ping-pongs between. */
    private targets: [THREE.WebGLRenderTarget, THREE.WebGLRenderTarget] | null = null;

    private readonly bright: THREE.ShaderMaterial;
    private readonly blur: THREE.ShaderMaterial;
    private readonly combine: THREE.ShaderMaterial;

    private readonly quad: THREE.Scene;
    private readonly quadCamera = new THREE.Camera();
    private readonly mesh: THREE.Mesh;

    /** Reused so a frame allocates nothing. */
    private readonly size = new THREE.Vector2();

    constructor() {
        const flat = { depthTest: false, depthWrite: false };

        this.bright = new THREE.ShaderMaterial({
            ...flat,
            uniforms: { tScene: { value: null }, uCutoff: { value: BLOOM_CUTOFF } },
            vertexShader: QUAD_VERTEX_SHADER,
            fragmentShader: BRIGHT_FRAGMENT_SHADER,
        });

        this.blur = new THREE.ShaderMaterial({
            ...flat,
            uniforms: { tScene: { value: null }, uDelta: { value: new THREE.Vector2() } },
            vertexShader: QUAD_VERTEX_SHADER,
            fragmentShader: BLUR_FRAGMENT_SHADER,
        });

        // `SrcBlend = ONE, DestBlend = INVSRCCOLOR` - the shader's own pass state, and the reason
        // this can be added straight over the canvas without blowing the highlights to flat white.
        this.combine = new THREE.ShaderMaterial({
            ...flat,
            uniforms: { tScene: { value: null }, uStrength: { value: BLOOM_STRENGTH } },
            vertexShader: QUAD_VERTEX_SHADER,
            fragmentShader: COMBINE_FRAGMENT_SHADER,
            transparent: true,
            blending: THREE.CustomBlending,
            blendSrc: THREE.OneFactor,
            blendDst: THREE.OneMinusSrcColorFactor,
            blendSrcAlpha: THREE.ZeroFactor,
            blendDstAlpha: THREE.OneFactor,
        });

        this.mesh = new THREE.Mesh(new THREE.PlaneGeometry(2, 2), this.bright);
        this.quad = new THREE.Scene();
        this.quad.add(this.mesh);
    }

    /** Blooms `frame` onto the canvas, over the picture already there. */
    render(renderer: THREE.WebGLRenderer, frame: THREE.Texture): void {
        // The combine pass ADDS to what is on the canvas, so the canvas must survive being drawn
        // to. Three clears before every render by default, which wiped the scene and left the
        // faint bloom ghost on its own - the picture simply disappeared the moment bloom was
        // switched on. Every quad here covers its whole target, so nothing wants a clear anyway.
        const autoClear = renderer.autoClear;
        renderer.autoClear = false;

        renderer.getDrawingBufferSize(this.size);
        const width = Math.max(1, Math.floor(this.size.x / DOWNSAMPLE));
        const height = Math.max(1, Math.floor(this.size.y / DOWNSAMPLE));
        const [a, b] = this.buffers(width, height);

        // 1. The hotspots, pulled out of the frame.
        this.draw(renderer, this.bright, frame, a);

        // 2. Blurred, ping-ponging, with the square widening on each pass.
        let read = a;
        let write = b;

        for (let i = 0; i < ITERATIONS; i++) {
            (this.blur.uniforms.uDelta.value as THREE.Vector2).set(
                blurDelta(i, width), blurDelta(i, height));

            this.draw(renderer, this.blur, read.texture, write);
            [read, write] = [write, read];
        }

        // 3. Added back over the frame, which is still on the canvas.
        this.draw(renderer, this.combine, read.texture, null);

        renderer.autoClear = autoClear;
    }

    private draw(
        renderer: THREE.WebGLRenderer, material: THREE.ShaderMaterial,
        source: THREE.Texture, into: THREE.WebGLRenderTarget | null,
    ): void {
        material.uniforms.tScene.value = source;
        this.mesh.material = material;

        renderer.setRenderTarget(into);
        renderer.render(this.quad, this.quadCamera);
        renderer.setRenderTarget(null);
    }

    /**
     * The ping-pong pair, built on first use and resized with the canvas.
     *
     * Plain eight-bit buffers with no colour space, because the whole chain works on the frame as
     * the canvas drew it - see the note at the top. `LinearFilter` matters: the shader's own
     * samplers all declare `MINFILTER=LINEAR; MAGFILTER=LINEAR`, and the taps sit fractions of a
     * texel apart, so nearest sampling would collapse the blur to a copy.
     */
    private buffers(
        width: number, height: number,
    ): [THREE.WebGLRenderTarget, THREE.WebGLRenderTarget] {
        if (this.targets === null) {
            const make = (): THREE.WebGLRenderTarget => {
                const target = new THREE.WebGLRenderTarget(width, height, {
                    minFilter: THREE.LinearFilter,
                    magFilter: THREE.LinearFilter,
                    depthBuffer: false,
                });

                // CLAMP on both axes, as every sampler in the effect declares - a tap that wrapped
                // would drag a hotspot from one edge of the screen to the other.
                target.texture.wrapS = THREE.ClampToEdgeWrapping;
                target.texture.wrapT = THREE.ClampToEdgeWrapping;

                return target;
            };

            this.targets = [make(), make()];
        } else if (this.targets[0].width !== width || this.targets[0].height !== height) {
            for (const target of this.targets) {
                target.setSize(width, height);
            }
        }

        return this.targets;
    }

    dispose(): void {
        for (const target of this.targets ?? []) {
            target.dispose();
        }

        this.targets = null;
        this.bright.dispose();
        this.blur.dispose();
        this.combine.dispose();
        this.mesh.geometry.dispose();
    }
}

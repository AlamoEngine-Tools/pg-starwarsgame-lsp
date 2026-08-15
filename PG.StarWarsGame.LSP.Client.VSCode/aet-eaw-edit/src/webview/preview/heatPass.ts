// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Heat haze: the one effect that bends the picture instead of adding to it.
//
// `Engine/PrimHeat.fx` reads a heat sprite's texture as a NORMAL map, offsets the screen position by
// it and samples the frame there - `out_texel = tex2D(SceneSampler, screenUv + attenuation *
// DistortionAmount * distort)`. The sprite contributes no colour whatsoever, which is why drawing
// one as an ordinary quad puts a flat violet-white sheet over the model: that is the normal map,
// added to the image it was supposed to be warping.
//
// Structured the way the engine structures it, in phases: the scene without heat into one buffer,
// the heat sprites into another, then one full-screen pass that reads the first through the second.
// The whole thing is skipped when nothing is distorting, so the ordinary path is untouched.

import * as THREE from 'three';

/**
 * How far the frame can be pulled, as a fraction of the screen.
 *
 * `float DistortionAmount = 0.01;` - the shader's own default, and no shipped emitter overrides it.
 */
const DISTORTION_AMOUNT = 0.01;

/** Zero distortion: a normal of (0.5, 0.5) decodes to no offset, and nothing is bending yet. */
const NEUTRAL_HEAT = new THREE.Color(0.5, 0.5, 0.5);

const COMPOSITE_VERTEX_SHADER = `
varying vec2 vUv;

void main() {
    vUv = uv;
    gl_Position = vec4(position.xy, 0.0, 1.0);
}
`;

/**
 * Reads the scene back through the heat buffer.
 *
 * The sRGB encode at the end is done by hand: three only applies `outputColorSpace` when it draws
 * to the canvas, and on these frames this pass is the thing drawing to the canvas. Getting it wrong
 * washes out every pixel of the preview, heat or no heat, so the two paths are pixel-compared in
 * the harness. Flat areas agree to within a unit of rounding; antialiased EDGES come out slightly
 * brighter, because a multisampled sRGB buffer resolves its samples in linear light while the
 * canvas averages them already encoded. That is the more correct of the two, and it only shows on
 * the frames where something is distorting.
 */
const COMPOSITE_FRAGMENT_SHADER = `
precision mediump float;

varying vec2 vUv;

uniform sampler2D uScene;
uniform sampler2D uHeat;
uniform float uAmount;
uniform float uDebug;

vec3 encodeSrgb(vec3 value) {
    return mix(
        pow(value, vec3(0.41666)) * 1.055 - vec3(0.055),
        value * 12.92,
        vec3(lessThanEqual(value, vec3(0.0031308))));
}

void main() {
    vec4 heat = texture2D(uHeat, vUv);

    // The normal, decoded from its 0..1 storage, scaled by how strongly this pixel is distorting.
    vec2 offset = 2.0 * (heat.xy - 0.5) * heat.a * uAmount;

    // The debug view draws the heat BUFFER itself - the normal in rg and the strength in a - which
    // is what AloViewer's "Debug Heat" shows. It is the only way to see a sprite whose whole effect
    // is a displacement: a bent picture tells you something is bending it, never which pixels.
    //
    // An empty field reads as BLACK rather than as mid grey, and that is correct: three premultiplies
    // the clear colour, so clearing to the neutral normal at alpha zero stores zeros. Nothing
    // distorting therefore looks like nothing, which is the honest answer.
    if (uDebug > 0.5) {
        gl_FragColor = vec4(heat.rg, heat.a, 1.0);
        return;
    }

    gl_FragColor = vec4(encodeSrgb(texture2D(uScene, vUv + offset).rgb), 1.0);
}
`;

/**
 * The two buffers and the full-screen pass that reads one through the other.
 *
 * Owned by the viewport and used only on the frames where something is actually distorting - the
 * targets are allocated on first use and resized to follow the canvas.
 */
export class HeatPass {
    private scene: THREE.WebGLRenderTarget | null = null;
    private heat: THREE.WebGLRenderTarget | null = null;

    private readonly composite: THREE.ShaderMaterial;
    private readonly quad: THREE.Scene;
    private readonly quadCamera = new THREE.Camera();

    /** Reused so a frame of rendering allocates nothing. */
    private readonly size = new THREE.Vector2();
    private readonly previousClear = new THREE.Color();

    constructor(private readonly heatLayer: number) {
        this.composite = new THREE.ShaderMaterial({
            uniforms: {
                uScene: { value: null },
                uHeat: { value: null },
                uAmount: { value: DISTORTION_AMOUNT },
                uDebug: { value: 0 },
            },
            vertexShader: COMPOSITE_VERTEX_SHADER,
            fragmentShader: COMPOSITE_FRAGMENT_SHADER,
            depthTest: false,
            depthWrite: false,
        });

        this.quad = new THREE.Scene();
        this.quad.add(new THREE.Mesh(new THREE.PlaneGeometry(2, 2), this.composite));
    }

    /**
     * Draws the scene with the heat sprites bending it.
     *
     * The camera's layer mask is borrowed for the heat phase and put back afterwards, so a caller
     * that has its own idea of which layers to draw keeps it.
     *
     * `into` sends the composited result to a render target instead of the canvas, which is what
     * lets a later pass - bloom - take this frame as its input. Null means straight to the canvas,
     * which is the ordinary path and costs nothing extra.
     *
     * `debug` draws the heat buffer itself rather than the bent picture.
     */
    render(
        renderer: THREE.WebGLRenderer, scene: THREE.Scene, camera: THREE.Camera,
        into: THREE.WebGLRenderTarget | null = null, debug = false,
    ): void {
        this.resize(renderer);

        const targets = { scene: this.scene!, heat: this.heat! };
        const layers = camera.layers.mask;
        const background = scene.background;

        renderer.getClearColor(this.previousClear);
        const clearAlpha = renderer.getClearAlpha();

        // The picture, without anything that bends it.
        renderer.setRenderTarget(targets.scene);
        renderer.render(scene, camera);

        // Then the benders, alone, onto a field that bends nothing. The scene's background has to
        // stand down for this pass or it would paint over the neutral field with a flat colour
        // that decodes to a hard pull to one side.
        camera.layers.set(this.heatLayer);
        scene.background = null;
        renderer.setClearColor(NEUTRAL_HEAT, 0);
        renderer.setRenderTarget(targets.heat);
        renderer.clear();
        renderer.render(scene, camera);

        camera.layers.mask = layers;
        scene.background = background;
        renderer.setClearColor(this.previousClear, clearAlpha);

        this.composite.uniforms.uScene.value = targets.scene.texture;
        this.composite.uniforms.uHeat.value = targets.heat.texture;
        this.composite.uniforms.uDebug.value = debug ? 1 : 0;

        renderer.setRenderTarget(into);
        renderer.render(this.quad, this.quadCamera);
    }

    /** Matches the buffers to the canvas, building them the first time round. */
    private resize(renderer: THREE.WebGLRenderer): void {
        renderer.getDrawingBufferSize(this.size);
        const width = Math.max(1, this.size.x);
        const height = Math.max(1, this.size.y);

        if (this.scene === null || this.heat === null) {
            // Multisampled to match the `antialias: true` canvas: without it, routing a frame
            // through a buffer would put the jagged edges back on every model.
            //
            // sRGB, not linear. An eight-bit buffer holding LINEAR light quantises the darks
            // savagely - banding across every shaded hull - and the canvas it stands in for holds
            // sRGB. Storing it the same way keeps the two paths within a rounding error of each
            // other; the hardware decodes on sample and the composite re-encodes.
            //
            // `stencilBuffer`: the shadow volumes count into the stencil DURING this scene render,
            // and a target without one has nothing for the darken quad to test against - so it
            // covers the whole frame and multiplies every pixel by the shadow colour. At the
            // default 0.5 that is the entire picture at half brightness, the moment any effect with
            // a heat emitter is switched on. `frameBuffer()` in viewport.ts carries the same note
            // for the same reason; this target was missed.
            this.scene = new THREE.WebGLRenderTarget(
                width, height, { samples: 4, stencilBuffer: true });
            this.scene.texture.colorSpace = THREE.SRGBColorSpace;

            // The heat buffer is DATA - a normal and a strength - so it stays linear. Encoding it
            // would bend the frame by the wrong amount everywhere.
            this.heat = new THREE.WebGLRenderTarget(width, height, { depthBuffer: false });
            return;
        }

        if (this.scene.width !== width || this.scene.height !== height) {
            this.scene.setSize(width, height);
            this.heat.setSize(width, height);
        }
    }

    dispose(): void {
        this.scene?.dispose();
        this.heat?.dispose();
        this.composite.dispose();

        for (const child of this.quad.children) {
            if (child instanceof THREE.Mesh) {
                child.geometry.dispose();
            }
        }
    }
}

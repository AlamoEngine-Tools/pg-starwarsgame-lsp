// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Heat haze: the one effect that bends the picture instead of adding to it.
//
// Built to match the reference renderer rather than reasoned out. AloViewer's `Render.cpp` runs it
// in three steps and this runs the same three:
//
//   1. Draw the scene normally. With multisampling on it goes to the BACK BUFFER and is then copied
//      out (`StretchRect(pBackBuffer -> pHeatSurface)`), so the texture the composite reads is the
//      exact image the viewer would otherwise have seen.
//   2. Clear a distortion buffer to a neutral normal and draw the heat sprites into it, alpha
//      blended - `HeatSaturationRenderer::OverrideStates` forces SRCALPHA / INVSRCALPHA whatever
//      blend mode the emitter declares.
//   3. Draw one full-screen quad with `Engine/SceneHeat.fx`, which reads the scene through the
//      distortion field.
//
// `SceneHeat.fx`'s whole pixel shader is three lines, and they say exactly what the offset is:
//
//     half4 distortion_pixel = tex2D(DistortionSampler, In.Tex0);
//     half4 distortion = DistortionAmount * 2.0 * (distortion_pixel - 0.5);
//     half4 scene_pixel = tex2D(SceneSampler, In.Tex0 + distortion.xy);
//
// No alpha term anywhere. This used to multiply the offset by the distortion buffer's accumulated
// alpha, which is not in the shader and which the emitters make tiny - `Large_Explosion_Space_Empire`
// authors its `heat` emitter at alpha 0.196 - so the picture barely moved. It also cleared the
// buffer at alpha ZERO, and three premultiplies a clear colour, so the neutral 0.5 was stored as 0
// and every pixel the sprites did not cover asked for a hard pull to one side. The clear is opaque
// now, exactly as `D3DCOLOR_XRGB(129,128,255)` is.

import * as THREE from 'three';

/**
 * How far the frame can be pulled, as a fraction of the screen.
 *
 * AloViewer's own choice: `m_heatEffect->GetEffect()->SetFloat("DistortionAmount", 0.05f)`.
 *
 * This was 0.01, which is the constant in `Engine/PrimHeat.fx` - the per-SPRITE heat shader, where
 * each quad samples the scene itself. That is a different technique from the one implemented here,
 * and its constant does not belong to it. `SceneHeat.fx`, the full-screen composite this actually
 * is, declares 0.25 and the reference renderer turns it down to 0.05.
 */
const DISTORTION_AMOUNT = 0.05;

/**
 * The channel value that means "no offset here".
 *
 * An eight-bit channel cannot hold 0.5, so the neutral is 128/255 and the shader subtracts THAT
 * rather than 0.5 - otherwise the whole frame slides by a fifth of a pixel wherever nothing is
 * distorting, which is a bug that would be very hard to see and very easy to leave in.
 *
 * AloViewer clears to `D3DCOLOR_XRGB(129, 128, 255)`, whose red is a channel step further out
 * still. Not copied: that bias belongs to the fixed-function `texbem` stage its DX8 technique goes
 * through, and reproducing it in a shader that does the arithmetic directly would just displace
 * every pixel by about one. The OPAQUE part of that clear is copied, and matters - the sprites
 * alpha-blend over the field, so a transparent clear leaves nothing for them to blend with.
 */
const NEUTRAL_CHANNEL = 128 / 255;

/** Zero distortion, as a colour to clear with. */
const NEUTRAL_HEAT = new THREE.Color(NEUTRAL_CHANNEL, NEUTRAL_CHANNEL, NEUTRAL_CHANNEL);

const COMPOSITE_VERTEX_SHADER = `
varying vec2 vUv;

void main() {
    vUv = uv;
    gl_Position = vec4(position.xy, 0.0, 1.0);
}
`;

/**
 * Reads the scene back through the distortion field.
 *
 * The scene texture holds the frame EXACTLY as the canvas already drew it - encoded, antialiased,
 * finished - because it is a copy of the canvas rather than a re-render into an offscreen buffer.
 * That is what stopped this pass from changing the whole picture. Rendering the scene into a render
 * target instead makes three write LINEAR values (its output conversion only applies when it draws
 * to the canvas), so the multisample resolve averaged in linear light while the canvas averages
 * encoded - and on a viewport full of thin bright lines that lifted every edge by 20 to 60
 * luminance the moment any heat effect appeared. AloViewer copies the back buffer for the same
 * reason: whatever it hands the composite is the picture, not a second rendering of it.
 *
 * So there is nothing to encode on the way out at all: encoded pixels in, the same pixels back out,
 * moved. Bloom reads this pass's result the same way this pass reads the scene - by copying the
 * canvas - so no step in the chain ever writes into a colour-managed buffer.
 */
const COMPOSITE_FRAGMENT_SHADER = `
precision mediump float;

varying vec2 vUv;

uniform sampler2D uScene;
uniform sampler2D uHeat;
uniform float uAmount;
uniform float uDebug;
uniform float uNeutral;

void main() {
    vec4 heat = texture2D(uHeat, vUv);

    // DistortionAmount * 2.0 * (distortion_pixel - 0.5), and nothing else - see the note at the
    // top. The buffer's alpha channel is not part of the offset in the shipped shader.
    vec2 offset = 2.0 * (heat.xy - uNeutral) * uAmount;

    // The debug view draws the distortion FIELD itself, which is the only way to see an effect
    // whose whole contribution is a displacement. Mid grey means nothing is bending there, which is
    // what the neutral clear stores.
    if (uDebug > 0.5) {
        gl_FragColor = vec4(heat.rgb, 1.0);
        return;
    }

    gl_FragColor = vec4(texture2D(uScene, vUv + offset).rgb, 1.0);
}
`;

/**
 * The two buffers and the full-screen pass that reads one through the other.
 *
 * Owned by the viewport and used only on the frames where something is actually distorting - the
 * targets are allocated on first use and resized to follow the canvas.
 */
export class HeatPass {
    /** A copy of the finished frame, taken off the canvas rather than rendered again. */
    private frame: THREE.FramebufferTexture | null = null;
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
                uNeutral: { value: NEUTRAL_CHANNEL },
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
     * The camera's layer mask is borrowed for the distortion phase and put back afterwards, so a
     * caller that has its own idea of which layers to draw keeps it.
     *
     * Always draws to the canvas. A later pass - bloom - takes this frame by copying the canvas,
     * exactly as this pass takes the scene, so nothing here needs to know whether one follows.
     *
     * `debug` draws the distortion field itself rather than the bent picture.
     */
    render(
        renderer: THREE.WebGLRenderer, scene: THREE.Scene, camera: THREE.Camera, debug = false,
    ): void {
        this.resize(renderer);

        const layers = camera.layers.mask;
        const background = scene.background;

        renderer.getClearColor(this.previousClear);
        const clearAlpha = renderer.getClearAlpha();

        // The picture, drawn to the CANVAS and copied off it - the reference renderer's
        // `StretchRect(pBackBuffer -> pHeatSurface)`. Drawing to the canvas is what gets the
        // renderer's own output encoding and the canvas's own antialiasing; copying is what makes
        // the composite read that exact image rather than a second, differently-resolved one.
        //
        // Nothing is presented until the frame ends, so the canvas holding the undistorted picture
        // for a moment on its way to being read back is invisible.
        renderer.setRenderTarget(null);
        renderer.render(scene, camera);
        renderer.copyFramebufferToTexture(this.frame!);

        // Then the benders, alone, onto a field that bends nothing. The scene's background has to
        // stand down for this pass or it would paint over the neutral field with a flat colour
        // that decodes to a hard pull to one side.
        camera.layers.set(this.heatLayer);
        scene.background = null;
        renderer.setClearColor(NEUTRAL_HEAT, 1);
        renderer.setRenderTarget(this.heat!);
        renderer.clear();
        renderer.render(scene, camera);

        camera.layers.mask = layers;
        scene.background = background;
        renderer.setClearColor(this.previousClear, clearAlpha);

        this.composite.uniforms.uScene.value = this.frame;
        this.composite.uniforms.uHeat.value = this.heat!.texture;
        this.composite.uniforms.uDebug.value = debug ? 1 : 0;

        renderer.setRenderTarget(null);
        renderer.render(this.quad, this.quadCamera);
    }

    /** Matches the buffers to the canvas, building them the first time round. */
    private resize(renderer: THREE.WebGLRenderer): void {
        renderer.getDrawingBufferSize(this.size);
        const width = Math.max(1, this.size.x);
        const height = Math.max(1, this.size.y);

        if (this.frame === null || this.heat === null) {
            // A copy of the canvas, so it needs neither samples nor a colour space: the pixels
            // arriving are already resolved and already encoded, and telling the sampler they are
            // sRGB would have the hardware decode them a second time.
            this.frame = new THREE.FramebufferTexture(width, height);

            // The distortion field is DATA - a normal - so it stays linear, and it has no depth
            // buffer because sprites drawn into it never test against one.
            this.heat = new THREE.WebGLRenderTarget(width, height, { depthBuffer: false });
            return;
        }

        if (this.frame.image.width !== width || this.frame.image.height !== height) {
            // A framebuffer texture cannot be resized in place - the copy would land in a buffer of
            // the wrong shape - so it is rebuilt.
            this.frame.dispose();
            this.frame = new THREE.FramebufferTexture(width, height);
            this.heat.setSize(width, height);
        }
    }

    dispose(): void {
        this.frame?.dispose();
        this.heat?.dispose();
        this.composite.dispose();

        for (const child of this.quad.children) {
            if (child instanceof THREE.Mesh) {
                child.geometry.dispose();
            }
        }
    }
}

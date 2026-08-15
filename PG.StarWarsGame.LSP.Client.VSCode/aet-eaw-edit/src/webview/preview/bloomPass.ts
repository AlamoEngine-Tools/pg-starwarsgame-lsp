// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The engine's bloom, as the last link of the frame's pass chain.
//
// It has to be a chain rather than a second independent hack, because the heat pass already owns
// compositing: it renders the scene and the heat sprites into separate buffers and reads one
// through the other. Bolting bloom on beside that would leave two passes each believing they draw
// the final image, and whichever ran last would erase the other. So the order is written down once
// here - scene, then heat, then bloom, then the canvas - and each step hands its result to the next
// as a texture.
//
// `UnrealBloomPass` is three's own, already vendored with the copy of three this bundles. Writing a
// bright-pass and a separable blur by hand would be reimplementing it a little worse.

import * as THREE from 'three';
import { EffectComposer } from 'three/examples/jsm/postprocessing/EffectComposer.js';
import { TexturePass } from 'three/examples/jsm/postprocessing/TexturePass.js';
import { UnrealBloomPass } from 'three/examples/jsm/postprocessing/UnrealBloomPass.js';

/**
 * How much of the frame's brightness spills.
 *
 * Restrained on purpose: this stands in for a 2006 engine's bloom over models whose glows are
 * already additive passes, so a strong setting turns every engine nozzle into a white disc and
 * hides the geometry the preview exists to show.
 */
const STRENGTH = 0.45;

/** How far the spill reaches, as a fraction of the smaller screen dimension. */
const RADIUS = 0.35;

/**
 * How bright a pixel must be before it spills at all.
 *
 * Above the lit hull and below the glows: a lower threshold blooms the whole model, which reads as
 * a preview that has lost focus rather than as an effect.
 */
const THRESHOLD = 0.85;

/**
 * Bloom over an already-composited frame.
 *
 * Deliberately takes a TEXTURE rather than a scene: what it blooms may be the plain render or the
 * heat-bent composite, and it must not care which.
 */
export class BloomPass {
    private readonly composer: EffectComposer;
    private readonly source: TexturePass;
    private readonly bloom: UnrealBloomPass;

    /** Reused so a frame allocates nothing. */
    private readonly size = new THREE.Vector2();

    constructor(renderer: THREE.WebGLRenderer) {
        this.composer = new EffectComposer(renderer);

        // The composer's own read/write buffers are never rendered into by a scene pass here - the
        // frame arrives as a texture - so the first pass is the source and it clears nothing.
        this.source = new TexturePass(null as unknown as THREE.Texture);
        this.bloom = new UnrealBloomPass(new THREE.Vector2(1, 1), STRENGTH, RADIUS, THRESHOLD);

        this.composer.addPass(this.source);
        this.composer.addPass(this.bloom);
        this.bloom.renderToScreen = true;
    }

    /** Blooms `frame` onto the canvas. */
    render(renderer: THREE.WebGLRenderer, frame: THREE.Texture): void {
        renderer.getDrawingBufferSize(this.size);
        const width = Math.max(1, this.size.x);
        const height = Math.max(1, this.size.y);

        this.composer.setSize(width, height);
        this.bloom.setSize(width, height);
        this.source.map = frame;

        // Delta is unused by both passes; the composer's signature wants one.
        this.composer.render(0);
    }

    dispose(): void {
        this.bloom.dispose();
        this.composer.dispose();
    }
}

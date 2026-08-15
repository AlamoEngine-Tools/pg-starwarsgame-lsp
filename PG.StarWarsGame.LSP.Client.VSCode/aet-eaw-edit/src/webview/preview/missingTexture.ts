// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// What a particle draws when its texture is not there.
//
// It used to draw a plain white quad tinted by the emitter's colour - which is indistinguishable
// from an emitter that is MEANT to be a soft coloured blob, and there are plenty of those. So a
// missing texture read as a slightly wrong effect rather than as a missing file, which is the one
// thing a modding tool must never let happen quietly.
//
// The engine's own answer is to draw the Petroglyph logo, which is unmistakable at any size and any
// tint. This does the same with the AET logo the extension already ships as its icon.

import * as THREE from 'three';

/**
 * The glyph, as bytes.
 *
 * Imported rather than fetched: `localResourceRoots` only covers `out/`, so a file under
 * `resources/` is not addressable from the webview at all, and the CSP already admits `data:` for
 * images. esbuild inlines it at build time - see the `.png` loader in esbuild.js.
 */
import logo from '../../../resources/icons/alamo-engine-tools.png';

export const MISSING_TEXTURE_SOURCE = logo;

/** The side of the marker tile. Small: it is a warning, not artwork. */
export const MISSING_TEXTURE_SIZE = 128;

/**
 * Recolours a decoded glyph into a marker a particle can actually draw.
 *
 * Two changes, both of which matter for how particles blend:
 *
 * - **White, not black.** The logo ships as black on transparent, and an additive blend adds the
 *   colour and ignores the alpha - so a black glyph adds nothing and the marker never appears.
 * - **Black outside the glyph.** Same reason from the other side: additive would add whatever sits
 *   in the transparent region, and anything but black paints a square over the scene.
 *
 * Coverage is carried through untouched rather than thresholded, so the curves stay smooth at the
 * handful of pixels a particle is usually drawn at.
 */
export function markerPixels(glyph: Uint8ClampedArray): Uint8ClampedArray {
    const marker = new Uint8ClampedArray(glyph.length);

    for (let at = 0; at < glyph.length; at += 4) {
        const coverage = glyph[at + 3];
        const lit = coverage > 0 ? 255 : 0;

        marker[at] = lit;
        marker[at + 1] = lit;
        marker[at + 2] = lit;
        marker[at + 3] = coverage;
    }

    return marker;
}

/**
 * The marker, decoded once and shared.
 *
 * Memoised on the promise rather than on the texture, so several emitters asking at once produce
 * one decode rather than a race between several.
 */
let pending: Promise<THREE.Texture> | null = null;

export function missingTexture(): Promise<THREE.Texture> {
    pending ??= decode();
    return pending;
}

async function decode(): Promise<THREE.Texture> {
    const image = new Image();
    image.src = MISSING_TEXTURE_SOURCE;
    await image.decode();

    const canvas = document.createElement('canvas');
    canvas.width = MISSING_TEXTURE_SIZE;
    canvas.height = MISSING_TEXTURE_SIZE;

    const context = canvas.getContext('2d');

    if (context === null) {
        throw new Error('no 2d context for the missing-texture marker');
    }

    context.drawImage(image, 0, 0, MISSING_TEXTURE_SIZE, MISSING_TEXTURE_SIZE);

    const decoded = context.getImageData(0, 0, MISSING_TEXTURE_SIZE, MISSING_TEXTURE_SIZE);
    const texture = new THREE.DataTexture(
        markerPixels(decoded.data), MISSING_TEXTURE_SIZE, MISSING_TEXTURE_SIZE);

    // Clamped, because it is one image rather than a tile, and linear because a particle draws it
    // at whatever handful of pixels it happens to occupy.
    texture.wrapS = THREE.ClampToEdgeWrapping;
    texture.wrapT = THREE.ClampToEdgeWrapping;
    texture.minFilter = THREE.LinearMipmapLinearFilter;
    texture.magFilter = THREE.LinearFilter;
    texture.generateMipmaps = true;
    texture.colorSpace = THREE.SRGBColorSpace;
    texture.needsUpdate = true;

    return texture;
}

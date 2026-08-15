// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// How a decoded texture should be addressed. Kept free of three.js and the DOM so it can be tested.

/** What to set on a texture's wrapS/wrapT. */
export type Addressing = 'repeat' | 'clamp';

/**
 * The addressing mode a texture of this size should use.
 *
 * Every `sampler_state` in the shipped shaders declares `AddressU = WRAP; AddressV = WRAP;`, and
 * three.js defaults to clamp-to-edge. That difference is invisible on the 46% of sub-meshes whose
 * UVs stay inside the unit square and ruins the rest: 3497 of 6482 sub-meshes in the shipped models
 * address outside it - `Eb_station_04_hp04_lc` reaches -953 - and under clamp every one of those
 * smears its edge texel across the surface, which reads as stretching.
 *
 * Repeat needs power-of-two dimensions, because WebGL1 renders a repeating non-power-of-two texture
 * black - a worse failure than the one being fixed. That costs nothing here: of 1750 shipped
 * textures only 24 are NPOT, and every one of those is interface art rather than model skin.
 */
export function addressingFor(width: number | undefined, height: number | undefined): Addressing {
    return isPowerOfTwo(width) && isPowerOfTwo(height) ? 'repeat' : 'clamp';
}

function isPowerOfTwo(value: number | undefined): boolean {
    return value !== undefined && Number.isInteger(value) && value > 0 && (value & (value - 1)) === 0;
}

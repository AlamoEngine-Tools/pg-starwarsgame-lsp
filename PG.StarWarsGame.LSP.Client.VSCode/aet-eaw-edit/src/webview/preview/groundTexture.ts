// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The grain on the ground plane.
//
// The floor used to be one flat colour on a perfectly flat plane, and it read as a polished ballroom
// or as dark still liquid - which made every model look like it was floating over the scene rather
// than standing in it. Nothing about the material was wrong: `MeshStandardMaterial` at roughness 1
// still carries a dielectric sheen, and with a raking key light that sheen sweeps an unbroken
// gradient across a surface that has no detail to interrupt it. A surface reads as a surface when
// light catches its texture unevenly, so this gives it some.
//
// Generated rather than shipped, because an image asset would have to be embedded in the bundle and
// this is four lines of arithmetic. Value noise on a WRAPPING lattice, so the tile meets itself on
// every edge - the floor repeats it, and a seam would draw a grid across the scene.

/** One side of the tile, in pixels. Enough grain to read close up, small enough to build inline. */
export const GROUND_TEXTURE_SIZE = 256;

/**
 * How dark and how light the grain goes, near the top of the range.
 *
 * Near white, NOT mid grey, because both maps this feeds are MULTIPLIERS. A mid-grey albedo map
 * took the floor five times darker than its own colour, and a mid-grey roughness map halved the
 * roughness - making the ground shinier, which is the exact opposite of the point. Sitting near 1
 * means the tile modulates the material rather than replacing it.
 *
 * Narrow, too. This is breaking up a sheen, not painting camouflage: wider and the floor becomes a
 * pattern competing with the model, and a value reaching white would drag the bloom threshold with
 * it.
 */
const BAND_CENTRE = 214;
const BAND_HALF_WIDTH = 26;

/** Octaves, coarsest first: the lattice period in cells, and how much each contributes. */
const OCTAVES: readonly { period: number; weight: number }[] = [
    { period: 4, weight: 0.5 },
    { period: 16, weight: 0.32 },
    { period: 64, weight: 0.18 },
];

/**
 * A fixed hash, so the floor is the same every session.
 *
 * A remembered floor is part of knowing where you are; one that reshuffled itself on every redraw
 * would be movement in the corner of the eye with no cause.
 */
function lattice(x: number, y: number, period: number): number {
    // Wrapped, which is what makes the tile seamless: the cell one past the edge IS the first cell.
    const wrapped = ((x % period) + period) % period + (((y % period) + period) % period) * period;
    const hashed = Math.sin(wrapped * 12.9898 + period * 78.233) * 43758.5453;

    return hashed - Math.floor(hashed);
}

/** Smoothstep, so the cells blend into grain rather than into visible diamonds. */
function ease(t: number): number {
    return t * t * (3 - 2 * t);
}

function octave(u: number, v: number, period: number): number {
    const x = u * period;
    const y = v * period;
    const cellX = Math.floor(x);
    const cellY = Math.floor(y);
    const fx = ease(x - cellX);
    const fy = ease(y - cellY);

    const top = lattice(cellX, cellY, period) * (1 - fx) + lattice(cellX + 1, cellY, period) * fx;
    const bottom = lattice(cellX, cellY + 1, period) * (1 - fx)
        + lattice(cellX + 1, cellY + 1, period) * fx;

    return top * (1 - fy) + bottom * fy;
}

/**
 * A tile of dull, even grain, as RGBA bytes.
 *
 * Grey, so the material's own colour decides what the ground is made of and this only decides how
 * evenly it catches the light.
 */
export function concreteNoise(): Uint8ClampedArray {
    const pixels = new Uint8ClampedArray(GROUND_TEXTURE_SIZE * GROUND_TEXTURE_SIZE * 4);

    for (let y = 0; y < GROUND_TEXTURE_SIZE; y++) {
        for (let x = 0; x < GROUND_TEXTURE_SIZE; x++) {
            const u = x / GROUND_TEXTURE_SIZE;
            const v = y / GROUND_TEXTURE_SIZE;

            let sum = 0;
            let total = 0;

            for (const { period, weight } of OCTAVES) {
                sum += octave(u, v, period) * weight;
                total += weight;
            }

            const value = Math.round(
                BAND_CENTRE + (sum / total - 0.5) * 2 * BAND_HALF_WIDTH);
            const at = (y * GROUND_TEXTURE_SIZE + x) * 4;

            pixels[at] = value;
            pixels[at + 1] = value;
            pixels[at + 2] = value;
            pixels[at + 3] = 255;
        }
    }

    return pixels;
}

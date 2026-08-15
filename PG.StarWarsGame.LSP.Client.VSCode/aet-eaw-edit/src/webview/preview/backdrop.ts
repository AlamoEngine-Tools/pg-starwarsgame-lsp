// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// What is behind the model.
//
// A flat grey reads as "no background", which is honest but makes it hard to judge a hull: a
// starfield gives a space model something to sit against, and a sky with a horizon does the same for
// a ground one. Both are backdrops rather than scenery - they carry no light of their own and are
// never lit, so they cannot be mistaken for part of the subject.
//
// The maths is here and three.js is not, so the star distribution and the gradient can be tested
// without a GPU. Both are DETERMINISTIC for a given seed: a screenshot that compares against another
// is worth more than a sky that is different every time.

/** How many stars. Enough to read as a field, few enough that it is one draw of a small buffer. */
export const STAR_COUNT = 1500;

/**
 * Star directions on the unit sphere, as flat xyz triples.
 *
 * Spread by AREA rather than by angle: picking a latitude uniformly crowds both poles, and the sky
 * ends up with an obvious clump directly above and below the model that reads as a rendering fault
 * rather than as a sky. Taking `y` uniformly and the ring radius from it is the standard fix, and
 * costs nothing.
 */
export function starPositions(seed: number): Float32Array {
    const random = seededRandom(seed);
    const positions = new Float32Array(STAR_COUNT * 3);

    for (let i = 0; i < STAR_COUNT; i++) {
        const y = random() * 2 - 1;
        const ring = Math.sqrt(Math.max(0, 1 - y * y));
        const angle = random() * Math.PI * 2;

        positions[i * 3] = Math.cos(angle) * ring;
        positions[i * 3 + 1] = y;
        positions[i * 3 + 2] = Math.sin(angle) * ring;
    }

    return positions;
}

/** The zenith, straight overhead. */
const ZENITH: readonly [number, number, number] = [0.16, 0.33, 0.61];

/** And at eye level, where the atmosphere is thickest and the colour washes out. */
const HORIZON: readonly [number, number, number] = [0.58, 0.68, 0.78];

/**
 * The clear-sky colour at a height on the dome, `1` overhead and `0` at the horizon.
 *
 * Held at the horizon's colour below zero rather than continuing the ramp: the dome is a full
 * sphere so the camera can look down from above, and inverting the gradient there would put a
 * second, darker sky under the model where the ground should be.
 */
export function skyGradient(height: number): [number, number, number] {
    const t = Math.min(1, Math.max(0, height));

    return [
        HORIZON[0] + (ZENITH[0] - HORIZON[0]) * t,
        HORIZON[1] + (ZENITH[1] - HORIZON[1]) * t,
        HORIZON[2] + (ZENITH[2] - HORIZON[2]) * t,
    ];
}

/**
 * A small deterministic generator.
 *
 * Its own rather than the particle simulation's, so a change to how effects scatter cannot silently
 * rearrange the sky and invalidate every screenshot taken against it.
 */
function seededRandom(seed: number): () => number {
    let state = (seed | 0) || 1;

    return () => {
        state = (state * 1664525 + 1013904223) | 0;
        return ((state >>> 0) % 1000000) / 1000000;
    };
}

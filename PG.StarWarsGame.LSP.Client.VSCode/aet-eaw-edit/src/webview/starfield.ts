// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The sky behind the credits crawl.
//
// This replaces a handful of CSS radial gradients tiled at a fixed pitch. Tiling is what made the
// old field look wrong: the same six dots recurred every couple of hundred pixels, and once seen
// the grid could not be unseen. Positions are generated instead, so nothing repeats.

export interface Star {
    /** Percentage across the field, so the sky scales with the panel. */
    x: number;
    y: number;
    /** Pixels. Varied, because a sky of identical dots reads as a texture rather than as stars. */
    size: number;
    opacity: number;
}

/**
 * Generates a field of stars.
 *
 * Deterministic for a given seed: the sky is built once and must not change between renders, or it
 * would twinkle every time the crawl advanced.
 */
export function generateStars(count: number, seed = 20250801): Star[] {
    const random = mulberry32(seed);
    const stars: Star[] = [];

    for (let i = 0; i < count; i++) {
        // Most stars are faint pinpricks and a few are brighter - squaring the roll skews the
        // distribution that way, which is what keeps it from looking like even scatter.
        const brightness = random() ** 2;

        stars.push({
            x: random() * 100,
            y: random() * 100,
            size: brightness > 0.85 ? 2 : brightness > 0.55 ? 1.5 : 1,
            opacity: 0.2 + brightness * 0.75,
        });
    }

    return stars;
}

/** Small deterministic PRNG - enough for scattering dots, and no dependency. */
function mulberry32(seed: number): () => number {
    let a = seed >>> 0;
    return () => {
        a = (a + 0x6D2B79F5) >>> 0;
        let t = Math.imul(a ^ (a >>> 15), 1 | a);
        t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
        return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
    };
}

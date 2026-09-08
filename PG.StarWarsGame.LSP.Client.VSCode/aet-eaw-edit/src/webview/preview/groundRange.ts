// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// How far the ground can be moved, and in what increments.
//
// Ground height used to be a number box, on the reasoning that no one slider range suits a corpus
// running from a two-unit trooper to a thousand-unit command centre. That reasoning was right about
// a FIXED range and wrong about the control: the range comes from the subject, so the track means
// the same thing on every model - one end is a subject's radius below the origin, the other is a
// radius above it, and the middle is zero, where the model stands.

/** Nothing loaded yet, or a subject with no size to speak of. */
const FALLBACK = 100;

/** How many steps make up the whole track. */
const STEPS = 100;

/** The slider's half-range and its increment, both rounded to numbers worth reading. */
export function groundRange(radius: number): { limit: number; step: number } {
    const limit = Number.isFinite(radius) && radius > 0 ? niceCeil(radius) : FALLBACK;

    return { limit, step: niceCeil(limit / STEPS) };
}

/** The next 1, 2 or 5 times a power of ten at or above a value. */
function niceCeil(value: number): number {
    const decade = 10 ** Math.floor(Math.log10(value));

    for (const step of [1, 2, 5]) {
        // A hair of slack, because 10 ** log10(x) does not land exactly on x - without it a value
        // that IS already round, like 2, is rounded up to the next rung and the track doubles.
        if (value <= step * decade * 1.000000001) {
            return step * decade;
        }
    }

    return 10 * decade;
}

/**
 * Pulls a value within `pull` steps of zero onto zero.
 *
 * Zero is where the model stands, so it is the one position on the ground track anybody returns to
 * - and it is the hardest to hit, because the track is two hundred positions wide and a step is
 * about a pixel and a half. The detent costs the single position either side of it, which on any
 * model is a fraction of a percent of its own size; the arrow keys pass `pull: 0` and keep it.
 */
export function snapToZero(value: number, step: number, pull = 1): number {
    // A tolerance, because a step is routinely something like 0.02 and `0.02 * 1` is not exactly
    // 0.02 in binary - without it the detent silently stops working on small models.
    return Math.abs(value) <= step * pull * 1.000000001 ? 0 : value;
}

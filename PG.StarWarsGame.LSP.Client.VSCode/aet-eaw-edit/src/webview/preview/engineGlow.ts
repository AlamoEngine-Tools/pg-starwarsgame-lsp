// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// How bright a ship's engine effects are drawn.
//
// Measured in the game: the space locomotor sets the brightness of the engine effects on every
// service - and an emitter's brightness multiplies the red, green, blue and alpha of each of its
// particles as they are drawn. While the engines are online it is 0.2 + 0.8 x speed / Max_Speed,
// clamped to 0..1, and 0 for a type whose Max_Speed is not above 0. While they are disabled for a
// time - a hit that drained the ship's power, from a projectile that disables engines - the effects
// stay drawn and flicker: a noise value between 0 and 1, read anew each game frame, times 0.2.

/** The game's frames a second, which the flicker steps at. */
const GAME_FRAMES_PER_SECOND = 30;

/** The brightness of a ship at rest, and the share its speed adds up to full. */
const AT_REST = 0.2;
const BY_SPEED = 0.8;

/** The most a disabled engine flickers up to. */
const FLICKER_PEAK = 0.2;

/**
 * The brightness of the engine effects of a ship whose engines are online.
 *
 * @param speedShare The ship's speed as a share of its `Max_Speed`, 0 at rest to 1 at full speed.
 * @param maxSpeed The type's `Max_Speed`, or null where it writes none - which is taken as a ship
 *     that can move: what the game's default for the tag is has not been measured.
 */
export function engineBrightness(
    speedShare: number,
    maxSpeed: number | null,
): number {
    if (maxSpeed !== null && !(maxSpeed > 0)) {
        return 0;
    }

    return Math.min(1, Math.max(0, AT_REST + BY_SPEED * speedShare));
}

/**
 * The brightness of the engine effects of a ship whose engines are disabled for a time, `seconds`
 * into the preview's run.
 *
 * One value a game frame, between 0 and a fifth. The game reads its own noise function for it,
 * which the preview has not read: the values here are the preview's, and only their range, their
 * pace and their unsteadiness are the game's.
 */
export function disabledFlicker(seconds: number): number {
    const frame = Math.floor(seconds * GAME_FRAMES_PER_SECOND);
    const hashed = Math.sin(frame * 127.1 + 311.7) * 43758.5453;

    return (hashed - Math.floor(hashed)) * FLICKER_PEAK;
}

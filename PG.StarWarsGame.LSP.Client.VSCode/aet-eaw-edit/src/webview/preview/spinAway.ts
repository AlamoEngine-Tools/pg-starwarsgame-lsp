// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Spinning away: the automated death clone for a unit that declares none.
//
// The mechanic, as the user gave it: with `Spin_Away_On_Death` set and NO `Death_Clone` declared,
// the unit carries on along its current vector at its current speed, corkscrewing, and explodes at
// the end.
//
// Measured over both shipped trees, so the shape of it is not guessed:
//
//   - 34 objects declare the tag, every one of them `Yes`.
//   - NOT ONE of the 34 also declares a `Death_Clone`. The rule holds in the data.
//   - All 34 declare `Max_Speed`.
//   - The engine's own parameter table names five tags in the family, not one, so the time, the
//     chance and the explosion are read rather than invented.
//
// WHAT IS NOT IN ANY FILE is the corkscrew itself - how fast it rolls and how wide it swings. Those
// two constants are CHOSEN, and marked as such below. Everything else on this page came out of the
// corpus or out of the engine's own table.

import type { PreviewSpinAway } from '../../protocol/modelPreview';

/**
 * The engine's tick, and the reason `Max_Speed` needs multiplying.
 *
 * `CONST_FRAME_TIME` is 0.03333 in GameConstants, so Alamo runs at 30Hz and every speed in the XML
 * is PER FRAME. A fighter's `Max_Speed` of 4.5 is 135 units a second; reading it as a per-second
 * figure would creep the wreck forward at a thirtieth of its speed and look like a bug in the
 * animation rather than in the arithmetic.
 */
export const FRAMES_PER_SECOND = 30;

/**
 * How many full rolls a second the wreck turns through. **CHOSEN, not measured.**
 *
 * Nothing in any file describes the corkscrew - the engine's parameter table has a time, a chance,
 * an explosion and a sound, and no rate. This is picked to read as "out of control" over the 2
 * seconds the shipped units spin for: three turns in that time.
 */
const ROLL_TURNS_PER_SECOND = 1.5;

/**
 * How far off the travel axis the nose swings, in engine units. **CHOSEN, not measured.**
 *
 * A roll about the travel axis ALONE spins the model and leaves its path dead straight, which is a
 * barrel roll rather than a corkscrew. Holding the nose off the axis makes the flight path a helix,
 * which is what the word describes.
 */
const HELIX_RADIUS = 14;

/** Where the wreck is, and how far round, at one moment of the spin. */
export interface SpinAwayPose {
    /**
     * Offset from where the unit died, in engine units.
     *
     * **`z` is the travel axis** - the BLUE one in the viewport's axis helper, which is where these
     * models point their nose. It was `x` (red), inferred from the fire bones aiming along local X;
     * the user watched a fighter spin away sideways and corrected it. A fire bone's local axis is a
     * fact about the BONE, not about which way the hull faces.
     */
    offset: { x: number; y: number; z: number };

    /** Roll about the travel axis, in radians. */
    roll: number;

    /** 0 to 1 through the spin, clamped. */
    progress: number;

    /** The spin is over: fire the explosion and take the geometry away. */
    done: boolean;
}

/** Where the wreck has got to `elapsed` seconds after it died. */
export function spinAwayPose(spin: PreviewSpinAway, elapsed: number): SpinAwayPose {
    const time = spin.timeSeconds > 0 ? spin.timeSeconds : 0;
    const at = Math.max(0, elapsed);
    const progress = time > 0 ? Math.min(1, at / time) : 1;

    // Held at the end rather than running on: the wreck stops where it exploded, and a caller that
    // keeps asking after the fact gets the last frame rather than a wreck receding forever.
    const held = time > 0 ? Math.min(at, time) : 0;

    const roll = held * ROLL_TURNS_PER_SECOND * Math.PI * 2;

    return {
        offset: {
            // The two that make the helix. Both start at zero, so the wreck leaves from exactly
            // where the unit was rather than jumping a radius sideways on the first frame.
            x: HELIX_RADIUS * Math.sin(roll),
            y: HELIX_RADIUS * (Math.cos(roll) - 1),
            // The travel axis.
            z: (spin.maxSpeed ?? 0) * FRAMES_PER_SECOND * held,
        },
        roll,
        progress,
        done: time <= 0 || at >= time,
    };
}

/**
 * Where the wreck ends up - the position its explosion belongs at.
 *
 * The explosion used to be fired with no part named, which puts a system at the MODEL ROOT: the
 * fighter flew 270 units away and blew up back where it started. It cannot simply be attached to the
 * hull instead, because the hull is hidden in the same breath and three prunes a hidden subtree,
 * effects included - the trap the ship's own death blast already exists to dodge.
 */
export function spinAwayEnd(spin: PreviewSpinAway): { x: number; y: number; z: number } {
    return spinAwayPose(spin, spin.timeSeconds).offset;
}

/**
 * What the panel says about it, in one line: the time and the chance, as numbers.
 *
 * The chance is READ OUT rather than rolled. Shipped values are 0.2 and 0.4, so most deaths do not
 * spin at all - and a preview that honoured that would look broken four presses out of five.
 *
 * A percentage and nothing else, at the user's instruction. It said "20% of deaths" and the trailing
 * two words are the kind of prose the panel has been having taken out of it.
 */
export function spinAwaySummary(spin: PreviewSpinAway): string {
    const seconds = `${Number.parseFloat(spin.timeSeconds.toFixed(1))}s`;

    // An undeclared chance is left UNSAID rather than printed as 0% or invented as 100%. The tag
    // being on is what decides that it can happen at all; the frequency is simply not in the file,
    // and every one of the 34 shipped objects does declare it.
    return (spin.chance ?? 0) > 0
        ? `${seconds}, ${Math.round(spin.chance * 100)}%`
        : seconds;
}

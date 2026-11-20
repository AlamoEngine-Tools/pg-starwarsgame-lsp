// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// How a dying object ends.
//
// Measured in the game: a dying object with a death clip STAYS. As soon as its clip is not the death
// clip or has finished - at once when there never was one - its fade start is set to now plus
// `Death_Persistence_Duration`; a persistence below 0 never starts it, and the object stays until
// something else takes it away. Until then it holds its clip's held frame. At the fade start a type
// that names `Death_Explosions_End` types is destroyed, those explosions going off; any other type
// fades its alpha towards 0 on a critically damped spring of smooth time `Death_Fade_Time`, and is
// destroyed when it falls to 0.025 or below - about 2.8 fade times in.

/** What a dying object's type says about how it ends. */
export interface DeathEnding {
    /** `Death_Persistence_Duration`: how long it stays once its death clip is over. */
    persistenceSeconds: number;
    /** `Death_Fade_Time`: how long it takes to fade out. */
    fadeSeconds: number;
    /** Whether it names `Death_Explosions_End` types, and so is destroyed rather than faded. */
    endsInExplosions: boolean;
}

/**
 * The game's values for a type that never writes the tags - measured: it stays for ever
 * (`Death_Persistence_Duration` -1) and would fade over a quarter of a second (`Death_Fade_Time`
 * 0.25). An empty value counts as not written; a variant takes its base's.
 */
export const UNWRITTEN_ENDING: DeathEnding = {
    persistenceSeconds: -1,
    fadeSeconds: 0.25,
    endsInExplosions: false,
};

/** The alpha at or below which a fading object is destroyed. */
const DESTROYED_AT_ALPHA = 0.025;

/** The game's simulation rate, which the fade is stepped at. */
const FRAMES_PER_SECOND = 30;

/**
 * The alpha `seconds` into a fade of smooth time `fadeSeconds`.
 *
 * Measured: a critically damped spring from 1 towards 0 with omega = 2 / T, stepped at the game's
 * 30 Hz - (1 + omega t) e^(-omega t), an ease-out that starts flat. Taken at the nearest 30 Hz frame,
 * which is what reproduces the measured values at T = 0.25 s. 0 at once for a fade time of 0 or less.
 */
export function fadeAlpha(seconds: number, fadeSeconds: number): number {
    if (!(fadeSeconds > 0)) {
        return 0;
    }

    const omega = 2 / fadeSeconds;
    const stepped =
        Math.round(seconds * FRAMES_PER_SECOND + 1e-6) / FRAMES_PER_SECOND;

    return (1 + omega * stepped) * Math.exp(-omega * stepped);
}

/** One dying object's progress towards its end, on its own clock. */
export interface DeathState {
    readonly ending: DeathEnding;
    /** Seconds since it started dying. */
    clock: number;
    /**
     * When its fade starts, on {@link clock}; null while its death clip still runs, and for good
     * when its persistence is below 0.
     */
    fadeStart: number | null;
    /** 1 until the fade starts, then down towards 0. */
    alpha: number;
    destroyed: boolean;
}

/** A dying object at its first moment; `clipOver` when it has no death clip to wait for. */
export function deathState(ending: DeathEnding, clipOver: boolean): DeathState {
    return {
        ending,
        clock: 0,
        fadeStart: clipOver ? fadeStartAt(0, ending) : null,
        alpha: 1,
        destroyed: false,
    };
}

/** The fade start for a clip over at `now`; null when the persistence never lets it start. */
function fadeStartAt(now: number, ending: DeathEnding): number | null {
    return ending.persistenceSeconds < 0
        ? null
        : now + ending.persistenceSeconds;
}

/**
 * Moves a dying object on by `dt` seconds. Answers whether it was destroyed on this step.
 *
 * `clipOver` is whether its clip is no longer the death clip or has finished. The fade start is set
 * the first time that holds, and never again - and never at all for a persistence below 0.
 */
export function stepDeath(
    state: DeathState,
    dt: number,
    clipOver: boolean,
): boolean {
    if (state.destroyed) {
        return false;
    }

    state.clock += dt;

    if (state.fadeStart === null && clipOver) {
        state.fadeStart = fadeStartAt(state.clock, state.ending);
    }

    // With a millionth of a second to spare: a clock summed from 30 Hz steps lands a hair short of
    // the second it has reached.
    if (state.fadeStart === null || state.clock + 1e-6 < state.fadeStart) {
        return false;
    }

    if (state.ending.endsInExplosions) {
        state.destroyed = true;
        return true;
    }

    state.alpha = fadeAlpha(
        state.clock - state.fadeStart,
        state.ending.fadeSeconds,
    );

    if (state.alpha <= DESTROYED_AT_ALPHA) {
        state.destroyed = true;
        return true;
    }

    return false;
}

// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// How a dying object ends.
//
// Measured in the game: a dying object with a death clip STAYS. As soon as its clip is not the death
// clip or has finished - at once when there never was one - its fade start is set to now plus
// `Death_Persistence_Duration`. Until then it holds its clip's held frame. At the fade start a type
// that names `Death_Explosions_End` types is destroyed, those explosions going off; any other type
// fades its alpha to 0 over `Death_Fade_Time` and is destroyed when it falls to 0.025 or below.

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
 * An ending that is over the moment the death clip is: no persistence and no fade.
 *
 * What a type that declares neither number is given. The game's value for an undeclared tag is not
 * measured; this one keeps such an object as the preview always showed it, gone at the end of its
 * clip.
 */
export const ENDS_WITH_ITS_CLIP: DeathEnding = {
    persistenceSeconds: 0,
    fadeSeconds: 0,
    endsInExplosions: false,
};

/** The alpha at or below which a fading object is destroyed. */
const DESTROYED_AT_ALPHA = 0.025;

/** One dying object's progress towards its end, on its own clock. */
export interface DeathState {
    readonly ending: DeathEnding;
    /** Seconds since it started dying. */
    clock: number;
    /** When its fade starts, on {@link clock}; null while its death clip still runs. */
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
        fadeStart: clipOver ? ending.persistenceSeconds : null,
        alpha: 1,
        destroyed: false,
    };
}

/**
 * Moves a dying object on by `dt` seconds. Answers whether it was destroyed on this step.
 *
 * `clipOver` is whether its clip is no longer the death clip or has finished. The fade start is set
 * the first time that holds, and never again.
 *
 * The fade is taken as linear; its curve is not measured.
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
        state.fadeStart = state.clock + state.ending.persistenceSeconds;
    }

    if (state.fadeStart === null || state.clock < state.fadeStart) {
        return false;
    }

    if (state.ending.endsInExplosions) {
        state.destroyed = true;
        return true;
    }

    const fading = state.clock - state.fadeStart;
    state.alpha =
        state.ending.fadeSeconds > 0
            ? Math.max(0, 1 - fading / state.ending.fadeSeconds)
            : 0;

    if (state.alpha <= DESTROYED_AT_ALPHA) {
        state.destroyed = true;
        return true;
    }

    return false;
}

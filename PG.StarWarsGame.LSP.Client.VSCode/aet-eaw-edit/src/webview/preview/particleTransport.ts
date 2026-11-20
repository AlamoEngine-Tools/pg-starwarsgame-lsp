// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The particle transport's states and what each verb does to them (#289): play, pause, stop and
// restart over a particle file. Pure, so the rules are tested apart from the hook that holds the
// state and the viewport that carries them out. The speed is not here - it is a room setting.

/** Where a particle file's systems stand. */
export type ParticleRun = "playing" | "paused" | "stopped";

export type ParticleVerb = "play" | "pause" | "stop" | "restart";

/** What a verb leaves behind: the new run, and the one thing the viewport is told, if any. */
export interface ParticleStep {
    run: ParticleRun;
    /**
     * `stop` holds every emitter and lets the air clear; `restart` rebuilds every system from
     * nothing. A pause is not a call: it is the run itself, synced to the viewport as a flag.
     */
    viewport: "stop" | "restart" | null;
}

/** What the transport bar drives. */
export interface ParticleTransport {
    run: ParticleRun;
    /** Pause while playing; play on while paused; play from the start while stopped. */
    togglePlay: () => void;
    stop: () => void;
    restart: () => void;
}

/**
 * The run a verb leads to.
 *
 * Play after Stop starts from the top, as a CD player's does - for a burst the top is the whole
 * effect, and lifting the hold alone would resume a clock that has nothing left to fire. Stop from
 * a pause also lets the step run again, since what a stop promises is that the air clears.
 */
export function particleStep(
    run: ParticleRun,
    verb: ParticleVerb,
): ParticleStep {
    switch (verb) {
        case "play":
            return {
                run: "playing",
                viewport: run === "stopped" ? "restart" : null,
            };
        case "pause":
            return {
                run: run === "playing" ? "paused" : run,
                viewport: null,
            };
        case "stop":
            return {
                run: "stopped",
                viewport: run === "stopped" ? null : "stop",
            };
        case "restart":
            return { run: "playing", viewport: "restart" };
    }
}

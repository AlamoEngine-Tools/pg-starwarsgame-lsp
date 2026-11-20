// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import {
    type DeathEnding,
    UNWRITTEN_ENDING,
    deathState,
    fadeAlpha,
    stepDeath,
} from "./deathEnding";

function near(
    actual: number,
    expected: number,
    what: string,
    within = 0.005,
): void {
    assert.ok(
        Math.abs(actual - expected) <= within,
        `${what}: ${actual}, expected ${expected}`,
    );
}

/** Steps a death by one 30 Hz frame at a time, for `seconds`. */
function run(ending: DeathEnding, seconds: number, clipOver = true) {
    const state = deathState(ending, clipOver);
    let destroyedAt: number | null = null;

    for (let frame = 1; frame <= Math.round(seconds * 30); frame++) {
        if (stepDeath(state, 1 / 30, clipOver)) {
            destroyedAt = frame / 30;
        }
    }

    return { state, destroyedAt };
}

describe("UNWRITTEN_ENDING", () => {
    /** Measured: the game's values for a type that never writes the tags. */
    it("stays for ever and fades over a quarter of a second", () => {
        assert.deepEqual(UNWRITTEN_ENDING, {
            persistenceSeconds: -1,
            fadeSeconds: 0.25,
            endsInExplosions: false,
        });
    });
});

describe("fadeAlpha", () => {
    /**
     * Measured: a critically damped spring from 1 towards 0, smooth time T, omega 2 / T, stepped at
     * the game's 30 Hz - (1 + omega t) e^(-omega t), an ease-out that starts flat.
     */
    it("follows the spring at T = 0.25 s in 30 Hz steps", () => {
        near(fadeAlpha(0.25 / 4, 0.25), 0.9, "T/4");
        near(fadeAlpha(0.25 / 2, 0.25), 0.71, "T/2");
        near(fadeAlpha(0.25, 0.25), 0.37, "T");
        near(fadeAlpha(0.5, 0.25), 0.09, "2T");
    });

    it("follows the spring at T = 1 s", () => {
        near(fadeAlpha(0.5, 1), 0.74, "T/2");
        near(fadeAlpha(1, 1), 0.41, "T");
        near(fadeAlpha(2, 1), 0.09, "2T");
    });

    it("starts flat, at 1", () => {
        assert.equal(fadeAlpha(0, 0.25), 1);
    });

    it("is 0 at once for a fade time of 0", () => {
        assert.equal(fadeAlpha(0, 0), 0);
    });
});

describe("stepDeath", () => {
    it("never starts the fade for a persistence below 0: the object stays", () => {
        const { state, destroyedAt } = run(
            {
                persistenceSeconds: -1,
                fadeSeconds: 0.25,
                endsInExplosions: false,
            },
            60,
        );

        assert.equal(destroyedAt, null);
        assert.equal(state.fadeStart, null);
        assert.equal(state.alpha, 1);
    });

    it("starts the fade at once for a persistence of 0, and destroys at 0.025 - about 2.8 T", () => {
        const { destroyedAt } = run(
            { persistenceSeconds: 0, fadeSeconds: 1, endsInExplosions: false },
            5,
        );

        assert.ok(destroyedAt !== null, "never destroyed");
        assert.ok(
            destroyedAt >= 2.7 && destroyedAt <= 2.9,
            `destroyed at ${destroyedAt}`,
        );
    });

    it("fades along the spring once the persistence is over", () => {
        const state = deathState(
            { persistenceSeconds: 2, fadeSeconds: 1, endsInExplosions: false },
            true,
        );

        for (let frame = 0; frame < 75; frame++) {
            stepDeath(state, 1 / 30, true);
        }

        near(state.alpha, 0.74, "half a fade time in");
    });

    it("destroys an object that ends in explosions at its fade start, with no fade", () => {
        const { state, destroyedAt } = run(
            { persistenceSeconds: 1, fadeSeconds: 1, endsInExplosions: true },
            2,
        );

        near(destroyedAt ?? -1, 1, "destroyed at", 1 / 30);
        assert.equal(state.alpha, 1);
    });
});

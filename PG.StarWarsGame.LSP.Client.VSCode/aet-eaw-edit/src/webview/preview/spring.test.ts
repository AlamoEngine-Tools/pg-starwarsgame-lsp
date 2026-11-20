// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { SteppedSpring } from "./spring";

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

/**
 * Measured: the death fade and the cloak both move on a critically damped spring of smooth time T,
 * omega = 2 / T, stepped at the game's 30 Hz.
 */
describe("SteppedSpring", () => {
    it("falls from 1 towards 0 along the death fade's values", () => {
        const spring = new SteppedSpring(1);

        spring.advance(0.5, 0, 1);
        near(spring.value, 0.74, "T/2");
        spring.advance(0.5, 0, 1);
        near(spring.value, 0.41, "T");
        spring.advance(1, 0, 1);
        near(spring.value, 0.09, "2T");
    });

    it("rises from 0 towards 1 the same way", () => {
        const spring = new SteppedSpring(0);

        spring.advance(0.5, 1, 1);
        near(spring.value, 0.26, "T/2");
    });

    it("keeps its velocity when the target flips mid-way", () => {
        const spring = new SteppedSpring(0);
        spring.advance(0.25, 1, 1);
        const atFlip = spring.value;

        spring.advance(1 / 30, 0, 1);

        assert.ok(spring.value > atFlip, `it stopped dead at ${atFlip}`);
    });

    it("steps only whole 30 Hz frames, whatever the frame rate", () => {
        const sixty = new SteppedSpring(1);
        for (let i = 0; i < 30; i++) {
            sixty.advance(1 / 60, 0, 1);
        }

        const thirty = new SteppedSpring(1);
        thirty.advance(0.5, 0, 1);

        near(sixty.value, thirty.value, "60 Hz against 30 Hz", 1e-9);
    });

    it("jumps to the target for a smooth time of 0", () => {
        const spring = new SteppedSpring(1);
        spring.advance(0, 0, 0);

        assert.equal(spring.value, 0);
    });
});

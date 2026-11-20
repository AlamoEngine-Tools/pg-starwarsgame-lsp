// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The spring the game eases its fades on.
//
// Measured: a dying object's fade and a cloak's both follow a critically damped spring of smooth time
// T, omega = 2 / T, stepped at the game's 30 Hz. Falling from 1 with no velocity it is
// (1 + omega t) e^(-omega t), an ease-out that starts flat. One implementation for both.

/** The game's simulation rate, which the spring is stepped at. */
const FRAMES_PER_SECOND = 30;

/**
 * A critically damped spring stepped at 30 Hz, however often it is advanced.
 *
 * Its own clock counts the time it has been advanced, and it steps the whole 30 Hz frames that time
 * has reached - the nearest one, which is what reproduces the measured fade values. Its velocity
 * carries over when the target changes, so a fade that turns round mid-way does so smoothly.
 */
export class SteppedSpring {
    private velocity = 0;
    private elapsed = 0;
    private frames = 0;

    constructor(public value: number) {}

    /** Moves the spring on by `dt` seconds towards `target`, with smooth time `smoothSeconds`. */
    advance(dt: number, target: number, smoothSeconds: number): void {
        if (!(smoothSeconds > 0)) {
            this.value = target;
            this.velocity = 0;
            return;
        }

        this.elapsed += dt;
        const due = Math.round(this.elapsed * FRAMES_PER_SECOND + 1e-6);

        for (; this.frames < due; this.frames++) {
            this.step(target, smoothSeconds);
        }
    }

    /** One 30 Hz step of the critically damped spring, in its usual stable form. */
    private step(target: number, smoothSeconds: number): void {
        const omega = 2 / smoothSeconds;
        const dt = 1 / FRAMES_PER_SECOND;
        const x = omega * dt;
        const decay = 1 / (1 + x + 0.48 * x * x + 0.235 * x * x * x);
        const change = this.value - target;
        const pull = (this.velocity + omega * change) * dt;

        this.velocity = (this.velocity - omega * pull) * decay;
        this.value = target + (change + pull) * decay;
    }
}

// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { FRAMES_PER_SECOND, spinAwayEnd, spinAwayPose, spinAwaySummary } from './spinAway';

const spin = { timeSeconds: 2, chance: 0.2, explosion: 'Small_Explosion_Space', maxSpeed: 4.5 };

describe('spinAwayPose', () => {
    /**
     * The user's account: with `Spin_Away_On_Death` set and no death clone declared, the unit
     * carries on along its current vector at its current speed, corkscrewing, and explodes at the
     * end.
     */
    it('starts where the unit died', () => {
        const at = spinAwayPose(spin, 0);

        assert.equal(at.offset.x, 0);
        assert.equal(at.offset.y, 0);
        assert.equal(at.offset.z, 0);
        assert.equal(at.progress, 0);
        assert.equal(at.done, false);
    });

    /**
     * `Max_Speed` is PER FRAME, not per second - the engine runs at 30Hz, so 4.5 is 135 a second.
     * Reading it as a per-second figure would creep the wreck forward at a thirtieth of its speed.
     */
    it('travels at Max_Speed times the frame rate, not at Max_Speed', () => {
        assert.equal(spinAwayPose(spin, 1).offset.z, 4.5 * FRAMES_PER_SECOND);
        assert.equal(FRAMES_PER_SECOND, 30);
    });

    /**
     * Down the BLUE axis. It was red - X - inferred from fire bones aiming along local X, and the
     * user watched a fighter spin away sideways and corrected it. A fire bone's local axis says
     * where that BONE points, not which way the hull faces.
     */
    it('travels along Z, not X', () => {
        const on = spinAwayPose(spin, 1);

        assert.ok(on.offset.z > 100);
        assert.ok(Math.abs(on.offset.x) < 20);
    });

    it('rolls as it goes - that is the corkscrew', () => {
        assert.equal(spinAwayPose(spin, 0).roll, 0);
        assert.ok(spinAwayPose(spin, 1).roll > 0);
        assert.ok(spinAwayPose(spin, 2).roll > spinAwayPose(spin, 1).roll);
    });

    /**
     * A roll about the travel axis alone spins the model and leaves the PATH straight. The nose is
     * held off the travel axis so the flight path is a helix, which is what a corkscrew is.
     */
    it('swings off the travel axis rather than boring straight ahead', () => {
        const quarter = spinAwayPose(spin, 0.5);

        assert.ok(Math.abs(quarter.offset.x) > 0 || Math.abs(quarter.offset.y) > 0);
    });

    /**
     * The explosion belongs where the wreck FINISHED. Fired with no part named it goes to the model
     * root, so a fighter that flew 270 units away blew up back where it started.
     */
    it('says where it ends, for the explosion', () => {
        assert.deepEqual(spinAwayEnd(spin), spinAwayPose(spin, spin.timeSeconds).offset);
        assert.equal(spinAwayEnd(spin).z, 4.5 * FRAMES_PER_SECOND * 2);
    });

    it('reports how far through it is', () => {
        assert.equal(spinAwayPose(spin, 1).progress, 0.5);
        assert.equal(spinAwayPose(spin, 2).progress, 1);
    });

    it('is done at the end of Spin_Away_On_Death_Time, and stays done', () => {
        assert.equal(spinAwayPose(spin, 1.99).done, false);
        assert.equal(spinAwayPose(spin, 2).done, true);
        assert.equal(spinAwayPose(spin, 9).done, true);
        assert.equal(spinAwayPose(spin, 9).progress, 1);
    });

    /**
     * A unit that declares no speed does not creep along at zero and hang there - it still spins in
     * place and still explodes on time. All 34 shipped objects DO declare one, so this is about a
     * mod that does not.
     */
    it('still spins and still ends when the unit declares no speed', () => {
        const still = { ...spin, maxSpeed: 0 };

        assert.equal(spinAwayPose(still, 1).offset.z, 0);
        assert.ok(spinAwayPose(still, 1).roll > 0);
        assert.equal(spinAwayPose(still, 2).done, true);
    });

    it('treats a missing time as over at once rather than never ending', () => {
        assert.equal(spinAwayPose({ ...spin, timeSeconds: 0 }, 0).done, true);
    });
});

describe('spinAwaySummary', () => {
    /**
     * The time and the chance, as numbers and nothing else.
     *
     * The chance is READ OUT rather than rolled - shipped values are 0.2 and 0.4, so a preview that
     * honoured them would look broken four presses out of five. It read "20% of deaths"; the user
     * asked for the percentage alone.
     */
    it('is the time and the chance, with no prose around them', () => {
        assert.equal(spinAwaySummary(spin), '2s, 20%');
        assert.equal(spinAwaySummary({ ...spin, chance: 0.4 }), '2s, 40%');
    });

    /** Unsaid rather than printed as 0% or invented as 100%: the file simply does not say. */
    it('leaves an undeclared chance out rather than making one up', () => {
        assert.equal(spinAwaySummary({ ...spin, chance: 0 }), '2s');
    });
});

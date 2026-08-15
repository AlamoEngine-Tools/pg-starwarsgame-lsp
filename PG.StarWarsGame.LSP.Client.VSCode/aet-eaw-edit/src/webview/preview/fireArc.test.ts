// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { fireConePoints } from './fireArc';

describe('fireConePoints', () => {
    it('puts the apex at the bone and opens along +X', () => {
        // Measured on Ev_stardestroyer.alo: every FP_ bone's local +X points away from the hull,
        // with Z near zero because a ship's guns fire horizontally.
        const { apex, rim } = fireConePoints(30, 20, 100, 8);

        assert.deepEqual(apex, [0, 0, 0]);
        assert.ok(rim.every(([x]) => Math.abs(x - 100) < 1e-6), 'rim sits at the range');
    });

    it('opens the width across Y and the height across Z', () => {
        // 90 degrees of width means the half-angle is 45, so the rim reaches the range sideways.
        const { rim } = fireConePoints(90, 0.0001, 100, 16);

        const widest = Math.max(...rim.map(([, y]) => Math.abs(y)));
        const tallest = Math.max(...rim.map(([, , z]) => Math.abs(z)));

        assert.ok(Math.abs(widest - 100) < 0.01, `expected ~100 across, got ${widest}`);
        assert.ok(tallest < 1, `expected a flat arc, got ${tallest}`);
    });

    it('produces one rim point per segment', () => {
        assert.equal(fireConePoints(30, 30, 10, 12).rim.length, 12);
    });

    it('treats a missing cone angle as a narrow line rather than a full sphere', () => {
        // Fire_Cone_Width is optional. Defaulting it to something huge would draw an arc the
        // hardpoint does not have.
        const { rim } = fireConePoints(0, 0, 100, 8);

        assert.ok(rim.every(([, y, z]) => Math.abs(y) < 1e-6 && Math.abs(z) < 1e-6));
    });

    it('never turns a wide-open arc inside out', () => {
        // Half of 180 is a right angle, where the tangent blows up. Clamped, so the cone stays a
        // cone instead of flipping behind the bone.
        const { rim } = fireConePoints(180, 180, 100, 8);

        assert.ok(rim.every(([x]) => x > 0), 'every rim point stays in front of the bone');
        assert.ok(rim.every(([, y, z]) => Number.isFinite(y) && Number.isFinite(z)));
    });
});

// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { groundRange, snapToZero } from './groundRange';

describe('groundRange', () => {
    it('reaches past a Star Destroyer', () => {
        // The objection to a slider was that no ONE range suits a corpus running from a two-unit
        // trooper to a thousand-unit command centre. The answer is not to pick a range but to take
        // it from the subject.
        assert.deepEqual(groundRange(349), { limit: 500, step: 5 });
    });

    it('is just as usable on a trooper', () => {
        assert.deepEqual(groundRange(2), { limit: 2, step: 0.02 });
    });

    it('rounds up to a round number, so the ends of the track read as something', () => {
        assert.equal(groundRange(120).limit, 200);
        assert.equal(groundRange(60).limit, 100);
        assert.equal(groundRange(30).limit, 50);
    });

    it('is symmetrical about zero, which is where the model stands', () => {
        // The caller mirrors it: -limit to +limit puts the ground plane at the centre of the track.
        assert.ok(groundRange(80).limit > 0);
    });

    it('falls back to something workable with no model loaded', () => {
        assert.equal(groundRange(0).limit, 100);
        assert.equal(groundRange(Number.NaN).limit, 100);
        assert.equal(groundRange(-5).limit, 100);
    });
});

describe('snapToZero', () => {
    it('leaves zero alone', () => {
        assert.equal(snapToZero(0, 5), 0);
    });

    it('pulls the position either side of zero onto it', () => {
        // A 400-position track is about a pixel and a half per step, so hitting the middle exactly
        // by dragging is a matter of luck. The detent is what makes the ground plane catchable.
        assert.equal(snapToZero(5, 5), 0);
        assert.equal(snapToZero(-5, 5), 0);
    });

    it('does not reach past one step', () => {
        // Any wider and the detent starts eating values someone might actually want.
        assert.equal(snapToZero(10, 5), 10);
        assert.equal(snapToZero(-10, 5), -10);
    });

    it('survives a step that does not divide cleanly in binary', () => {
        // 0.02 is the step on a two-unit trooper, and 0.02 * 1 is not exactly 0.02 in floating
        // point - without a tolerance the detent silently stops working on small models.
        assert.equal(snapToZero(0.02, 0.02), 0);
        assert.equal(snapToZero(0.06, 0.02), 0.06);
    });

    it('can be switched off, for input that is already exact', () => {
        // Arrow keys move exactly one step, so a reader stepping to +1 meant +1 and must get it.
        assert.equal(snapToZero(5, 5, 0), 5);
    });
});

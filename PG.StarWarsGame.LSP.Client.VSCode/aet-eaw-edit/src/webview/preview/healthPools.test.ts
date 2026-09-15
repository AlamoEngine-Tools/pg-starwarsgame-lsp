// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { displayPercent, leashed, percentOf } from './healthPools';

const pool = (current: number, max: number) => ({ current, max });

describe('percentOf', () => {
    it('is zero for a pool with no maximum, not a division by zero', () => {
        assert.equal(percentOf(pool(0, 0)), 0);
    });

    it('clamps a repaired pool that overshoots its maximum', () => {
        assert.equal(percentOf(pool(120, 100)), 1);
    });
});

describe('leashed with no destroyable hardpoints', () => {
    // The guard that matters most. GameObjectClass::Service checks the hardpoint COUNT and the
    // destroyable count before it looks at the tag, so a fighter is never touched. Without that,
    // an empty hardpoint pool reads as 0% and would drag every hull down to the constraint.
    it('leaves a full hull alone', () => {
        const after = leashed(pool(100, 100), pool(0, 0), 0.2, true);

        assert.deepEqual(after.hull, pool(100, 100));
    });
});

describe('leashed pulling hardpoints down', () => {
    it('damages them to exactly the cap, in one step', () => {
        // hull 45%, so the cap is 65%. 1000 of hardpoints at 85% lands on 650.
        const after = leashed(pool(45, 100), pool(850, 1000), 0.2, true);

        assert.equal(after.hardpoints.current, 650);
    });

    it('does nothing while the hull is at 80% or better', () => {
        // cap clamps to 100%, so the first fifth of hull loss is free.
        const after = leashed(pool(85, 100), pool(1000, 1000), 0.2, true);

        assert.equal(after.hardpoints.current, 1000);
    });

    // Not gated by the tag: hardpoints follow the hull even on the palace.
    it('applies even when the unit does not die with its hardpoints', () => {
        const after = leashed(pool(45, 100), pool(850, 1000), 0.2, false);

        assert.equal(after.hardpoints.current, 650);
    });
});

describe('leashed pulling the hull down', () => {
    it('sets the hull to the hardpoint percentage plus the constraint', () => {
        // hardpoints 25%, so the hull is set to 45%.
        const after = leashed(pool(100, 100), pool(250, 1000), 0.2, true);

        assert.equal(after.hull.current, 45);
    });

    // The half the tag gates - and the whole substance of untying the pools.
    it('does not touch the hull when the unit does not die with its hardpoints', () => {
        const after = leashed(pool(100, 100), pool(250, 1000), 0.2, false);

        assert.equal(after.hull.current, 100);
    });

    it('never raises a hull that is already below the cap', () => {
        const after = leashed(pool(10, 100), pool(1000, 1000), 0.2, true);

        assert.equal(after.hull.current, 10);
    });
});

describe('leashed at the extremes of the constraint', () => {
    // What EaWX ships. Every cap clamps to 100%, so both corrections stop running.
    it('corrects nothing at all when the constraint is 1', () => {
        const after = leashed(pool(100, 100), pool(250, 1000), 1, true);

        assert.deepEqual(after.hull, pool(100, 100));
        assert.deepEqual(after.hardpoints, pool(250, 1000));
    });

    it('converges both pools on the lower one when the constraint is 0', () => {
        const after = leashed(pool(100, 100), pool(250, 1000), 0, true);

        assert.equal(after.hull.current, 25);
        assert.equal(after.hardpoints.current, 250);
    });

    // The hull is corrected FIRST and the hardpoint step reads the updated value, exactly as
    // Service does before it calls Service_Hard_Points. At 0 the order is what makes them meet.
    it('applies the hull correction before the hardpoint one', () => {
        const after = leashed(pool(100, 100), pool(400, 1000), 0, true);

        assert.equal(after.hull.current, 40);
        assert.equal(after.hardpoints.current, 400);
    });
});

describe('displayPercent', () => {
    it('is the worse of the two pools', () => {
        assert.equal(displayPercent(pool(90, 100), pool(250, 1000), true), 0.25);
        assert.equal(displayPercent(pool(30, 100), pool(900, 1000), true), 0.3);
    });

    // With the tag off the bar never consults the hardpoints - the palace's generators can be shot
    // off without the bar moving at all.
    it('is the hull alone when the unit does not die with its hardpoints', () => {
        assert.equal(displayPercent(pool(90, 100), pool(250, 1000), false), 0.9);
    });

    it('is the hull alone for a unit with no destroyable hardpoints', () => {
        assert.equal(displayPercent(pool(90, 100), pool(0, 0), true), 0.9);
    });
});

// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { skyGradient, starPositions, STAR_COUNT } from './backdrop';

describe('starPositions', () => {
    it('puts every star on the sphere', () => {
        const stars = starPositions(1);

        assert.equal(stars.length, STAR_COUNT * 3);

        for (let i = 0; i < STAR_COUNT; i++) {
            const radius = Math.hypot(stars[i * 3], stars[i * 3 + 1], stars[i * 3 + 2]);
            assert.ok(Math.abs(radius - 1) < 1e-6, `star ${i} sat at ${radius}`);
        }
    });

    /**
     * Spread by AREA, not by angle. Picking a latitude uniformly crowds both poles - the sky ends up
     * with two obvious clumps directly above and below the model, which reads as a rendering fault.
     */
    it('spreads the stars evenly rather than crowding the poles', () => {
        const stars = starPositions(7);

        // The band around the equator holding half the sphere's AREA is |y| < 0.5, so about half
        // the stars should fall inside it.
        let equatorial = 0;
        for (let i = 0; i < STAR_COUNT; i++) {
            if (Math.abs(stars[i * 3 + 1]) < 0.5) { equatorial++; }
        }

        const share = equatorial / STAR_COUNT;
        assert.ok(Math.abs(share - 0.5) < 0.05, `half the area held ${share} of the stars`);
    });

    /** The same sky every time a model opens, or a screenshot never compares against another. */
    it('is the same sky for the same seed', () => {
        assert.deepEqual([...starPositions(3)], [...starPositions(3)]);
        assert.notDeepEqual([...starPositions(3)], [...starPositions(4)]);
    });
});

describe('skyGradient', () => {
    it('runs from a deep zenith down to a pale horizon', () => {
        const top = skyGradient(1);
        const horizon = skyGradient(0);

        // Saturation, not raw blue: a washed-out horizon carries MORE blue than a deep zenith
        // because it is nearly white. What separates them is the gap between the channels.
        assert.ok(top[2] - top[0] > horizon[2] - horizon[0],
            'the zenith should be the more saturated end');
        assert.ok(horizon[0] > top[0], 'the horizon should be the paler end');
    });

    it('stays a colour at either extreme and below the horizon', () => {
        for (const height of [-1, -0.5, 0, 0.5, 1]) {
            for (const channel of skyGradient(height)) {
                assert.ok(channel >= 0 && channel <= 1, `${height} gave ${channel}`);
            }
        }
    });

    /** Below the horizon it holds the horizon's own colour rather than inverting into the ground. */
    it('does not keep going once it is under the horizon', () => {
        assert.deepEqual(skyGradient(-0.5), skyGradient(0));
    });
});

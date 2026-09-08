// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { lightBearing } from './lightBearing';

describe('lightBearing', () => {
    it('names zero as the front, because that is where the Front view stands', () => {
        // The reader already has Front, Side and Top on the stage. Saying "0 deg" tells them
        // nothing about which way the light then points; saying "the front" reuses what they know.
        assert.equal(lightBearing(0, 45).from, 'the front');
    });

    it('turns clockwise seen from above', () => {
        assert.equal(lightBearing(90, 45).from, 'the right');
        assert.equal(lightBearing(180, 45).from, 'behind');
        assert.equal(lightBearing(270, 45).from, 'the left');
    });

    it('names the corners', () => {
        assert.equal(lightBearing(45, 45).from, 'the front right');
        assert.equal(lightBearing(225, 45).from, 'the back left');
    });

    it('rounds to the nearest of the eight, so a slider step does not flicker', () => {
        assert.equal(lightBearing(20, 45).from, 'the front');
        assert.equal(lightBearing(25, 45).from, 'the front right');
    });

    it('wraps, so 360 reads the same as 0', () => {
        assert.equal(lightBearing(360, 45).from, lightBearing(0, 45).from);
        assert.equal(lightBearing(-90, 45).from, lightBearing(270, 45).from);
    });

    it('says when the light has gone under the ground plane', () => {
        // The slider runs from -90 to 90 with nothing marking the horizon, so the only way to know
        // the light has dropped below the floor is to notice the shadow vanish.
        assert.equal(lightBearing(45, -10).belowGround, true);
        assert.equal(lightBearing(45, 0).belowGround, false);
        assert.equal(lightBearing(45, 10).belowGround, false);
    });

    it('describes the height in words as well as degrees', () => {
        assert.equal(lightBearing(45, 80).height, 'overhead');
        assert.equal(lightBearing(45, 45).height, 'above');
        assert.equal(lightBearing(45, 10).height, 'raking');
        assert.equal(lightBearing(45, 0).height, 'level');
        assert.equal(lightBearing(45, -30).height, 'below');
    });
});

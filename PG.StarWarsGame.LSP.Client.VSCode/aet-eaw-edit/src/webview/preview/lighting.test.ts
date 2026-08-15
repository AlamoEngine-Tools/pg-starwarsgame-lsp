// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import {
    DEFAULT_LIGHT_AZIMUTH_DEGREES, DEFAULT_LIGHT_ELEVATION_DEGREES, lightDirection, shadowFrustum,
} from './lighting';

const close = (actual: number, expected: number, what: string): void => {
    assert.ok(Math.abs(actual - expected) < 1e-6, `${what}: ${actual} is not ${expected}`);
};

describe('lightDirection', () => {
    it('puts an overhead light straight up', () => {
        const up = lightDirection(0, 90);

        close(up.y, 1, 'y');
        close(up.x, 0, 'x');
        close(up.z, 0, 'z');
    });

    /**
     * The SAME convention as the camera presets in `framing.ts`: azimuth is measured round Y from
     * +Z, so 90 degrees is +X. Two conventions in one viewport would mean "front" for the camera and
     * "front" for the light pointed different ways.
     */
    it('measures azimuth round Y from +Z, as the camera presets do', () => {
        const front = lightDirection(0, 0);
        close(front.z, 1, 'z at azimuth 0');

        const side = lightDirection(90, 0);
        close(side.x, 1, 'x at azimuth 90');
    });

    it('always returns a unit vector', () => {
        for (const [azimuth, elevation] of [[0, 0], [45, 45], [217, -30], [359, 89]]) {
            const d = lightDirection(azimuth, elevation);
            close(Math.hypot(d.x, d.y, d.z), 1, `length at ${azimuth}/${elevation}`);
        }
    });

    it('lights from below when the elevation goes negative', () => {
        assert.ok(lightDirection(0, -30).y < 0);
    });

    /** The default is a raking light, not an overhead one - see the constant's own note. */
    it('does not default to straight overhead', () => {
        assert.ok(DEFAULT_LIGHT_ELEVATION_DEGREES < 90);

        const d = lightDirection(DEFAULT_LIGHT_AZIMUTH_DEGREES, DEFAULT_LIGHT_ELEVATION_DEGREES);
        assert.ok(Math.hypot(d.x, d.z) > 0.5, 'the default light has no horizontal throw');
    });
});

describe('shadowFrustum', () => {
    /**
     * three's default shadow camera is five units across. A Star Destroyer is about 600 long, so the
     * hull falls entirely outside it and NO shadow is drawn - which reads as the feature not
     * existing rather than as a frustum being too small.
     */
    it('grows with the model', () => {
        assert.ok(shadowFrustum(300).extent > 300);
        assert.ok(shadowFrustum(300).extent > shadowFrustum(3).extent);
    });

    it('leaves room for a caster standing outside the lit area', () => {
        // Geometry behind the model relative to the light still has to reach the depth map.
        assert.ok(shadowFrustum(10).far > shadowFrustum(10).extent * 2);
    });

    it('keeps a usable frustum for a model of no size at all', () => {
        // A particle-only scene measures a point; a zero extent is a degenerate projection.
        const tiny = shadowFrustum(0);

        assert.ok(tiny.extent > 0);
        assert.ok(tiny.far > tiny.near);
    });
});

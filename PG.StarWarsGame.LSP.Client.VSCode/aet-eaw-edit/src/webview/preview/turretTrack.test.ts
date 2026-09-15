// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { axisRange } from './turretHandles';
import {
    knobPoint, stopTicks, trackPlane, trackPoint, trackPoints, trackRadius, trackSegments,
} from './turretTrack';

function close(actual: number, expected: number, what: string): void {
    assert.ok(Math.abs(actual - expected) < 1e-6, `${what}: ${actual} is not ${expected}`);
}

describe('trackPlane', () => {
    it('puts traverse and elevation in their own planes, and there is no third', () => {
        assert.equal(trackPlane(axisRange('yaw', 45)), 'traverse');
        assert.equal(trackPlane(axisRange('pitch', 45)), 'elevation');
    });
});

describe('trackPoint', () => {
    // Zero is straight ahead, along local +X - the same zero the fire bones use and the same one
    // the aim readout counts from.
    it('puts zero degrees on the bone forward axis', () => {
        assert.deepEqual(trackPoint('traverse', 0, 10), [10, 0, 0]);
        assert.deepEqual(trackPoint('elevation', 0, 10), [10, 0, 0]);
    });

    it('swings traverse across Y, which is the lateral axis', () => {
        const [x, y, z] = trackPoint('traverse', 90, 10);

        close(x, 0, 'x');
        close(y, 10, 'y');
        assert.equal(z, 0);
    });

    // Positive pitch is nose UP, matching the viewport's own elevation convention.
    it('swings elevation into +Z, which is up', () => {
        const [x, y, z] = trackPoint('elevation', 90, 10);

        close(x, 0, 'x');
        assert.equal(y, 0);
        close(z, 10, 'z');
    });

    it('keeps every point on the radius, whatever the angle', () => {
        for (const degrees of [0, 17, 45, 123, -80, 200]) {
            const [x, y, z] = trackPoint('traverse', degrees, 7);

            close(Math.hypot(x, y, z), 7, `radius at ${degrees}`);
        }
    });
});

describe('trackPoints', () => {
    /**
     * The track spans the legal range and nothing more. A full circle on a turret bounded to plus
     * or minus 45 would claim a reach the unit has not got, and the reader has to see the stop
     * before reaching it rather than by pushing against it.
     */
    it('spans exactly the legal range on a bounded axis', () => {
        const points = trackPoints(axisRange('yaw', 45), 10, 8);

        assert.equal(points.length, 9);
        assert.deepEqual(points[0], trackPoint('traverse', -45, 10));
        assert.deepEqual(points[points.length - 1], trackPoint('traverse', 45, 10));
    });

    // A closed ring's last point IS its first, and drawing both leaves a seam.
    it('closes a free axis into a full ring without repeating a point', () => {
        const points = trackPoints(axisRange('yaw', 360), 10, 8);

        assert.equal(points.length, 8);
        assert.notDeepEqual(points[0], points[points.length - 1]);
    });

    it('gives a free axis the whole circle', () => {
        const points = trackPoints(axisRange('yaw', 180), 10, 4);

        assert.equal(points.length, 4);
        close(points[1][1], 10, 'a quarter turn lands on +Y');
    });
});

describe('trackSegments', () => {
    it('pairs the points up into segments', () => {
        // 8 spans between 9 points, two ends each, three numbers each.
        assert.equal(trackSegments(axisRange('yaw', 45), 10, 8).length, 8 * 2 * 3);
    });

    it('adds the closing segment on a free axis, and only there', () => {
        assert.equal(trackSegments(axisRange('yaw', 360), 10, 8).length, 8 * 2 * 3);
        assert.equal(trackSegments(axisRange('yaw', 45), 10, 8).length, 8 * 2 * 3);
    });
});

describe('stopTicks', () => {
    it('marks both ends of a bounded axis', () => {
        // Two ticks, two ends each, three numbers each.
        assert.equal(stopTicks(axisRange('yaw', 45), 10, 2).length, 2 * 2 * 3);
    });

    // The absence IS the information: a track with no ticks is a turret with no stops.
    it('marks nothing on an axis that has no stops', () => {
        assert.deepEqual(stopTicks(axisRange('yaw', 360), 10, 2), []);
        assert.deepEqual(stopTicks(axisRange('yaw', 0), 10, 2), []);
    });

    it('crosses the track rather than sitting beside it', () => {
        const ticks = stopTicks(axisRange('yaw', 90), 10, 4);
        const inner = Math.hypot(ticks[0], ticks[1], ticks[2]);
        const outer = Math.hypot(ticks[3], ticks[4], ticks[5]);

        close(inner, 8, 'inner end');
        close(outer, 12, 'outer end');
    });
});

describe('knobPoint', () => {
    it('sits on the track, at the angle the turret is pointed', () => {
        assert.deepEqual(knobPoint(axisRange('yaw', 45), 30, 10), trackPoint('traverse', 30, 10));
        assert.deepEqual(
            knobPoint(axisRange('pitch', 45), -20, 10), trackPoint('elevation', -20, 10));
    });
});

describe('trackRadius', () => {
    // The same gizmo has to be grabbable on a speeder and not swallow a Star Destroyer.
    it('scales with the subject', () => {
        close(trackRadius(600), 36, '600-unit hull');
        close(trackRadius(200), 12, '200-unit hull');
    });

    it('never shrinks below something a pointer can hit', () => {
        assert.equal(trackRadius(0), 6);
        assert.equal(trackRadius(10), 6);
    });

    /**
     * The fraction was measured, not chosen: at 0.18 the Gargantuan's six tracks each reached about
     * a third of the hull and grew into one another. A track must stay well inside the hull it sits
     * on, because the count it has to survive is dozens rather than six.
     */
    it('keeps a track small enough that many of them stay separable', () => {
        assert.ok(trackRadius(600) < 600 / 8, `${trackRadius(600)} is not a small part of the hull`);
    });
});

// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import {
    clipPlanes, fitDistance, frameSphere, sphereFromBounds, type BoundingSphere,
} from './framing';

const FOV = 50;

/** Half-angle a point subtends from the camera, which is what "fits on screen" means. */
function angleFromAxis(distance: number, radius: number): number {
    return Math.asin(Math.min(radius / distance, 1));
}

describe('fitDistance', () => {
    it('puts a larger model further away, in proportion', () => {
        const near = fitDistance(10, FOV, 1);
        const far = fitDistance(100, FOV, 1);

        assert.ok(far > near);
        assert.ok(Math.abs(far / near - 10) < 1e-6, `expected a tenfold step, got ${far / near}`);
    });

    it('keeps the model inside the field of view with room to spare', () => {
        const radius = 42;
        const distance = fitDistance(radius, FOV, 1);

        // Comfortably inside half the FOV: touching the edge reads as clipped even when it is not.
        assert.ok(angleFromAxis(distance, radius) < (FOV / 2) * (Math.PI / 180));
    });

    /**
     * A portrait-shaped panel is narrower than it is tall, so the horizontal field is the limit. Using
     * the vertical one regardless crops the ends off a Star Destroyer.
     */
    it('pulls back further for a narrow viewport', () => {
        assert.ok(fitDistance(10, FOV, 0.5) > fitDistance(10, FOV, 1));
    });

    it('does not pull back further for a wide viewport than for a square one', () => {
        assert.ok(fitDistance(10, FOV, 2) <= fitDistance(10, FOV, 1) + 1e-9);
    });

    /** Some shipped models are a single bone with no geometry; the camera must not land on it. */
    it('survives a zero-radius model', () => {
        const distance = fitDistance(0, FOV, 1);

        assert.ok(Number.isFinite(distance) && distance > 0, `got ${distance}`);
    });
});

describe('frameSphere', () => {
    const sphere: BoundingSphere = { center: { x: 5, y: -2, z: 3 }, radius: 20 };

    it('looks at the centre of the model, not the origin', () => {
        // Models are not authored around their own origin; aiming at it points the camera at empty
        // space for anything built off-centre.
        assert.deepEqual(frameSphere(sphere, FOV, 1).target, sphere.center);
    });

    it('places the camera at the fitting distance from that centre', () => {
        const pose = frameSphere(sphere, FOV, 1);

        const actual = Math.hypot(
            pose.position.x - sphere.center.x,
            pose.position.y - sphere.center.y,
            pose.position.z - sphere.center.z,
        );

        assert.ok(Math.abs(actual - fitDistance(sphere.radius, FOV, 1)) < 1e-6);
    });

    /** Y is up by the time the model reaches the client - the exporter already rotated it. */
    it('looks down on the model by default', () => {
        assert.ok(frameSphere(sphere, FOV, 1).position.y > sphere.center.y);
    });

    it('honours an explicit angle', () => {
        const front = frameSphere(sphere, FOV, 1, 0, 0);

        assert.ok(Math.abs(front.position.y - sphere.center.y) < 1e-6, 'level with the centre');
        assert.ok(Math.abs(front.position.x - sphere.center.x) < 1e-6, 'directly in front');
        assert.ok(front.position.z > sphere.center.z, 'in front, not behind');
    });

    it('orbits without changing distance', () => {
        const distances = [0, 90, 180, 270].map(azimuth => {
            const pose = frameSphere(sphere, FOV, 1, azimuth, 20);
            return Math.hypot(
                pose.position.x - sphere.center.x,
                pose.position.y - sphere.center.y,
                pose.position.z - sphere.center.z,
            );
        });

        for (const distance of distances) {
            assert.ok(Math.abs(distance - distances[0]) < 1e-6);
        }
    });
});

describe('clipPlanes', () => {
    /**
     * The corpus spans orders of magnitude - infantry are a couple of units across, a command centre
     * is thousands. One fixed near plane either clips the small models away or z-fights the large
     * ones into stripes, so both planes scale with the model.
     */
    /**
     * The depth buffer's precision is set almost entirely by the NEAR plane, and a coplanar overlay
     * is what pays for a bad one.
     *
     * Measured on `Ev_victorystardestroyer.ALO`, whose `Lighting` mesh sits on the hull: at a
     * far/near of 15135, 42% of that mesh lost the depth test and vanished into speckle. The
     * effects declare `ZWriteEnable = FALSE` and `ZFunc = LESSEQUAL` and no bias at all, so the
     * engine relies on plain depth precision to hold those overlays together - and so must this.
     */
    it('keeps the depth range tight enough to resolve a coplanar overlay', () => {
        for (const radius of [1, 10, 67, 500, 5000]) {
            const planes = clipPlanes(
                { center: { x: 0, y: 0, z: 0 }, radius }, radius * 3);

            assert.ok(planes.far / planes.near < 4000,
                `radius ${radius}: far/near is ${(planes.far / planes.near).toFixed(0)}`);
        }
    });

    it('still lets the reader get close to a surface before it clips', () => {
        // The other half of the trade: too large a near plane and dollying in to look at a hardpoint
        // slices the hull open. A hundredth of the radius is closer than anyone needs to get.
        const planes = clipPlanes({ center: { x: 0, y: 0, z: 0 }, radius: 67 }, 190);

        assert.ok(planes.near <= 67 / 100, `near is ${planes.near}`);
    });

    it('scales both planes with the model', () => {
        const small = clipPlanes({ center: { x: 0, y: 0, z: 0 }, radius: 1 }, 10);
        const large = clipPlanes({ center: { x: 0, y: 0, z: 0 }, radius: 5000 }, 20000);

        assert.ok(large.near > small.near);
        assert.ok(large.far > small.far);
    });

    it('always encloses the whole model', () => {
        const sphere = { center: { x: 0, y: 0, z: 0 }, radius: 100 };
        const distance = fitDistance(sphere.radius, FOV, 1);

        const planes = clipPlanes(sphere, distance);

        assert.ok(planes.near < distance - sphere.radius, 'the near face would be clipped');
        assert.ok(planes.far > distance + sphere.radius, 'the far face would be clipped');
    });

    it('keeps the near plane off zero for a degenerate model', () => {
        assert.ok(clipPlanes({ center: { x: 0, y: 0, z: 0 }, radius: 0 }, 1).near > 0);
    });

    /**
     * The far plane has to clear the EFFECTS, not just the geometry. Boba Fett is about seven units
     * tall and his flamethrower throws particles 140 to 210 a second for 1.2 seconds - so the flame
     * ends a couple of hundred units out and the scene was culling it halfway along, which reads as
     * the effect stopping rather than as the camera not seeing it.
     */
    it('reaches past an effect that flies far beyond the model', () => {
        const boba = { center: { x: 0, y: 0, z: 0 }, radius: 7 };
        const distance = fitDistance(boba.radius, FOV, 1);

        const tight = clipPlanes(boba, distance);
        const withFlame = clipPlanes(boba, distance, 250);

        assert.ok(tight.far < 250, 'the model-only fit was already big enough, so this proves nothing');
        assert.ok(withFlame.far > distance + 250, `the flame is still clipped: ${withFlame.far}`);
    });

    it('ignores a reach the model already covers', () => {
        const sphere = { center: { x: 0, y: 0, z: 0 }, radius: 500 };
        const distance = fitDistance(sphere.radius, FOV, 1);

        assert.equal(clipPlanes(sphere, distance, 5).far, clipPlanes(sphere, distance).far);
    });

    /** The multiplier is the reader's override, for the trails a fit cannot know about. */
    it('takes a draw-distance multiplier', () => {
        const sphere = { center: { x: 0, y: 0, z: 0 }, radius: 10 };

        const once = clipPlanes(sphere, 40, 0, 1).far;
        const further = clipPlanes(sphere, 40, 0, 4).far;

        assert.ok(Math.abs(further - once * 4) < 1e-6, `${further} against ${once}`);
    });
});

describe('sphereFromBounds', () => {
    it('centres between the corners and reaches them', () => {
        const sphere = sphereFromBounds({ x: -1, y: -2, z: -3 }, { x: 1, y: 2, z: 3 });

        assert.deepEqual(sphere.center, { x: 0, y: 0, z: 0 });
        assert.ok(Math.abs(sphere.radius - Math.sqrt(1 + 4 + 9)) < 1e-6);
    });

    it('handles an off-centre box', () => {
        const sphere = sphereFromBounds({ x: 10, y: 10, z: 10 }, { x: 20, y: 20, z: 20 });

        assert.deepEqual(sphere.center, { x: 15, y: 15, z: 15 });
    });
});

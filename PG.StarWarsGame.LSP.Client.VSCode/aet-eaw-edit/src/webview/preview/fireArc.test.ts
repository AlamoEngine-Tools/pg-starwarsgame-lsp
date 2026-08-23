// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { drawnConeRange, fireConeOutline, fireConePoints, fireConeSurface } from './fireArc';

describe('fireConePoints', () => {
    const length = (p: readonly number[]) => Math.hypot(p[0], p[1], p[2]);

    it('puts every rim point at the weapon s RANGE from the muzzle', () => {
        // The rim lies on a SPHERE, not on a flat plane at distance `range`. It used to be
        // `range * tan(halfAngle)`, which is the plane reading - and the Nebulon B declares
        // 175 x 160 degrees, so tan(87.5) = 22.9 made a 1100-unit weapon draw a sheet 25000 units
        // across. That is what "extremely flat and long" was.
        const { rim } = fireConePoints(175, 160, 1100, 24);

        for (const point of rim) {
            assert.ok(Math.abs(length(point) - 1100) < 1e-3,
                `rim point at ${length(point).toFixed(1)}, expected 1100`);
        }
    });

    it('never runs away as the arc approaches a half circle', () => {
        // The old geometry needed an 89-degree clamp to stop tan() exploding. On a sphere there is
        // nothing to clamp: a 179-degree arc is simply most of a hemisphere.
        const { rim } = fireConePoints(179, 179, 100, 16);

        for (const point of rim) {
            assert.ok(length(point) < 101);
        }
    });

    it('opens to the half-angle the XML declares', () => {
        // A 90-degree cone reaches 45 degrees off the bone's axis, so its widest rim point sits at
        // cos(45) along X and sin(45) across.
        const { rim } = fireConePoints(90, 90, 100, 4);
        const widest = rim.reduce((a, b) => (Math.abs(b[1]) > Math.abs(a[1]) ? b : a));

        assert.ok(Math.abs(widest[0] - 100 * Math.cos(Math.PI / 4)) < 1e-3);
        assert.ok(Math.abs(Math.abs(widest[1]) - 100 * Math.sin(Math.PI / 4)) < 1e-3);
    });

    it('is elliptical: width opens across Y and height across Z', () => {
        // A turret with a wide traverse and little elevation reads as the flat fan it actually is -
        // which is a different thing from the whole cone being flattened.
        const { rim } = fireConePoints(120, 20, 100, 32);
        const spreadY = Math.max(...rim.map(p => Math.abs(p[1])));
        const spreadZ = Math.max(...rim.map(p => Math.abs(p[2])));

        assert.ok(spreadY > spreadZ * 3);
    });

    it('points down the bone s local +X', () => {
        // Measured across every FP_ bone of Ev_stardestroyer.alo: local X has a positive dot with
        // the direction away from the hull. See `AlamoFireBone.AimDirection`.
        const { rim } = fireConePoints(20, 20, 100, 8);

        for (const point of rim) {
            assert.ok(point[0] > 0);
        }
    });

    it('starts at the muzzle', () => {
        assert.deepEqual(fireConePoints(20, 20, 100, 8).apex, [0, 0, 0]);
    });

    it('gives one rim point per segment', () => {
        assert.equal(fireConePoints(20, 20, 100, 12).rim.length, 12);
    });
});

describe('drawnConeRange', () => {
    it('clamps a reach that would swallow the viewport', () => {
        // A capital ship's arcs reach 2000 units on a hull about 600 long. Drawn as SOLID cones at
        // their true length the camera sits inside them and the whole viewport goes orange - which
        // is less use than the wire outline it replaced.
        assert.ok(drawnConeRange(2000, 600) < 2000);
    });

    it('keeps a short reach exactly as declared', () => {
        // A fighter's 500 against a 40-unit hull is already legible; shortening it would misreport
        // a weapon whose reach the reader CAN see in full.
        assert.equal(drawnConeRange(60, 600), 60);
    });

    it('scales with the subject, not with a fixed number', () => {
        // The corpus runs from a two-unit trooper to a thousand-unit command centre, so one cap
        // suits nothing. Same reasoning as the ground-plane range.
        assert.ok(drawnConeRange(9999, 40) < drawnConeRange(9999, 600));
    });

    it('never collapses to nothing', () => {
        assert.ok(drawnConeRange(2000, 0) > 0);
        assert.ok(drawnConeRange(2000, Number.NaN) > 0);
    });

    it('draws a WIDE arc shorter than a narrow one', () => {
        // The length that suits a cone depends on how much sky it covers. A Star Destroyer's six
        // banks are 160 by 130 degrees - very nearly domes - and at twice the hull they enclosed
        // the camera completely: the viewport was one orange field with the ship lost inside it.
        // A narrow arc is a spike, and a spike can be as long as it likes.
        assert.ok(drawnConeRange(2000, 600, 160, 130) < drawnConeRange(2000, 600, 20, 20));
    });

    it('keeps a wide arc clear of the hull it belongs to', () => {
        // Not so short that the gizmo disappears into the ship, either.
        const wide = drawnConeRange(2000, 600, 160, 130);

        assert.ok(wide > 600 * 0.4, `${wide} is inside the hull`);
        assert.ok(wide < 600, `${wide} still encloses a 600-unit subject`);
    });

    it('is unchanged for a reach that was already short enough', () => {
        // Whatever the width. A fighter's 60 units is legible as a cone or as a dome.
        assert.equal(drawnConeRange(60, 600, 175, 160), 60);
    });
});

describe('fireConeSurface', () => {
    const points = (t: number[]) =>
        Array.from({ length: t.length / 3 }, (_, i) => [t[i * 3], t[i * 3 + 1], t[i * 3 + 2]]);

    /** Angle off the bone's +X axis, in degrees. */
    const offAxis = (p: number[]) =>
        Math.atan2(Math.hypot(p[1], p[2]), p[0]) * (180 / Math.PI);

    it('never opens WIDER than the larger half-angle', () => {
        // The saddle, and what made them Pringles. The old build added the yaw and pitch offsets, so
        // a point 45 degrees round the rim of a 175-by-160 arc sat further off axis than either
        // half-angle allows - it bulged backwards, twice per turn.
        const tris = fireConeSurface(175, 160, 100, 32, 4);

        for (const p of points(tris)) {
            assert.ok(offAxis(p) <= 87.5 + 0.01,
                `a rim point sits ${offAxis(p).toFixed(1)} degrees off axis, past the 87.5 limit`);
        }
    });

    it('opens to exactly the declared half-angle on each axis', () => {
        // 90 wide by 30 tall: 45 degrees across Y, 15 across Z. The ellipse is in the ANGLES.
        const tris = fireConeSurface(90, 30, 100, 64, 2);
        const across = points(tris).filter(p => Math.abs(p[2]) < 0.5);
        const up = points(tris).filter(p => Math.abs(p[1]) < 0.5);

        assert.ok(Math.abs(Math.max(...across.map(offAxis)) - 45) < 1);
        assert.ok(Math.abs(Math.max(...up.map(offAxis)) - 15) < 1);
    });

    it('is between the two half-angles on the diagonal, never beyond', () => {
        const tris = fireConeSurface(90, 30, 100, 8, 1);
        const worst = Math.max(...points(tris).map(offAxis));

        assert.ok(worst <= 45.01, `diagonal reached ${worst.toFixed(1)}`);
    });

    it('has a ROUNDED bottom - the cap sits on the sphere', () => {
        // Every point is either the apex or exactly `range` away: straight sides from the muzzle,
        // a spherical end. That is the shape of a firing arc.
        const tris = fireConeSurface(60, 60, 100, 16, 3);

        for (const p of points(tris)) {
            const d = Math.hypot(p[0], p[1], p[2]);
            assert.ok(d < 0.01 || Math.abs(d - 100) < 0.01,
                `a vertex sits ${d.toFixed(2)} from the muzzle - neither apex nor rim`);
        }
    });

    it('closes the sides, so a narrow arc reads as a cone rather than a floating disc', () => {
        // At 20 degrees the walls ARE the cone; the cap is a small bulge on the end.
        const tris = fireConeSurface(20, 20, 100, 16, 2);

        assert.ok(points(tris).some(p => Math.hypot(p[0], p[1], p[2]) < 0.01),
            'no apex vertex, so the sides were never drawn');
    });

    it('draws nothing for an arc with no reach', () => {
        assert.deepEqual(fireConeSurface(90, 90, 0, 8, 2), []);
    });
});

describe('fireConeOutline', () => {
    const points = (t: number[]) =>
        Array.from({ length: t.length / 3 }, (_, i) => [t[i * 3], t[i * 3 + 1], t[i * 3 + 2]]);

    it('closes the rim, so the mouth of the cone reads as one edge', () => {
        const segments = 16;
        const lines = points(fireConeOutline(60, 40, 100, segments, 4));

        // Every rim vertex appears TWICE - once ending its segment, once starting the next - which
        // is what a closed loop looks like drawn as line segments. Four ribs on sixteen segments
        // reach the quarter points, which are rim vertices already, so they add no new ones.
        const rim = lines.filter(p => Math.hypot(p[0], p[1], p[2]) > 0.01);
        const distinct = new Set(rim.map(p => p.map(v => v.toFixed(3)).join(',')));

        assert.equal(distinct.size, segments, 'the rim should be one closed ring');
        assert.equal(rim.length, segments * 2 + 4, 'each rim vertex is used by two segments');
    });

    it('draws ribs from the muzzle to the rim', () => {
        // Without them a wide arc is a floating hoop: the eye needs the lines that say where the
        // cone comes from.
        const lines = points(fireConeOutline(60, 40, 100, 16, 4));

        assert.equal(lines.filter(p => Math.hypot(p[0], p[1], p[2]) < 0.01).length, 4);
    });

    it('traces the SAME rim the filled surface ends on', () => {
        // An outline that disagreed with the surface it edges would read as two shapes.
        const outline = points(fireConeOutline(90, 30, 100, 8, 0));
        const surface = points(fireConeSurface(90, 30, 100, 8, 1));
        const key = (p: number[]) => p.map(v => v.toFixed(2)).join(',');
        const onSurface = new Set(surface.map(key));

        for (const p of outline) {
            assert.ok(onSurface.has(key(p)), `${key(p)} is not on the surface's rim`);
        }
    });

    it('draws nothing for an arc with no reach', () => {
        assert.deepEqual(fireConeOutline(90, 90, 0, 8, 4), []);
    });
});

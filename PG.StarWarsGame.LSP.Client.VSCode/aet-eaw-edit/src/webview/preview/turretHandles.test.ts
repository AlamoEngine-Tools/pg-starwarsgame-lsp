// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import type { TurretSweep } from './deathClone';

import {
    aimText, atStop, axisRange, clampAim, clampAxis, dragToAim, handlesForShownArcs, turretEnvelopes,
    turretHandles,
} from './turretHandles';

function sweep(over: Partial<TurretSweep> = {}): TurretSweep {
    return {
        id: 'hardpoint:HP_Turret',
        partId: 'HP_Turret',
        turretBone: 'TURRET_01',
        barrelBone: 'BARREL_01',
        turret: { rotateExtentDegrees: 45, elevateExtentDegrees: 30 },
        ...over,
    };
}

function handle(rotate: number | null, elevate: number | null) {
    return turretHandles(
        [sweep({ turret: { rotateExtentDegrees: rotate, elevateExtentDegrees: elevate } })],
        new Set(['hardpoint:HP_Turret']))[0];
}

describe('axisRange', () => {
    // The engine's gate is `extent < 180.0 && extent > 0.0`, so the clamping band is OPEN at both
    // ends. Everything here is that one comparison read back.
    it('clamps inside the band, to plus and minus the extent', () => {
        assert.deepEqual(axisRange('yaw', 45), { axis: 'yaw', free: false, limitDegrees: 45 });
        assert.deepEqual(axisRange('pitch', 90), { axis: 'pitch', free: false, limitDegrees: 90 });
        assert.deepEqual(
            axisRange('yaw', 179.9), { axis: 'yaw', free: false, limitDegrees: 179.9 });
    });

    // 180 is the threshold itself and is NOT less than 180, so it is already the free case - which
    // is also the hardpoint's own default rotate extent.
    it('is free at 180 and above, because there are no stops left', () => {
        assert.equal(axisRange('yaw', 180).free, true);
        assert.equal(axisRange('yaw', 360).free, true);
        assert.equal(axisRange('yaw', 180).limitDegrees, 180);
    });

    /**
     * The surprising half of the same comparison: the gate excludes zero, so the clamp is SKIPPED
     * and the bone turns freely. That disagrees with the firing gate, where an extent of zero
     * refuses every bearing off dead centre. No shipped object authors one.
     */
    it('is free at zero and below, because the clamp is skipped rather than tightened', () => {
        assert.equal(axisRange('yaw', 0).free, true);
        assert.equal(axisRange('pitch', -10).free, true);
        assert.equal(axisRange('yaw', null).free, true);
        assert.equal(axisRange('yaw', undefined).free, true);
    });
});

describe('turretHandles', () => {
    // C3: a turret whose arc is shown has handles. The arc state is persistent and already lives at
    // three levels; hover cannot gate a manipulator the pointer has to travel to.
    it('follows the arc toggle rather than inventing a selection of its own', () => {
        assert.equal(turretHandles([sweep()], new Set(['hardpoint:HP_Turret'])).length, 1);
        assert.equal(turretHandles([sweep()], new Set()).length, 0);
        assert.equal(turretHandles([sweep()], new Set(['weapon:A'])).length, 0);
    });

    it('carries both bones, because the two axes sit on different ones', () => {
        const [only] = turretHandles([sweep()], new Set(['hardpoint:HP_Turret']));

        assert.equal(only.turretBone, 'TURRET_01');
        assert.equal(only.barrelBone, 'BARREL_01');
    });

    // 2 of 19 models name a turret bone and no barrel; both axes then sit on the one bone.
    it('keeps a null barrel bone null rather than guessing a name', () => {
        const [only] = turretHandles(
            [sweep({ barrelBone: null })], new Set(['hardpoint:HP_Turret']));

        assert.equal(only.barrelBone, null);
    });

    it('reads each axis off its own extent', () => {
        const only = handle(45, 30);

        assert.deepEqual(only.yaw, { axis: 'yaw', free: false, limitDegrees: 45 });
        assert.deepEqual(only.pitch, { axis: 'pitch', free: false, limitDegrees: 30 });
    });
});

describe('clamping an aim', () => {
    it('stops a bounded axis at its own extent', () => {
        assert.equal(clampAxis(axisRange('yaw', 45), 60), 45);
        assert.equal(clampAxis(axisRange('yaw', 45), -60), -45);
        assert.equal(clampAxis(axisRange('yaw', 45), 20), 20);
    });

    // A free axis has no stop to hold it, so it wraps the way the engine's own Clamp_180 does.
    it('wraps a free axis instead of stopping it', () => {
        assert.equal(clampAxis(axisRange('yaw', 360), 190), -170);
        assert.equal(clampAxis(axisRange('yaw', 360), -190), 170);
        assert.equal(clampAxis(axisRange('yaw', 360), 540), 180);
    });

    it('clamps the two axes independently', () => {
        assert.deepEqual(
            clampAim(handle(45, 180), { yaw: 90, pitch: 200 }), { yaw: 45, pitch: -160 });
    });
});

describe('atStop', () => {
    it('is true only when an axis is sitting on its own stop', () => {
        const yaw = axisRange('yaw', 45);

        assert.equal(atStop(yaw, 45), true);
        assert.equal(atStop(yaw, -45), true);
        assert.equal(atStop(yaw, 44), false);
    });

    // A reader cannot tell a stop from a dropped pointer event, so a free axis must never claim one.
    it('is never true on an axis that has no stops', () => {
        assert.equal(atStop(axisRange('yaw', 360), 180), false);
        assert.equal(atStop(axisRange('yaw', 0), 0), false);
    });
});

describe('dragToAim', () => {
    it('turns screen X into yaw and screen Y into pitch', () => {
        assert.deepEqual(
            dragToAim(handle(180, 180), { yaw: 0, pitch: 0 }, 10, 0, 0.5),
            { yaw: 5, pitch: 0 });
    });

    // Dragging UP raises the barrel, matching the viewport's nose-up convention.
    it('inverts Y so that dragging up raises the barrel', () => {
        assert.equal(
            dragToAim(handle(180, 180), { yaw: 0, pitch: 0 }, 0, -20, 0.5).pitch, 10);
    });

    it('measures from where the drag started, not from rest', () => {
        assert.equal(
            dragToAim(handle(180, 180), { yaw: 30, pitch: 0 }, 10, 0, 0.5).yaw, 35);
    });

    // The handle cannot be dragged past a stop - the clamp is in the same call, so there is no
    // frame in which the bone is somewhere the engine would not allow.
    it('cannot be dragged past a stop', () => {
        assert.equal(dragToAim(handle(45, 30), { yaw: 40, pitch: 0 }, 100, 0, 0.5).yaw, 45);
    });
});

describe('aimText', () => {
    it('reads out both axes, in degrees', () => {
        assert.equal(
            aimText(handle(45, 30), { yaw: 12.34, pitch: -5 }),
            'Yaw 12.3 deg, Pitch -5 deg');
    });

    it('says when an axis is at its stop rather than leaving it to be inferred', () => {
        assert.equal(
            aimText(handle(45, 30), { yaw: 45, pitch: 0 }),
            'Yaw 45 deg (at stop), Pitch 0 deg');
    });

    it('says when an axis has no stops at all', () => {
        assert.equal(
            aimText(handle(360, 30), { yaw: 100, pitch: 0 }),
            'Yaw 100 deg (free), Pitch 0 deg');
    });
});

describe('turretEnvelopes', () => {
    /**
     * C2, and the reason the feature exists. `Can_Weapon_Point_At`'s turret branch tests the yaw
     * extent and nothing else, so the SHOT has no pitch bound; `Calculate_Desired_Turret_Angle`
     * clamps both, so the BARREL does. The two envelopes are different shapes.
     */
    it('gives the shot no pitch bound while the barrel keeps one', () => {
        const both = turretEnvelopes(handle(45, 30));

        assert.equal(both.fire.pitchDegrees, 360);
        assert.equal(both.rotation.pitchDegrees, 60);
    });

    it('shares the yaw between them, because one number bounds both', () => {
        const both = turretEnvelopes(handle(45, 30));

        assert.equal(both.fire.yawDegrees, 90);
        assert.equal(both.rotation.yawDegrees, 90);
    });

    // The extents are HALF angles and the geometry takes FULL ones. Doubling in one place is the
    // whole remedy for the error this workstream found three times over.
    it('doubles the extent into a full angle, and stops at a whole turn', () => {
        assert.equal(turretEnvelopes(handle(45, 30)).rotation.yawDegrees, 90);
        assert.equal(turretEnvelopes(handle(179, 30)).rotation.yawDegrees, 358);
        assert.equal(turretEnvelopes(handle(360, 30)).rotation.yawDegrees, 360);
    });
});

describe('handlesForShownArcs', () => {
    /**
     * The regression. A hardpoint turret is reached twice - once as a HARDPOINT, keyed
     * `HP_Gargantuan_Main_Turret_Front`, and once as the WEAPON on it, keyed
     * `hardpoint:HP_Gargantuan_Main_Turret_Front` - and the arc toggle speaks the weapon's key.
     * Building the handles from the hardpoint list meant no Gargantuan turret could ever match, so
     * the handles never appeared. They are built from the weapons now.
     */
    it('keys a hardpoint turret by its WEAPON id, which is what the arc toggle uses', () => {
        const handles = handlesForShownArcs(
            [{
                id: 'hardpoint:HP_Gargantuan_Main_Turret_Front',
                partId: 'HP_Gargantuan_Main_Turret_Front',
                turret: {
                    rotateExtentDegrees: 120, elevateExtentDegrees: 45,
                    turretBone: 'B_Turret_Front', barrelBone: 'B_Barrel_Front',
                },
            }],
            new Set(['hardpoint:HP_Gargantuan_Main_Turret_Front']));

        assert.equal(handles.length, 1);
        assert.equal(handles[0].id, 'hardpoint:HP_Gargantuan_Main_Turret_Front');
        assert.equal(handles[0].yaw.limitDegrees, 120);
    });

    it('gives nothing to a turret whose arc is switched off', () => {
        assert.deepEqual(handlesForShownArcs(
            [{ id: 'weapon:A', partId: 'hull', turret: { turretBone: 'B_Turret', rotateExtentDegrees: 360 } }],
            new Set()), []);
    });

    it('gives nothing to a weapon that names no turret bone', () => {
        assert.deepEqual(handlesForShownArcs(
            [{ id: 'weapon:A', partId: 'hull', turret: null }],
            new Set(['weapon:A'])), []);
    });
});

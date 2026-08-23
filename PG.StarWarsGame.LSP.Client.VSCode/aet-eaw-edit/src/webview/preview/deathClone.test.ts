// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import type { PreviewDeathClone, PreviewTurret } from '../../protocol/modelPreview';

import {
    cloneForDamage, deathCloneRows, sweepAngles, turretSweeps,
} from './deathClone';

const CLONES: PreviewDeathClone[] = [
    { damageType: 'Damage_Normal', objectId: 'SD_Death_Clone', modelFile: 'EV_SD_D.ALO', playsIdle: false, animations: [], particles: [] },
    { damageType: 'Damage_Force_Lightning', objectId: 'Lightning_Clone', modelFile: 'X_D.ALO', playsIdle: true, animations: [], particles: [] },
    { damageType: null, objectId: 'Fallback_Clone', modelFile: null, playsIdle: false, animations: [], particles: [] },
];

describe('cloneForDamage', () => {
    it('picks the clone whose damage type matches the weapon you built', () => {
        // The whole reason this is in the Gameplay lens rather than a static list: what kills the
        // unit decides what it leaves behind, and the attacker panel is where you choose that.
        assert.equal(cloneForDamage(CLONES, 'Damage_Force_Lightning')?.objectId, 'Lightning_Clone');
    });

    it('matches the damage type however either side cased it', () => {
        assert.equal(cloneForDamage(CLONES, 'damage_normal')?.objectId, 'SD_Death_Clone');
    });

    /**
     * `Damage_Normal` is the ORDINARY death, and everything else is a special one.
     *
     * Measured over foc's 134 `Death_Clone` rows: 59 name `Damage_Normal` and every other type is a
     * particular way to die - `Damage_Force_Whirlwind` 27, `Damage_Force_Lightning` 27,
     * `Damage_Crush` 11, `Damage_Fire` 4. So a turbolaser killing a Star Destroyer produces its
     * `Damage_Normal` wreck; only a specific kill swaps in a specific one. Treating `Damage_Normal`
     * as just another exact match meant the ordinary weapon produced NO clone at all, which is what
     * "the death clone never gets triggered" was.
     */
    it('falls back to the Damage_Normal clone for an ordinary kill', () => {
        assert.equal(cloneForDamage(CLONES, 'Damage_Turbolaser')?.objectId, 'SD_Death_Clone');
    });

    it('still prefers a specific clone over the normal one', () => {
        assert.equal(cloneForDamage(CLONES, 'Damage_Force_Lightning')?.objectId, 'Lightning_Clone');
    });

    it('falls back to a row that names no damage type, when there is no normal one', () => {
        // NOT a shipped shape - all 134 rows name a type - but a mod could write one, and a row
        // that names nothing can only mean "anything".
        const noNormal = CLONES.filter(c => c.damageType !== 'Damage_Normal');

        assert.equal(cloneForDamage(noNormal, 'Damage_Invented')?.objectId, 'Fallback_Clone');
    });

    it('is nothing when no row matches and there is neither a normal nor a catch-all', () => {
        // 28 of the 33 objects declaring a specific clone declare ONLY specific ones. An ordinary
        // kill on one of those leaves no wreck, and that is the file's own answer.
        const specific = CLONES.filter(c => c.damageType === 'Damage_Force_Lightning');

        assert.equal(cloneForDamage(specific, 'Damage_Invented'), null);
    });

    it('is nothing for a subject that leaves no clone at all', () => {
        assert.equal(cloneForDamage([], 'Damage_Normal'), null);
    });
});

describe('deathCloneRows', () => {
    it('marks the row the current weapon would actually produce', () => {
        const rows = deathCloneRows(CLONES, 'Damage_Force_Lightning');

        assert.deepEqual(rows.map(r => r.selected), [false, true, false]);
    });

    it('says what each row leaves behind', () => {
        const rows = deathCloneRows(CLONES, 'Damage_Normal');

        assert.match(rows[0].detail, /EV_SD_D\.ALO/);
        assert.match(rows[1].detail, /plays its idle/i);
    });

    it('marks a clone that is not defined rather than showing a blank row', () => {
        // A typo costs the wreck entirely and the game says nothing about it.
        const rows = deathCloneRows(
            [{ damageType: 'Damage_Normal', objectId: 'Missing', modelFile: null, playsIdle: false, animations: [], particles: [] }],
            'Damage_Normal');

        assert.match(rows[0].detail, /not defined|no model/i);
    });

    it('labels the catch-all row as what it is', () => {
        const rows = deathCloneRows(CLONES, 'Damage_Normal');

        assert.match(rows[2].label, /any (other )?damage/i);
    });
});

describe('sweepAngles', () => {
    const turret: PreviewTurret = {
        restAngle: 0, rotateExtentDegrees: 90, elevateExtentDegrees: 45,
        turretBone: 'B_Turret', barrelBone: 'B_Barrel',
    };

    it('swings the turret between the extents its XML declares', () => {
        // Not a full circle: a 90-degree traverse means 45 either side of rest, and drawing more
        // would show a reach the unit does not have.
        const at = (t: number) => sweepAngles(turret, t);

        assert.equal(at(0).rotate, 0);
        assert.ok(Math.abs(at(0.25).rotate - 45) < 1e-6);
        assert.ok(Math.abs(at(0.75).rotate + 45) < 1e-6);
    });

    it('elevates on its own extent, which is usually much smaller', () => {
        // AT_AA is 360 rotate by 45 elevate; using one number for both would tip a turret through
        // the hull.
        const peak = sweepAngles(turret, 0.25);

        assert.ok(Math.abs(peak.elevate) <= 45 / 2 + 1e-6);
    });

    it('is centred on the rest angle the XML gives', () => {
        const offset = sweepAngles({ ...turret, restAngle: 180 }, 0);

        assert.equal(offset.rotate, 180);
    });

    it('does not move a turret that declares no extents', () => {
        // 53 of foc's weapon objects declare the extents and the rest do not. A sweep that invented
        // one would claim a traverse the unit has not got.
        const still = sweepAngles(
            { restAngle: 0, turretBone: 'B', barrelBone: null }, 0.25);

        assert.deepEqual(still, { rotate: 0, elevate: 0 });
    });

    it('treats a full 360 traverse as a continuous turn rather than a swing', () => {
        // A 360 turret has no end stops, so swinging back and forth would misrepresent it.
        const full = { ...turret, rotateExtentDegrees: 360 };

        assert.ok(Math.abs(sweepAngles(full, 0.25).rotate - 90) < 1e-6);
        assert.ok(Math.abs(sweepAngles(full, 0.5).rotate - 180) < 1e-6);
    });
});

describe('turretSweeps', () => {
    const extents: PreviewTurret = {
        turretBone: 'B_Turret_Base', barrelBone: 'B_Missile_Launcher',
        rotateExtentDegrees: 360, elevateExtentDegrees: 45,
    };

    it('finds a turret declared on a UNIT WEAPON, not only on a hardpoint', () => {
        // MEASURED on the live AT-AA: its turret is `bank:A` with B_Turret_Base / B_Missile_Launcher
        // at 360 by 45, and it has NO hardpoints at all. Reading hardpoints alone found nothing to
        // sweep on the very unit the feature exists for.
        const sweeps = turretSweeps([], [{ id: 'bank:A', partId: 'hull', turret: extents }]);

        assert.equal(sweeps.length, 1);
        assert.equal(sweeps[0].turretBone, 'B_Turret_Base');
        assert.equal(sweeps[0].barrelBone, 'B_Missile_Launcher');
        assert.equal(sweeps[0].partId, 'hull');
    });

    it('finds one declared on a hardpoint, on that mount s own part', () => {
        const sweeps = turretSweeps(
            [{ id: 'HP_Gun', partId: 'hp:HP_Gun', turret: extents }], []);

        assert.equal(sweeps[0].partId, 'hp:HP_Gun');
    });

    it('lists one turret ONCE when both a mount and its weapon name it', () => {
        // A hardpoint weapon carries the mount's turret too, so the naive union sweeps the same bone
        // twice - and the second pass fights the first.
        const sweeps = turretSweeps(
            [{ id: 'HP_Gun', partId: 'hp:HP_Gun', turret: extents }],
            [{ id: 'hardpoint:HP_Gun', partId: 'hp:HP_Gun', turret: extents }]);

        assert.equal(sweeps.length, 1);
    });

    it('leaves out a turret that declares no bone to turn', () => {
        assert.deepEqual(turretSweeps(
            [{ id: 'HP', partId: 'hull', turret: { rotateExtentDegrees: 90 } }], []), []);
    });

    it('leaves out a turret with no traverse at all', () => {
        // 53 of foc's weapon objects declare the extents and the rest do not. Sweeping one that
        // declares neither would claim a reach it has not got.
        assert.deepEqual(turretSweeps(
            [{ id: 'HP', partId: 'hull', turret: { turretBone: 'B_Turret' } }], []), []);
    });
});

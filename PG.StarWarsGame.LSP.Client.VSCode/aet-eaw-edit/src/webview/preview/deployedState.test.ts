// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import type { PreviewWeapon } from '../../protocol/modelPreview';

import { isDeployedClip, weaponsInState } from './deployedState';

describe('isDeployedClip', () => {
    /**
     * The engine is deployed only in `LST_WALK_DEPLOYED` - after the deploy has run, not while it runs
     * (`Is_Deploying`). So the deploy clip counts once it has played to its end and is held there.
     */
    it('counts the deploy clip only once it is held at its end', () => {
        assert.equal(isDeployedClip({ action: 'deploy', stance: 'normal' }, false), false);
        assert.equal(isDeployedClip({ action: 'deploy', stance: 'normal' }, true), true);
    });

    /** Every `deployed_*` clip - the deployed idle, walk, power-up and deaths - is played in the state. */
    it('counts any clip held in the deployed stance, wherever the playhead is', () => {
        assert.equal(isDeployedClip({ action: 'die', stance: 'deployed' }, false), true);
        assert.equal(isDeployedClip({ action: 'powerup', stance: 'deployed' }, true), true);
    });

    it('does not count the undeploy clip at any point', () => {
        assert.equal(isDeployedClip({ action: 'undeploy', stance: 'normal' }, false), false);
        assert.equal(isDeployedClip({ action: 'undeploy', stance: 'normal' }, true), false);
    });

    it('does not count an ordinary clip or no clip at all', () => {
        assert.equal(isDeployedClip({ action: 'idle', stance: 'normal' }, true), false);
        assert.equal(isDeployedClip(null, true), false);
    });
});

function weapon(over: Partial<PreviewWeapon> = {}): PreviewWeapon {
    return {
        id: 'weapon:A',
        source: 'Unit',
        label: 'Muzzle A',
        fireBones: ['MuzzleA_00'],
        firePointMode: 'CycleBones',
        firesForward: false,
        inaccuracy: [],
        fireModes: [],
        coneWidthDegrees: 110,
        coneHeightDegrees: 120,
        deployedConeWidthDegrees: 360,
        deployedConeHeightDegrees: 360,
        turret: {
            rotateExtentDegrees: 55,
            elevateExtentDegrees: 60,
            turretBone: 'Turret',
            deployedRotateExtentDegrees: 360,
            deployedElevateExtentDegrees: 180,
        },
        ...over,
    };
}

describe('weaponsInState', () => {
    it('leaves every weapon untouched while not deployed', () => {
        const weapons = [weapon()];

        assert.equal(weaponsInState(weapons, false), weapons);
    });

    /** The AT-AT: 55 / 60 normally, unrestricted while deployed, turret swing included. */
    it('swaps in the deployed arc and the deployed turret limits', () => {
        const [deployed] = weaponsInState([weapon()], true);

        assert.equal(deployed.coneWidthDegrees, 360);
        assert.equal(deployed.coneHeightDegrees, 360);
        assert.equal(deployed.turret?.rotateExtentDegrees, 360);
        assert.equal(deployed.turret?.elevateExtentDegrees, 180);
        assert.equal(deployed.turret?.turretBone, 'Turret');
    });

    /** A weapon on a unit that is never deployed - every hardpoint, every fighter - keeps its arc. */
    it('keeps the arc of a weapon that has no deployed values', () => {
        const hardpoint = weapon({
            deployedConeWidthDegrees: null,
            deployedConeHeightDegrees: undefined,
            turret: { rotateExtentDegrees: 45, elevateExtentDegrees: 30 },
        });

        const [kept] = weaponsInState([hardpoint], true);

        assert.equal(kept.coneWidthDegrees, 110);
        assert.equal(kept.turret?.rotateExtentDegrees, 45);
    });

    it('does not mutate the weapons it was given', () => {
        const original = weapon();

        weaponsInState([original], true);

        assert.equal(original.coneWidthDegrees, 110);
        assert.equal(original.turret?.rotateExtentDegrees, 55);
    });
});

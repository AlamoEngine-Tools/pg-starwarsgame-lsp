// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import type { PreviewHardpoint, PreviewTargetDefence } from '../../protocol/modelPreview';

import { hullPool, unitDestroyed, unitTargetable } from './unitPool';

function hardpoint(over: Partial<PreviewHardpoint> = {}): PreviewHardpoint {
    return {
        id: 'HP_Weapon',
        partId: 'hp:HP_Weapon',
        type: null,
        attachBone: 'HP_Bone',
        isDestroyable: true,
        isTargetable: true,
        health: 350,
        damageParticlesBone: null,
        damageDecalBone: null,
        collisionMeshBone: null,
        engineParticlesBone: null,
        deathExplosionParticles: null,
        deathBreakoffProp: null,
        engineDeathHidesEngineParticles: false,
        tooltipText: null,
        turret: null,
        ...over,
    };
}

const DEFENCE: PreviewTargetDefence = {
    isShielded: true,
    armorType: 'Armor_Star_Destroyer',
    shieldArmorType: 'Shield_Capital',
    shieldPoints: 1000,
    tacticalHealth: 2000,
    energyCapacity: null,
    hardpointHealthTotal: 1050,
    hullFactors: {},
    shieldFactors: {},
    damageTypes: [],
};

const MOUNTS = [
    hardpoint({ id: 'A', health: 350 }),
    hardpoint({ id: 'B', health: 375 }),
    hardpoint({ id: 'C', health: 325 }),
];

describe('hullPool', () => {
    it('is the SUM of the mounts on a unit that has them', () => {
        // The convention most mods author to. Nobody established what the engine's own
        // Hull_Vs_Hard_Points_Health_Constraint computes, so the preview draws the rule modders
        // actually use rather than inventing one from the constant.
        const pool = hullPool(DEFENCE, MOUNTS, { A: 350, B: 375, C: 325 });

        assert.equal(pool.max, 1050);
        assert.equal(pool.current, 1050);
        assert.equal(pool.fromHardpoints, true);
    });

    it('drains as mounts take damage', () => {
        const pool = hullPool(DEFENCE, MOUNTS, { A: 100, B: 375, C: 0 });

        assert.equal(pool.current, 475);
        assert.equal(pool.max, 1050);
    });

    it('reaches zero exactly when the last mount does', () => {
        const pool = hullPool(DEFENCE, MOUNTS, { A: 0, B: 0, C: 0 });

        assert.equal(pool.current, 0);
    });

    it('uses the unit\'s OWN health where it has no hardpoints', () => {
        // A fighter takes damage on its own pool directly, which is the other half of the rule.
        const pool = hullPool({ ...DEFENCE, hardpointHealthTotal: null }, [], {});

        assert.equal(pool.max, 2000);
        assert.equal(pool.fromHardpoints, false);
    });

    it('never counts a mount that cannot die', () => {
        // An indestructible mount would put a floor under the bar that nothing could remove.
        const mounts = [...MOUNTS, hardpoint({ id: 'Fixed', isDestroyable: false, health: 999 })];
        const pool = hullPool(DEFENCE, mounts, { A: 350, B: 375, C: 325, Fixed: 999 });

        assert.equal(pool.current, 1050);
    });

    it('treats a mount with no declared health as contributing nothing', () => {
        // 210 of foc's hardpoints declare no Health at all - they are indestructible rather than
        // already dead, and reading a null as zero would show a ship starting at half strength.
        const mounts = [hardpoint({ id: 'A', health: 350 }), hardpoint({ id: 'N', health: null })];
        const pool = hullPool(DEFENCE, mounts, { A: 350, N: null });

        assert.equal(pool.max, 350);
        assert.equal(pool.current, 350);
    });

    it('counts a mount destroyed by hand as gone, whatever its health says', () => {
        // The two are one fact wearing two faces: a mount ticked off in the Hardpoints list is
        // destroyed without anything ever subtracting its health, and reading the health alone left
        // the bar at full while the ship lost mount after mount.
        const pool = hullPool(DEFENCE, MOUNTS, { A: 350, B: 375, C: 325 }, new Set(['A', 'B']));

        assert.equal(pool.current, 325);
    });

    it('is empty once every mount has been destroyed by hand', () => {
        const pool = hullPool(DEFENCE, MOUNTS, { A: 350, B: 375, C: 325 },
            new Set(['A', 'B', 'C']));

        assert.equal(pool.current, 0);
    });

    it('never reports more left than it started with', () => {
        assert.equal(hullPool(DEFENCE, MOUNTS, { A: 9999, B: 375, C: 325 }).current, 1050);
    });
});

describe('unitDestroyed', () => {
    it('is true once every destructible mount is gone', () => {
        assert.equal(unitDestroyed(MOUNTS, new Set(['A', 'B', 'C'])), true);
    });

    it('is false while one still stands', () => {
        assert.equal(unitDestroyed(MOUNTS, new Set(['A', 'B'])), false);
    });

    it('counts an UNTARGETABLE mount, which is the whole point', () => {
        // The game warns about untargetable-but-destructible, and at least two mods use it as a
        // gameplay element - a ship that cannot be finished off by shooting what you can see. It
        // still has to die before the unit does.
        const mounts = [hardpoint({ id: 'A' }), hardpoint({ id: 'Hidden', isTargetable: false })];

        assert.equal(unitDestroyed(mounts, new Set(['A'])), false);
        assert.equal(unitDestroyed(mounts, new Set(['A', 'Hidden'])), true);
    });

    it('ignores an indestructible mount, which could never die', () => {
        // Otherwise the unit could never die either.
        const mounts = [hardpoint({ id: 'A' }), hardpoint({ id: 'Fixed', isDestroyable: false })];

        assert.equal(unitDestroyed(mounts, new Set(['A'])), true);
    });

    it('is false for a unit with no destructible mounts at all', () => {
        // Nothing to finish, so this rule does not apply - a fighter dies by its own health pool.
        assert.equal(unitDestroyed([], new Set()), false);
        assert.equal(unitDestroyed([hardpoint({ isDestroyable: false })], new Set()), false);
    });
});

describe('unitTargetable', () => {
    it('says a unit WITH hardpoints cannot be shot at directly', () => {
        // Only its mounts can be. The attacker panel's "Hull" choice is meaningless there.
        assert.equal(unitTargetable(MOUNTS), false);
    });

    it('says a unit without them can', () => {
        assert.equal(unitTargetable([]), true);
    });

    it('says a unit whose only mounts are indestructible can', () => {
        // Nothing there can ever be destroyed, so shooting the mounts could never kill it.
        assert.equal(unitTargetable([hardpoint({ isDestroyable: false })]), true);
    });
});

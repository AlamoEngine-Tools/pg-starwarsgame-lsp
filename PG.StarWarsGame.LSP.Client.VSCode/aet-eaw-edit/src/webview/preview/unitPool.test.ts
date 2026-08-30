// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import type { PreviewHardpoint, PreviewTargetDefence } from '../../protocol/modelPreview';

import { hullPool, shieldGeneratorsDown, unitDestroyed, unitTargetable } from './unitPool';

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

const HARDPOINTS = [
    hardpoint({ id: 'A', health: 350 }),
    hardpoint({ id: 'B', health: 375 }),
    hardpoint({ id: 'C', health: 325 }),
];

describe('hullPool', () => {
    it('is the SUM of the hardpoints on a unit that has them', () => {
        // The convention most mods author to. Nobody established what the engine's own
        // Hull_Vs_Hard_Points_Health_Constraint computes, so the preview draws the rule modders
        // actually use rather than inventing one from the constant.
        const pool = hullPool(DEFENCE, HARDPOINTS, { A: 350, B: 375, C: 325 });

        assert.equal(pool.max, 1050);
        assert.equal(pool.current, 1050);
        assert.equal(pool.fromHardpoints, true);
    });

    it('drains as hardpoints take damage', () => {
        const pool = hullPool(DEFENCE, HARDPOINTS, { A: 100, B: 375, C: 0 });

        assert.equal(pool.current, 475);
        assert.equal(pool.max, 1050);
    });

    it('reaches zero exactly when the last hardpoint does', () => {
        const pool = hullPool(DEFENCE, HARDPOINTS, { A: 0, B: 0, C: 0 });

        assert.equal(pool.current, 0);
    });

    it('uses the unit\'s OWN health where it has no hardpoints', () => {
        // A fighter takes damage on its own pool directly, which is the other half of the rule.
        const pool = hullPool({ ...DEFENCE, hardpointHealthTotal: null }, [], {});

        assert.equal(pool.max, 2000);
        assert.equal(pool.fromHardpoints, false);
    });

    it('never counts a hardpoint that cannot die', () => {
        // An indestructible hardpoint would put a floor under the bar that nothing could remove.
        const hardpoints = [...HARDPOINTS, hardpoint({ id: 'Fixed', isDestroyable: false, health: 999 })];
        const pool = hullPool(DEFENCE, hardpoints, { A: 350, B: 375, C: 325, Fixed: 999 });

        assert.equal(pool.current, 1050);
    });

    it('treats a hardpoint with no declared health as contributing nothing', () => {
        // 210 of foc's hardpoints declare no Health at all - they are indestructible rather than
        // already dead, and reading a null as zero would show a ship starting at half strength.
        const hardpoints = [hardpoint({ id: 'A', health: 350 }), hardpoint({ id: 'N', health: null })];
        const pool = hullPool(DEFENCE, hardpoints, { A: 350, N: null });

        assert.equal(pool.max, 350);
        assert.equal(pool.current, 350);
    });

    it('counts a hardpoint destroyed by hand as gone, whatever its health says', () => {
        // The two are one fact wearing two faces: a hardpoint ticked off in the Hardpoints list is
        // destroyed without anything ever subtracting its health, and reading the health alone left
        // the bar at full while the ship lost hardpoint after hardpoint.
        const pool = hullPool(DEFENCE, HARDPOINTS, { A: 350, B: 375, C: 325 }, new Set(['A', 'B']));

        assert.equal(pool.current, 325);
    });

    it('is empty once every hardpoint has been destroyed by hand', () => {
        const pool = hullPool(DEFENCE, HARDPOINTS, { A: 350, B: 375, C: 325 },
            new Set(['A', 'B', 'C']));

        assert.equal(pool.current, 0);
    });

    it('never reports more left than it started with', () => {
        assert.equal(hullPool(DEFENCE, HARDPOINTS, { A: 9999, B: 375, C: 325 }).current, 1050);
    });
});

describe('the hull pool of a unit with no hardpoints', () => {
    // 188 objects over the two trees carry their weapons as WEAPON behaviour and no hardpoints at
    // all, against 68 with hardpoints - so this is the COMMON unit, not an edge case.
    const NO_HARDPOINTS: PreviewHardpoint[] = [];

    it('reports what the unit has left, not what it started with', () => {
        // The bug. `hullPool` answered `tacticalHealth` for both ends of the bar, so the reader
        // could empty the pool with the attacker panel and watch a full hull bar the whole time -
        // the number that depletes and the number that is drawn were two different numbers.
        const pool = hullPool(DEFENCE, NO_HARDPOINTS, {}, new Set(), 750);

        assert.equal(pool.current, 750);
        assert.equal(pool.max, 2000);
        assert.equal(pool.fromHardpoints, false);
    });

    it('opens at full health when nothing has been fired yet', () => {
        const pool = hullPool(DEFENCE, NO_HARDPOINTS, {}, new Set());

        assert.equal(pool.current, 2000);
        assert.equal(pool.max, 2000);
    });

    it('clamps a live value to the bar it is drawn in', () => {
        // Same reason the hardpoint sum is clamped: a repair that overshoots or a stale entry must
        // not report a ship in better condition than it was built in, and an overkill shot must not
        // draw a negative bar.
        assert.equal(hullPool(DEFENCE, NO_HARDPOINTS, {}, new Set(), -400).current, 0);
        assert.equal(hullPool(DEFENCE, NO_HARDPOINTS, {}, new Set(), 9999).current, 2000);
    });

    it('leaves a unit WITH hardpoints reading its hardpoints', () => {
        // The hardpoints are the authority there, and the live hull number is the panel's own
        // running total for a pool that unit does not use.
        const pool = hullPool(DEFENCE, HARDPOINTS, { A: 350, B: 375, C: 325 }, new Set(), 5);

        assert.equal(pool.current, 1050);
        assert.equal(pool.fromHardpoints, true);
    });
});

describe('unitDestroyed', () => {
    it('is true once every destructible hardpoint is gone', () => {
        assert.equal(unitDestroyed(HARDPOINTS, new Set(['A', 'B', 'C'])), true);
    });

    it('is false while one still stands', () => {
        assert.equal(unitDestroyed(HARDPOINTS, new Set(['A', 'B'])), false);
    });

    it('counts an UNTARGETABLE hardpoint, which is the whole point', () => {
        // The game warns about untargetable-but-destructible, and at least two mods use it as a
        // gameplay element - a ship that cannot be finished off by shooting what you can see. It
        // still has to die before the unit does.
        const hardpoints = [hardpoint({ id: 'A' }), hardpoint({ id: 'Hidden', isTargetable: false })];

        assert.equal(unitDestroyed(hardpoints, new Set(['A'])), false);
        assert.equal(unitDestroyed(hardpoints, new Set(['A', 'Hidden'])), true);
    });

    it('ignores an indestructible hardpoint, which could never die', () => {
        // Otherwise the unit could never die either.
        const hardpoints = [hardpoint({ id: 'A' }), hardpoint({ id: 'Fixed', isDestroyable: false })];

        assert.equal(unitDestroyed(hardpoints, new Set(['A'])), true);
    });

    it('is false for a unit with no destructible hardpoints at all', () => {
        // Nothing to finish, so this rule does not apply - a fighter dies by its own health pool.
        assert.equal(unitDestroyed([], new Set()), false);
        assert.equal(unitDestroyed([hardpoint({ isDestroyable: false })], new Set()), false);
    });
});

describe('a unit with no hardpoints dying', () => {
    // Reported: it could not die at all. `unitDestroyed` required a destructible hardpoint, and
    // its own doc said such a unit "dies by its own health pool" - which nothing implemented. Every
    // fighter, every infantry squad and most ground vehicles were unkillable.
    const NO_HARDPOINTS: PreviewHardpoint[] = [];

    it('dies when its hull pool is emptied', () => {
        assert.equal(
            unitDestroyed(NO_HARDPOINTS, new Set(), { current: 0, max: 2000, fromHardpoints: false }),
            true);
    });

    it('lives while it has any hull left', () => {
        assert.equal(
            unitDestroyed(NO_HARDPOINTS, new Set(), { current: 1, max: 2000, fromHardpoints: false }),
            false);
    });

    it('does not die on open just because it declares no health', () => {
        // The trap the old `destructible.length > 0` guard was really protecting against, moved
        // rather than dropped: a pool with no maximum is a unit that does not use this channel, not
        // a unit at zero. Answering true would kill it the moment the preview opened.
        assert.equal(
            unitDestroyed(NO_HARDPOINTS, new Set(), { current: 0, max: 0, fromHardpoints: false }),
            false);
    });

    it('is alive when nothing has told it about a pool at all', () => {
        assert.equal(unitDestroyed(NO_HARDPOINTS, new Set()), false);
        assert.equal(unitDestroyed(NO_HARDPOINTS, new Set(), null), false);
    });

    it('leaves the hardpoint rule alone where there are hardpoints', () => {
        // An empty hull reading must not kill a ship that still has a hardpoint standing: for those
        // the pool is the SUM of the hardpoints, and the hardpoints are what the engine offers.
        const empty = { current: 0, max: 1050, fromHardpoints: true };

        assert.equal(unitDestroyed(HARDPOINTS, new Set(['A', 'B']), empty), false);
        assert.equal(unitDestroyed(HARDPOINTS, new Set(['A', 'B', 'C']), empty), true);
    });
});

describe('unitTargetable', () => {
    it('says a unit WITH hardpoints cannot be shot at directly', () => {
        // Only its hardpoints can be. The attacker panel's "Hull" choice is meaningless there.
        assert.equal(unitTargetable(HARDPOINTS), false);
    });

    it('says a unit without them can', () => {
        assert.equal(unitTargetable([]), true);
    });

    it('says a unit whose only hardpoints are indestructible can', () => {
        // Nothing there can ever be destroyed, so shooting the hardpoints could never kill it.
        assert.equal(unitTargetable([hardpoint({ isDestroyable: false })]), true);
    });
});

describe('shieldGeneratorsDown', () => {
    const generator = (over: Partial<PreviewHardpoint> = {}) =>
        hardpoint({ id: 'HP_Shield_01', type: 'HARD_POINT_SHIELD_GENERATOR', ...over });

    /**
     * The user's rule: *"if a ship has all hardpoints of type shield generator destroyed the shields
     * have to drop to zero, no matter the current state - similar to how the engines turn off."*
     *
     * Measured: 23 shield-generator hardpoints ship across both trees and every one of them is
     * `Is_Destroyable` YES, so this is reachable on all of them.
     */
    it('is down once every generator is destroyed', () => {
        const hardpoints = [generator({ id: 'A' }), generator({ id: 'B' })];

        assert.equal(shieldGeneratorsDown(hardpoints, new Set(['A', 'B'])), true);
    });

    it('holds while one is still standing', () => {
        const hardpoints = [generator({ id: 'A' }), generator({ id: 'B' })];

        assert.equal(shieldGeneratorsDown(hardpoints, new Set(['A'])), false);
    });

    /**
     * A unit whose shield depends on NO hardpoint keeps it. Every fighter is in this position: it
     * declares SHIELDED and carries no hardpoints at all, and reading "none destroyed" as "all
     * destroyed" would strip the shield off every one of them.
     */
    it('is not down for a unit that has no generators to lose', () => {
        assert.equal(shieldGeneratorsDown([], new Set()), false);
        assert.equal(shieldGeneratorsDown([hardpoint({ id: 'W' })], new Set(['W'])), false);
    });

    /** The type is what says so, and the files are not consistent about case. */
    it('reads the type case-insensitively', () => {
        const lower = [generator({ id: 'A', type: 'hard_point_shield_generator' })];

        assert.equal(shieldGeneratorsDown(lower, new Set(['A'])), true);
    });

    /**
     * An INDESTRUCTIBLE generator still projects. A hull carrying one destroyable and one that
     * cannot be shot off never loses its shield, which is the file's own answer rather than a gap.
     */
    it('never drops while an indestructible generator stands', () => {
        const hardpoints = [
            generator({ id: 'A' }),
            generator({ id: 'B', isDestroyable: false }),
        ];

        assert.equal(shieldGeneratorsDown(hardpoints, new Set(['A', 'B'])), false);
    });
});

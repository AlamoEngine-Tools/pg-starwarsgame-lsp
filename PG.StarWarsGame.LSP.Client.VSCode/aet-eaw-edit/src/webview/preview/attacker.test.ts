// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import type { PreviewProjectile, PreviewTargetDefence } from '../../protocol/modelPreview';

import {
    DAMAGE_SWITCHES, DEFAULT_ATTACKER, armorFactor, attackerFromProjectile, fireAtHardpoint,
    attackerProjectile, damageSwitches, fireBlast, poolRows, poolsFor, poolSummary,
    projectileChoices,
    resolveHit, type Attacker,
} from './attacker';
import { blastVictims } from './blast';
import { healthColour } from './reticles';
import { hullPool, unitDestroyed } from './unitPool';

const SHIELDED: PreviewTargetDefence = {
    isShielded: true,
    armorType: 'Armor_Star_Destroyer',
    shieldArmorType: 'Shield_Capital',
    shieldPoints: 2000,
    tacticalHealth: 7500,
    energyCapacity: 8000,
    diesWithHardpoints: true,
    hullVsHardpointsConstraint: 0.2,
    hullFactors: { Damage_Ion: 4, Damage_Anti_Fighter: 0.25 },
    shieldFactors: { Damage_Ion: 0.5 },
    damageTypes: ['Damage_Anti_Fighter', 'Damage_Default', 'Damage_Ion'],
};

const UNSHIELDED: PreviewTargetDefence = { ...SHIELDED, isShielded: false };

/**
 * A weapon with EVERY switch off, so each test names exactly the ones it means.
 *
 * Deliberately not spread from `DEFAULT_ATTACKER`, which sets `hitpoint` - that made every
 * "shield only" case secretly a two-switch one, and the four rows below are the whole point.
 */
function attacker(over: Partial<Attacker> = {}): Attacker {
    return {
        damage: 100,
        damageType: 'Damage_Default',
        shield: false,
        energy: false,
        hitpoint: false,
        // No blast unless a case asks for one, so a test about the switches stays about them.
        blastDamage: 0,
        blastRange: 0,
        blastDropoff: false,
        blastDropoffTiers: 0,
        ...over,
    };
}

/** Everything intact, as a fresh preview opens. */
function pools() {
    return { subject: 'Target', shield: 2000, hull: 7500, energy: 8000 };
}

describe('armorFactor', () => {
    it('reads the factor the table declares', () => {
        assert.equal(armorFactor(SHIELDED.hullFactors, 'Damage_Ion'), 4);
    });

    it('is 1.0 for a pair the table does not name', () => {
        // The shipped table names 2426 of 4293 possible pairs, so this is the COMMON case, not an
        // edge one. Treating a missing pair as zero would make most weapons do nothing.
        assert.equal(armorFactor(SHIELDED.hullFactors, 'Damage_Default'), 1);
        assert.equal(armorFactor({}, 'Damage_Ion'), 1);
    });

    it('matches the damage type however the file cased it', () => {
        assert.equal(armorFactor(SHIELDED.hullFactors, 'damage_ion'), 4);
    });

    it('keeps a declared zero, which is not the same as an absent pair', () => {
        // A modder writing 0 means "this does nothing to that armor" and must not be silently
        // promoted to 1.0 by a falsy check.
        assert.equal(armorFactor({ Damage_Ion: 0 }, 'Damage_Ion'), 0);
    });
});

describe('resolveHit, an unshielded target', () => {
    it('puts hitpoint damage on the hull, scaled by the armor factor', () => {
        const after = resolveHit(
            attacker({ damageType: 'Damage_Ion', hitpoint: true }), UNSHIELDED, pools());

        // 100 x 4 against Armor_Star_Destroyer.
        assert.equal(after.hull, 7500 - 400);
        assert.equal(after.shield, 2000);
    });

    it('puts shield damage NOWHERE - there is no shield to deplete', () => {
        // SHIELDED is what puts a shield in play. An object carrying leftover Shield_Points without
        // the behaviour has none, so a shield-only weapon simply does nothing to it.
        const after = resolveHit(attacker({ shield: true }), UNSHIELDED, pools());

        assert.deepEqual(after, pools());
    });
});

describe('resolveHit, the four rules on a shielded target', () => {
    it('energy only drains the energy pool', () => {
        const after = resolveHit(
            attacker({ energy: true, damageType: 'Damage_Ion' }), SHIELDED, pools());

        assert.equal(after.energy, 8000 - 100);
        assert.equal(after.shield, 2000);
        assert.equal(after.hull, 7500);
    });

    it('shield only depletes the shield and leaves the hull alone', () => {
        const after = resolveHit(
            attacker({ shield: true, damageType: 'Damage_Ion' }), SHIELDED, pools());

        // Scaled by the SHIELD armor's factor, 0.5 - a different column from the hull's.
        assert.equal(after.shield, 2000 - 50);
        assert.equal(after.hull, 7500);
    });

    it('hitpoint only BYPASSES the shield and hits the hull', () => {
        // The rule most likely to be got wrong: a hitpoint-only bolt is not stopped by a full
        // shield, it ignores it.
        const after = resolveHit(
            attacker({ hitpoint: true, damageType: 'Damage_Ion' }), SHIELDED, pools());

        assert.equal(after.shield, 2000);
        assert.equal(after.hull, 7500 - 400);
    });

    it('more than one switch takes the shield first, then the hull', () => {
        const after = resolveHit(
            attacker({ shield: true, hitpoint: true, damage: 10000, damageType: 'Damage_Ion' }),
            SHIELDED, pools());

        // 10000 x 0.5 = 5000 against a 2000 shield: the shield is gone and the surplus carries on
        // to the hull, re-scaled by the HULL's own factor rather than the shield's.
        assert.equal(after.shield, 0);
        assert.ok(after.hull < 7500);
    });

    it('a multi-switch hit the shield absorbs entirely leaves the hull untouched', () => {
        const after = resolveHit(
            attacker({ shield: true, hitpoint: true, damage: 100, damageType: 'Damage_Ion' }),
            SHIELDED, pools());

        assert.equal(after.shield, 2000 - 50);
        assert.equal(after.hull, 7500);
    });

    it('a multi-switch hit on an ALREADY DOWN shield goes straight to the hull', () => {
        const after = resolveHit(
            attacker({ shield: true, hitpoint: true, damageType: 'Damage_Ion' }),
            SHIELDED, { ...pools(), shield: 0 });

        assert.equal(after.hull, 7500 - 400);
    });

    it('drains energy alongside, when that switch is set too', () => {
        const after = resolveHit(
            attacker({ shield: true, energy: true, damageType: 'Damage_Ion' }), SHIELDED, pools());

        assert.equal(after.shield, 2000 - 50);
        assert.equal(after.energy, 8000 - 100);
    });
});

describe('resolveHit, the edges', () => {
    it('never drives a pool below zero', () => {
        const after = resolveHit(
            attacker({ hitpoint: true, damage: 999999 }), SHIELDED, pools());

        assert.equal(after.hull, 0);
    });

    it('does nothing at all when no switch is set', () => {
        // Every switch off is a projectile that damages nothing. Real: a tractor beam bolt.
        assert.deepEqual(resolveHit(attacker(), SHIELDED, pools()), pools());
    });

    it('does nothing with no defence data, rather than throwing', () => {
        // A bare model has no XML behind it, so there is nothing to shoot at.
        assert.deepEqual(
            resolveHit(attacker({ hitpoint: true }), null, pools()), pools());
    });

    it('treats an absent pool as nothing to take away', () => {
        // Most objects declare no Energy_Capacity at all.
        const noEnergy: PreviewTargetDefence = { ...SHIELDED, energyCapacity: null };
        const after = resolveHit(
            attacker({ energy: true }), noEnergy, { subject: 'Target', shield: 2000, hull: 7500, energy: 0 });

        assert.equal(after.energy, 0);
    });
});

describe('attackerFromProjectile', () => {
    const bolt: PreviewProjectile = {
        id: 'Proj_Ship_Large_Turbolaser_Green',
        render: 'Model',
        blastAreaDropoff: false,
        damage: 60,
        damageType: 'Damage_Star_Destroyer',
        doesShieldDamage: true,
        doesEnergyDamage: false,
        doesHitpointDamage: true,
    };

    it('copies the values a projectile declares', () => {
        assert.deepEqual(attackerFromProjectile(bolt, DEFAULT_ATTACKER), {
            damage: 60,
            damageType: 'Damage_Star_Destroyer',
            shield: true,
            energy: false,
            hitpoint: true,
            // Declared by no bolt here, so cleared - a weapon that does not go off.
            blastDamage: 0,
            blastRange: 0,
            blastDropoff: false,
            blastDropoffTiers: 0,
        });
    });

    it('is a COPY, so editing afterwards does not write back', () => {
        // The user's rule: filling from a projectile fills the fields, it does not bind them. A
        // binding would make a later edit look like it had been ignored.
        const filled = attackerFromProjectile(bolt, DEFAULT_ATTACKER);
        filled.damage = 999;

        assert.equal(bolt.damage, 60);
    });

    it('keeps what the reader already had where the projectile says nothing', () => {
        // A bolt that declares no damage number should not blank the field - the reader's own value
        // is better than a zero nobody chose.
        const bare: PreviewProjectile = {
            id: 'P', render: 'Model', blastAreaDropoff: false,
            doesShieldDamage: false, doesEnergyDamage: false, doesHitpointDamage: false,
        };

        const filled = attackerFromProjectile(bare, { ...DEFAULT_ATTACKER, damage: 42 });

        assert.equal(filled.damage, 42);
        assert.equal(filled.damageType, DEFAULT_ATTACKER.damageType);
    });
});

describe('fireAtHardpoint', () => {
    it('takes the hardpoint down by the hull-scaled damage, not the shield-scaled one', () => {
        // MEASURED: a hardpoint declares no Armor_Type and no shield of its own - 0 of them do, and
        // 145 declare only Health. So a hardpoint is hull geometry and takes the hull's factor.
        const after = fireAtHardpoint(
            attacker({ hitpoint: true, damageType: 'Damage_Ion' }), UNSHIELDED, pools(), 800);

        assert.equal(after.hardpointHealth, 800 - 400);
    });

    it('leaves the hull pool alone - the shot went into the hardpoint', () => {
        const after = fireAtHardpoint(
            attacker({ hitpoint: true, damageType: 'Damage_Ion' }), UNSHIELDED, pools(), 800);

        assert.equal(after.pools.hull, 7500);
    });

    it('kills a hardpoint at EXACTLY its health, not a point later', () => {
        const after = fireAtHardpoint(
            attacker({ hitpoint: true, damage: 200, damageType: 'Damage_Ion' }),
            UNSHIELDED, pools(), 800);

        // 200 x 4 = 800 against 800.
        assert.equal(after.hardpointHealth, 0);
        assert.equal(after.destroyed, true);
    });

    it('does not call a hardpoint destroyed while a point is left', () => {
        const after = fireAtHardpoint(
            attacker({ hitpoint: true, damage: 199, damageType: 'Damage_Ion' }),
            UNSHIELDED, pools(), 800);

        assert.equal(after.destroyed, false);
    });

    it('is stopped by the ship shield, because a hardpoint sits behind it', () => {
        const after = fireAtHardpoint(
            attacker({ shield: true, hitpoint: true, damage: 100, damageType: 'Damage_Ion' }),
            SHIELDED, pools(), 800);

        assert.equal(after.pools.shield, 2000 - 50);
        assert.equal(after.hardpointHealth, 800);
    });

    it('is NOT stopped by the shield when the bolt bypasses it', () => {
        // The hitpoint-only rule reaches a hardpoint exactly as it reaches the hull.
        const after = fireAtHardpoint(
            attacker({ hitpoint: true, damageType: 'Damage_Ion' }), SHIELDED, pools(), 800);

        assert.equal(after.pools.shield, 2000);
        assert.equal(after.hardpointHealth, 800 - 400);
    });

    it('reports a hardpoint with no health of its own as indestructible rather than dead at zero', () => {
        // 210 of foc's hardpoints declare no Health. A null must not read as "already at zero".
        const after = fireAtHardpoint(
            attacker({ hitpoint: true, damage: 999 }), UNSHIELDED, pools(), null);

        assert.equal(after.hardpointHealth, null);
        assert.equal(after.destroyed, false);
    });
});

describe('the switch labels', () => {
    it('offers exactly the three the engine reads', () => {
        assert.deepEqual(DAMAGE_SWITCHES.map(s => s.id), ['shield', 'energy', 'hitpoint']);
    });

    it('says what each one MEANS, not what the tag is called', () => {
        // `Projectile_Does_Hitpoint_Damage` tells a reader nothing about the bypass, which is the
        // one rule here that surprises people.
        assert.match(DAMAGE_SWITCHES.find(s => s.id === 'hitpoint')!.title, /bypass/i);
    });
});

describe('poolRows', () => {
    /**
     * A unit with no destructible hardpoints is damaged on its OWN pool, and the row has to show it.
     *
     * `hullPool` reports the declared `Tactical_Health` for such a unit - it has no hardpoints to sum,
     * and a maximum is the only thing it can honestly report. Reading that as the CURRENT value
     * pinned the bar to full: the damage landed in `pools.hull` every time and the row never moved,
     * which is exactly "units without hardpoints don't take any damage".
     */
    it('shows what the hull pool has left', () => {
        // The pool is built from the live number before it gets here, so the row reads it rather
        // than second-guessing it against the raw attacker total.
        const rows = poolRows(
            { ...SHIELDED, tacticalHealth: 1000 },
            { subject: 'Target', shield: 0, hull: 640, energy: 0 },
            { current: 640, max: 1000 });

        assert.match(rows.find(r => r.id === 'hull')!.detail, /^640 of 1000/);
    });

    it('draws ONE health bar for the two pools, as the game does', () => {
        // Briefly this was two rows. The game draws one - the lower of the two - and the pools said
        // separately belong in the damage log, where the reader wants the arithmetic.
        const rows = poolRows(
            { ...SHIELDED, tacticalHealth: 7500 },
            { subject: 'Target', shield: 0, hull: 7500, energy: 0 },
            { current: 7500, max: 7500 },
            undefined,
            { current: 2525, max: 2850 });

        assert.equal(rows.filter(r => r.id === 'hull').length, 1);
        assert.equal(rows.find(r => r.id === 'hardpoints'), undefined);
        assert.match(rows.find(r => r.id === 'hull')!.detail, /lower of hull and hardpoints/);
    });

    it('leaves the hardpoint row out for a unit with none', () => {
        const rows = poolRows(
            { ...SHIELDED, tacticalHealth: 1000 },
            { subject: 'Target', shield: 0, hull: 1000, energy: 0 },
            { current: 1000, max: 1000 });

        assert.equal(rows.find(r => r.id === 'hardpoints'), undefined);
    });

    it('reads out each pool against its capacity', () => {
        // Energy asked for explicitly: it is behind a toggle now and absent unless turned on.
        const rows = poolRows(
            SHIELDED, { subject: 'Target', shield: 1500, hull: 7500, energy: 8000 },
            null, { energy: true });

        assert.deepEqual(rows.map(r => r.id), ['shield', 'hull', 'energy']);
        assert.match(rows[0].detail, /1500 of 2000/);
    });

    it('names the armor each pool is defended by, which is what picks the factor', () => {
        const rows = poolRows(SHIELDED, { subject: 'Target', shield: 2000, hull: 7500, energy: 8000 });

        assert.match(rows[0].detail, /Shield_Capital/);
        assert.match(rows[1].detail, /Armor_Star_Destroyer/);
    });

    it('says a shield is not in play rather than drawing a full one', () => {
        // Shield_Points without SHIELDED is leftover data. Showing "2000 of 2000" would promise a
        // shield that no weapon can ever touch.
        const rows = poolRows(UNSHIELDED, { subject: 'Target', shield: 2000, hull: 7500, energy: 8000 });

        assert.match(rows[0].detail, /not in play/i);
    });

    it('leaves out a pool the target does not declare', () => {
        // Most objects declare no Energy_Capacity, and a row reading "0 of 0" is noise.
        const bare: PreviewTargetDefence = { ...SHIELDED, energyCapacity: null };
        const rows = poolRows(bare, { subject: 'Target', shield: 2000, hull: 7500, energy: 0 });

        assert.deepEqual(rows.map(r => r.id), ['shield', 'hull']);
    });

    it('is empty with nothing to report', () => {
        assert.deepEqual(poolRows(null, null), []);
    });

    it('reads the hull off the HARDPOINTS where the unit has them', () => {
        // A unit with hardpoints cannot be targeted itself, and most mods author its health as the
        // sum of its hardpoints - so the bar has to be that sum, not the Tactical_Health the file
        // happens to carry. On the Star Destroyer those are 4075 and 2000.
        const rows = poolRows(SHIELDED, { subject: 'Target', shield: 2000, hull: 7500, energy: 8000 },
            { current: 3150, max: 4075 });

        assert.match(rows[1].detail, /3150 of 4075/);
    });

    it('says the bar is the lower of the two, and what the stage follows instead', () => {
        // The reader sees a percentage their XML does not contain, so the row has to say which
        // number it is - and that the damage stage follows the HULL rather than this bar.
        const rows = poolRows(
            SHIELDED, { subject: 'Target', shield: 2000, hull: 7500, energy: 8000 },
            { current: 7500, max: 7500 }, undefined, { current: 2035, max: 4075 });

        const health = rows.find(r => r.id === 'hull')!;

        assert.match(health.detail + health.title, /lower of/i);
        assert.match(health.title, /stage/i);
    });

    it('keeps a unit with no hardpoints on its own health', () => {
        const rows = poolRows(SHIELDED, { subject: 'Target', shield: 2000, hull: 7500, energy: 8000 },
            { current: 7500, max: 7500 });

        assert.match(rows[1].detail, /7500 of 7500/);
    });
});

describe('projectileChoices', () => {
    const catalog = [
        'Proj_Ship_Large_Turbolaser_Green', 'Proj_Ship_Ion_Cannon', 'Proj_Inf_Blaster',
    ];

    it('offers EVERY projectile in the tree, not just what the subject fires', () => {
        // The panel builds a weapon to shoot AT the subject, so the subject's own armament is the
        // wrong list entirely - which is what it used to offer.
        assert.equal(projectileChoices(catalog, '').length, 3);
    });

    it('filters on a search, case-insensitively', () => {
        assert.deepEqual(projectileChoices(catalog, 'ion'), ['Proj_Ship_Ion_Cannon']);
        assert.deepEqual(projectileChoices(catalog, 'ION'), ['Proj_Ship_Ion_Cannon']);
    });

    it('matches anywhere in the name, since the prefixes are all the same', () => {
        // Every one starts `Proj_`, so a prefix-only search would be useless.
        assert.equal(projectileChoices(catalog, 'turbolaser').length, 1);
        assert.equal(projectileChoices(catalog, 'ship').length, 2);
    });

    it('ignores surrounding whitespace in the search', () => {
        assert.equal(projectileChoices(catalog, '  ion  ').length, 1);
    });

    it('gives nothing back for a search that matches nothing', () => {
        assert.deepEqual(projectileChoices(catalog, 'zzz'), []);
    });

    it('caps a long list so the dropdown stays usable', () => {
        // 212 in eaw. A select with every one of them is a scroll, not a picker - the search is the
        // way through it, and a cap makes that obvious rather than optional.
        const many = Array.from({ length: 300 }, (_, i) => `Proj_${i}`);

        assert.ok(projectileChoices(many, '').length < 300);
    });
});

describe('fireBlast', () => {
    const hardpoints = { A: 100, B: 100, C: 100 };

    it('damages every victim the blast caught', () => {
        const after = fireBlast(
            { ...DEFAULT_ATTACKER, damage: 0 }, UNSHIELDED,
            { subject: 'Target', shield: 0, hull: 1000, energy: 0 },
            [
                { id: 'A', directDamage: 0, blastDamage: 30, tier: null , wasted: false },
                { id: 'B', directDamage: 0, blastDamage: 30, tier: null , wasted: false },
            ],
            hardpoints);

        assert.equal(after.hardpointHealth.A, 70);
        assert.equal(after.hardpointHealth.B, 70);
        assert.equal(after.hardpointHealth.C, 100);
    });

    it('gives the aimed-at hardpoint its direct damage on top', () => {
        const after = fireBlast(
            DEFAULT_ATTACKER, UNSHIELDED, { subject: 'Target', shield: 0, hull: 1000, energy: 0 },
            [
                { id: 'A', directDamage: 40, blastDamage: 30, tier: null , wasted: false },
                { id: 'B', directDamage: 0, blastDamage: 30, tier: null , wasted: false },
            ],
            hardpoints);

        assert.equal(after.hardpointHealth.A, 30);
        assert.equal(after.hardpointHealth.B, 70);
    });

    it('names every hardpoint the shot destroyed', () => {
        const after = fireBlast(
            DEFAULT_ATTACKER, UNSHIELDED, { subject: 'Target', shield: 0, hull: 1000, energy: 0 },
            [
                { id: 'A', directDamage: 200, blastDamage: 0, tier: null , wasted: false },
                { id: 'B', directDamage: 0, blastDamage: 500, tier: null , wasted: false },
            ],
            hardpoints);

        assert.deepEqual([...after.destroyed].sort(), ['A', 'B']);
    });

    it('loses the surplus rather than carrying it to the next hardpoint', () => {
        // The user's rule, and it is the whole difference between a blast and a chain: a projectile
        // with no AOE applies its damage to ONE target, and if that is more than the target has
        // left, the target simply disappears.
        const after = fireBlast(
            { ...DEFAULT_ATTACKER, damage: 5000 }, UNSHIELDED,
            { subject: 'Target', shield: 0, hull: 1000, energy: 0 },
            [{ id: 'A', directDamage: 5000, blastDamage: 0, tier: null , wasted: false }],
            hardpoints);

        assert.equal(after.hardpointHealth.A, 0);
        assert.equal(after.hardpointHealth.B, 100);
    });

    it('drains the shared shield ONCE per victim, in order', () => {
        // One shot, but each victim is resolved against the pools as they stand - so a blast that
        // catches three hardpoints through a thin shield gets through on the later ones.
        const after = fireBlast(
            { ...DEFAULT_ATTACKER, shield: true, hitpoint: true, damage: 0 }, SHIELDED,
            { subject: 'Target', shield: 50, hull: 1000, energy: 0 },
            [
                { id: 'A', directDamage: 0, blastDamage: 40, tier: null , wasted: false },
                { id: 'B', directDamage: 0, blastDamage: 40, tier: null , wasted: false },
            ],
            hardpoints);

        assert.equal(after.pools.shield, 0);
        // A was fully absorbed; B met a shield with 10 left and the other 30 reached the hardpoint.
        assert.equal(after.hardpointHealth.A, 100);
        assert.equal(after.hardpointHealth.B, 70);
    });

    it('leaves a hardpoint with no declared health alone', () => {
        const after = fireBlast(
            DEFAULT_ATTACKER, UNSHIELDED, { subject: 'Target', shield: 0, hull: 1000, energy: 0 },
            [{ id: 'N', directDamage: 999, blastDamage: 0, tier: null , wasted: false }],
            { N: null });

        assert.equal(after.hardpointHealth.N, null);
        assert.equal(after.destroyed.size, 0);
    });
});

describe('poolsFor', () => {
    /**
     * Reported: "loading a unit without hardpoints immediately plays the death explosion on load."
     *
     * Measured with `probe-death-on-load.js`: whichever hardpoint-less unit is opened FIRST in a
     * fresh panel sets off its own `Death_Explosions`, and the next one does not. It is the load
     * ORDER, not the subject - a TIE Fighter and an X-Wing each blast when they go first and are
     * quiet when they go second.
     *
     * The pools are reset by an effect keyed on the scene, and that effect also runs at MOUNT, when
     * there is no scene yet - leaving `hull: 0`. The first real scene then renders for one commit
     * against that zero, `unitDestroyed` reads an empty hull pool with a positive maximum, and the
     * death watch fires. The next commit refills the pool and quietly undoes it, which is why the
     * panel looks healthy afterwards and only the explosion gives it away.
     *
     * So the pools have to say WHICH subject they were measured on. A number that describes another
     * scene is not a reading of this one.
     */
    it('returns the pools when they were measured on the scene being drawn', () => {
        const pools = { subject: 'TIE_Fighter', shield: 0, hull: 30, energy: 500 };

        assert.deepEqual(poolsFor({ subject: 'TIE_Fighter' }, pools), pools);
    });

    it('returns null for the zeroes left behind before any scene arrived', () => {
        // The mount-time run of the reset effect: no scene, so every pool is 0.
        const mounted = { subject: '', shield: 0, hull: 0, energy: 0 };

        assert.equal(poolsFor({ subject: 'TIE_Fighter' }, mounted), null);
    });

    it('returns null while the pools still describe the previous subject', () => {
        const previous = { subject: 'X-Wing', shield: 0, hull: 0, energy: 0 };

        assert.equal(poolsFor({ subject: 'TIE_Fighter' }, previous), null);
    });

    it('returns null when there is no scene and when there are no pools', () => {
        assert.equal(poolsFor(null, { subject: 'X-Wing', shield: 1, hull: 1, energy: 1 }), null);
        assert.equal(poolsFor({ subject: 'X-Wing' }, null), null);
    });

    /**
     * The whole point, end to end: with the guard in place the first scene reads its own declared
     * health rather than the mount's zero, so nothing is dead and nothing explodes.
     */
    it('leaves a freshly opened unit at full health rather than dead', () => {
        const mounted = { subject: '', shield: 0, hull: 0, energy: 0 };
        const live = poolsFor({ subject: 'TIE_Fighter' }, mounted);
        const hull = hullPool({ ...UNSHIELDED, tacticalHealth: 50 }, live?.hull);

        assert.deepEqual(hull, { current: 50, max: 50 });
        assert.equal(unitDestroyed([], new Set(), hull), false);
    });
});

describe('poolRows, as bars', () => {
    const pools = { subject: 'Target', shield: 1000, hull: 3750, energy: 2000 };

    /**
     * The rows drive the status bars under the ability command bar, so each carries a fraction and
     * a colour as well as its words. One model, two places: the flyout's readout and the bars are
     * the same three pools, and a second copy would drift the first time one changed.
     */
    it('carries the fraction each pool is at', () => {
        const rows = poolRows(SHIELDED, pools, null, { energy: true });

        assert.deepEqual(
            rows.map(r => [r.id, r.fraction]),
            [['shield', 0.5], ['hull', 0.5], ['energy', 0.25]]);
    });

    /**
     * Colours the user set: shields blue, hull on the hardpoints' own ramp so a bar and a targeting
     * mark say the same thing, and energy something neither of them is.
     */
    it('gives each pool its own colour, and the hull the hardpoint ramp', () => {
        const full = poolRows(SHIELDED, { ...pools, hull: 7500 }, null, { energy: true });
        const hurt = poolRows(SHIELDED, { ...pools, hull: 750 }, null, { energy: true });

        assert.equal(full.find(r => r.id === 'hull')!.colour, healthColour(1));
        assert.equal(hurt.find(r => r.id === 'hull')!.colour, healthColour(0.1));
        assert.notEqual(full.find(r => r.id === 'shield')!.colour,
            full.find(r => r.id === 'energy')!.colour);
    });

    it('clamps a fraction rather than drawing a bar past its own end', () => {
        const over = poolRows(SHIELDED, { ...pools, shield: 99999 }, null, { energy: true });

        assert.equal(over.find(r => r.id === 'shield')!.fraction, 1);
    });

    /**
     * Energy is behind a feature toggle, off by default. The user: the mechanic is in the engine and
     * works, but it is disabled and has no UI, so a mod maker turns it on deliberately after reading
     * what it means. Every energy element goes with it, this row included.
     */
    /**
     * Shot-off generators empty the bar, whatever the pool holds.
     *
     * The user's rule, and it is a SWITCH rather than a drain - so the row has to read 0 even when
     * nothing has touched the shield, and say why rather than leaving a reader to wonder which shot
     * did it.
     */
    it('reads the shield as empty once its generators are gone', () => {
        const rows = poolRows(SHIELDED, pools, null, { energy: false, shieldsDown: true });
        const shield = rows.find(r => r.id === 'shield')!;

        assert.equal(shield.fraction, 0);
        assert.match(shield.detail, /generator/i);
        assert.doesNotMatch(shield.detail, /^1000 of/);
    });

    it('leaves the shield alone while a generator stands', () => {
        const rows = poolRows(SHIELDED, pools, null, { energy: false, shieldsDown: false });

        assert.equal(rows.find(r => r.id === 'shield')!.fraction, 0.5);
    });

    it('leaves energy out entirely unless it has been turned on', () => {
        const off = poolRows(SHIELDED, pools, null, { energy: false });

        assert.deepEqual(off.map(r => r.id), ['shield', 'hull']);
    });

    it('defaults to leaving energy out when nobody says', () => {
        assert.equal(poolRows(SHIELDED, pools).some(r => r.id === 'energy'), false);
    });
});

describe('DAMAGE_SWITCHES, gated', () => {
    it('offers the energy switch only when energy is on', () => {
        assert.deepEqual(damageSwitches({ energy: false }).map(s => s.id), ['shield', 'hitpoint']);
        assert.deepEqual(
            damageSwitches({ energy: true }).map(s => s.id), ['shield', 'energy', 'hitpoint']);
    });
});

describe('the attacker carries its own blast', () => {
    const projectile = (over: Partial<PreviewProjectile> = {}): PreviewProjectile => ({
        id: 'Proj_Test', render: 'Model', blastAreaDropoff: false,
        damage: 60, damageType: 'Damage_Default',
        doesShieldDamage: false, doesEnergyDamage: false, doesHitpointDamage: true,
        ...over,
    });

    /**
     * Reported: *"We are missing AOE damage settings currently. Those need to be added, so we can
     * start simulating that."*
     *
     * Blast used to arrive ONLY with a picked projectile - `fire` branched on whether one was
     * chosen, and the no-projectile path built a single hit with `blastDamage: 0`. So a reader
     * could type a damage number and a damage type but had no way to say "and it goes off".
     */
    it('defaults to no blast, which is what a plain bolt is', () => {
        assert.equal(DEFAULT_ATTACKER.blastDamage, 0);
        assert.equal(DEFAULT_ATTACKER.blastRange, 0);
        assert.equal(DEFAULT_ATTACKER.blastDropoff, false);
    });

    /**
     * ONE path through `blastVictims` whether or not a projectile was picked. The two used to be
     * separate branches, which is exactly how they came to disagree about whether a blast happened.
     */
    it('builds a projectile out of what the reader typed', () => {
        const spec = attackerProjectile(attacker({
            damage: 100, blastDamage: 40, blastRange: 300, blastDropoff: true,
            blastDropoffTiers: 3,
        }));

        assert.equal(spec.damage, 100);
        assert.equal(spec.blastAreaDamage, 40);
        assert.equal(spec.blastAreaRange, 300);
        assert.equal(spec.blastAreaDropoff, true);
        assert.equal(spec.blastAreaDropoffTiers, 3);
    });

    it('reaches a neighbour inside the radius and not one outside it', () => {
        const spec = attackerProjectile(attacker({
            damage: 100, blastDamage: 40, blastRange: 300,
        }));

        const hits = fireBlast(
            attacker({ hitpoint: true }), UNSHIELDED,
            { subject: 'Target', shield: 0, hull: 5000, energy: 0 },
            blastVictims(spec, 'A', [
                { id: 'A', distance: 0 },
                { id: 'B', distance: 120 },
                { id: 'C', distance: 900 },
            ]),
            { A: 400, B: 400, C: 400 });

        assert.equal(hits.hardpointHealth.C, 400);
        assert.ok(hits.hardpointHealth.B! < 400);
    });

    /** Picking a projectile fills the blast fields too, or the picker would half-fill the form. */
    it('fills the blast from a projectile the reader picks', () => {
        const filled = attackerFromProjectile(
            projectile({
                blastAreaDamage: 55, blastAreaRange: 250,
                blastAreaDropoff: true, blastAreaDropoffTiers: 4,
            }),
            DEFAULT_ATTACKER);

        assert.equal(filled.blastDamage, 55);
        assert.equal(filled.blastRange, 250);
        assert.equal(filled.blastDropoff, true);
        assert.equal(filled.blastDropoffTiers, 4);
    });

    it('a projectile that declares no blast clears the fields rather than leaving a stale one', () => {
        // The opposite of the damage fields, and deliberately: a bolt with no damage number keeps
        // whatever was typed, but a bolt with NO BLAST is a bolt that does not go off, and leaving
        // the last one's radius behind would simulate a weapon nobody described.
        const filled = attackerFromProjectile(
            projectile(),
            { ...DEFAULT_ATTACKER, blastDamage: 99, blastRange: 999, blastDropoff: true });

        assert.equal(filled.blastDamage, 0);
        assert.equal(filled.blastRange, 0);
        assert.equal(filled.blastDropoff, false);
    });
});

describe('firing at a hardpoint that is already destroyed', () => {
    // The discard case, measured. The engine finds the hardpoint by collision mesh, sees it at zero,
    // and clears the flag that would otherwise pass the shot to the hull - so neither pool takes it.
    // A ship whose surfaces are covered by dead hardpoints stops taking damage from those angles.
    const HITPOINT: Attacker = {
        ...DEFAULT_ATTACKER, damage: 500, shield: false, energy: false, hitpoint: true,
    };

    it('leaves the hardpoint at zero rather than driving it negative', () => {
        const after = fireAtHardpoint(
            HITPOINT, UNSHIELDED, { subject: 'T', shield: 0, hull: 2000, energy: 0 }, 0);

        assert.equal(after.hardpointHealth, 0);
    });

    it('does not pass the shot to the hull', () => {
        const after = fireAtHardpoint(
            HITPOINT, UNSHIELDED, { subject: 'T', shield: 0, hull: 2000, energy: 0 }, 0);

        assert.equal(after.pools.hull, 2000);
    });
});

describe('overkill on a hardpoint', () => {
    it('evaporates rather than carrying anywhere', () => {
        // 500 into a hardpoint holding 100 destroys it and loses the other 400 - the surplus reaches
        // neither the hull nor a neighbour.
        const after = fireAtHardpoint(
            { ...DEFAULT_ATTACKER, damage: 500, shield: false, energy: false, hitpoint: true },
            UNSHIELDED, { subject: 'T', shield: 0, hull: 2000, energy: 0 }, 100);

        assert.equal(after.hardpointHealth, 0);
        assert.equal(after.destroyed, true);
        assert.equal(after.pools.hull, 2000);
    });
});

describe('the main display draws one health bar, as the game does', () => {
    it('shows the LOWER of the two pools', () => {
        // Get_Display_Health_Percent. The hull is at 90% and the hardpoints at 25%, so the bar
        // reads 25% - and the reader is not shown two bars for one unit.
        const rows = poolRows(
            { ...SHIELDED, tacticalHealth: 1000 },
            { subject: 'Target', shield: 0, hull: 900, energy: 0 },
            { current: 900, max: 1000 },
            undefined,
            { current: 250, max: 1000 });

        assert.equal(rows.filter(r => r.id === 'hull').length, 1);
        assert.equal(rows.find(r => r.id === 'hardpoints'), undefined);
        assert.equal(rows.find(r => r.id === 'hull')!.fraction, 0.25);
    });

    it('is the hull alone where the unit does not die with its hardpoints', () => {
        const rows = poolRows(
            { ...SHIELDED, tacticalHealth: 1000, diesWithHardpoints: false },
            { subject: 'Target', shield: 0, hull: 900, energy: 0 },
            { current: 900, max: 1000 },
            undefined,
            { current: 250, max: 1000 });

        assert.equal(rows.find(r => r.id === 'hull')!.fraction, 0.9);
    });
});

describe('poolSummary', () => {
    it('says only the numbers that move', () => {
        const summary = poolSummary(
            { ...SHIELDED, tacticalHealth: 1000 },
            { current: 900, max: 1000 },
            { current: 250, max: 1000 })!;

        assert.match(summary.text, /Hull 900 of 1000/);
        assert.match(summary.text, /Hardpoints 250 of 1000/);
    });

    // Four rows of constant text pushed the log itself off the panel. None of this moves while a
    // reader fires, so it costs no height.
    it('keeps the fixed facts on the tooltip', () => {
        const summary = poolSummary(
            { ...SHIELDED, tacticalHealth: 1000 },
            { current: 900, max: 1000 },
            { current: 250, max: 1000 })!;

        assert.match(summary.title, /Tactical_Health/);
        assert.match(summary.title, /Hull_Vs_Hard_Points_Health_Constraint/);
        assert.doesNotMatch(summary.text, /Hull_Vs_Hard_Points_Health_Constraint/);
    });

    it('names the workspace constraint rather than assuming 0.2', () => {
        const summary = poolSummary(
            { ...SHIELDED, tacticalHealth: 1000, hullVsHardpointsConstraint: 1 },
            { current: 900, max: 1000 },
            { current: 250, max: 1000 })!;

        assert.match(summary.title, /Leash: 1,/);
    });

    it('says so where the unit does not die with its hardpoints', () => {
        const summary = poolSummary(
            { ...SHIELDED, tacticalHealth: 1000, diesWithHardpoints: false },
            { current: 900, max: 1000 },
            { current: 250, max: 1000 })!;

        assert.match(summary.title, /Dies with hardpoints: no/);
    });

    it('leaves the hardpoints out for a unit with none', () => {
        const summary = poolSummary(
            { ...SHIELDED, tacticalHealth: 1000 }, { current: 900, max: 1000 })!;

        assert.match(summary.text, /^Hull 900 of 1000$/);
    });

    it('is nothing at all for a subject with no health to report', () => {
        assert.equal(poolSummary({ ...SHIELDED, tacticalHealth: 0 }, { current: 0, max: 0 }), null);
    });
});

describe('the arithmetic the log shows', () => {
    const ION: Attacker = {
        ...DEFAULT_ATTACKER, damage: 100, damageType: 'Damage_Ion',
        shield: false, energy: false, hitpoint: true,
    };

    const NO_POOLS = { subject: 'T', shield: 0, hull: 5000, energy: 0 };

    it('names the factor, the damage type and the armour it was read against', () => {
        // SHIELDED gives Damage_Ion a hull factor of 4 against Armor_Star_Destroyer, and a reader
        // who does not know why 100 became 400 is exactly who the log is for.
        const { workings } = fireAtHardpoint(ION, SHIELDED, NO_POOLS, 1000);

        assert.match(
            workings.join(' | '), /Armor factor: 100 x 4 = 400 - Damage_Ion vs Armor_Star_Destroyer/);
    });

    it('shows the pool before and after', () => {
        const { workings } = fireAtHardpoint(ION, SHIELDED, NO_POOLS, 1000);

        assert.match(workings.join(' | '), /Health 1000 -> 600/);
    });

    it('says how much overkill was lost', () => {
        // 400 into a hardpoint holding 100: the other 300 reaches neither pool.
        const { workings } = fireAtHardpoint(ION, SHIELDED, NO_POOLS, 100);

        assert.match(workings.join(' | '), /overkill lost/);
    });

    it('explains a shot thrown away on wreckage', () => {
        const { workings } = fireAtHardpoint(ION, SHIELDED, NO_POOLS, 0);

        assert.match(workings.join(' | '), /already destroyed/);
    });

    it('shows how a blast was divided, and what evaporated', () => {
        const hits = [
            { id: 'A', directDamage: 0, blastDamage: 100, tier: null, wasted: false },
            { id: 'B', directDamage: 0, blastDamage: 100, tier: null, wasted: false },
            { id: 'C', directDamage: 0, blastDamage: 100, tier: null, wasted: true },
        ];
        const result = fireBlast(ION, SHIELDED, NO_POOLS, hits, { A: 500, B: 500, C: 0 });

        assert.match(result.workings.join(' | '), /Blast 300 split across 3 in range - 100 each/);
        assert.match(result.workings.join(' | '), /Wreckage in range: 1 of 3 - 100 lost/);
        assert.match((result.perVictim.C ?? []).join(' | '), /Discarded 100 - already destroyed/);
    });

    it('shows the shield taking its own factor, and the spare re-scaled behind it', () => {
        const through: Attacker = { ...ION, shield: true };
        const { workings } = fireAtHardpoint(
            through, SHIELDED, { subject: 'T', shield: 10, hull: 5000, energy: 0 }, 1000);

        const text = workings.join(' | ');

        assert.match(text, /Shield factor: /);
        assert.match(text, /Shield 10 -> 0/);
        assert.match(text, /Spare through the shield: /);
    });
});

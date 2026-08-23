// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import type { PreviewProjectile, PreviewTargetDefence } from '../../protocol/modelPreview';

import {
    DAMAGE_SWITCHES, DEFAULT_ATTACKER, armorFactor, attackerFromProjectile, fireAtMount,
    fireBlast, poolRows, projectileChoices, resolveHit, type Attacker,
} from './attacker';

const SHIELDED: PreviewTargetDefence = {
    isShielded: true,
    armorType: 'Armor_Star_Destroyer',
    shieldArmorType: 'Shield_Capital',
    shieldPoints: 2000,
    tacticalHealth: 7500,
    energyCapacity: 8000,
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
        ...over,
    };
}

/** Everything intact, as a fresh preview opens. */
function pools() {
    return { shield: 2000, hull: 7500, energy: 8000 };
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
            attacker({ energy: true }), noEnergy, { shield: 2000, hull: 7500, energy: 0 });

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

describe('fireAtMount', () => {
    it('takes the mount down by the hull-scaled damage, not the shield-scaled one', () => {
        // MEASURED: a hardpoint declares no Armor_Type and no shield of its own - 0 of them do, and
        // 145 declare only Health. So a mount is hull geometry and takes the hull's factor.
        const after = fireAtMount(
            attacker({ hitpoint: true, damageType: 'Damage_Ion' }), UNSHIELDED, pools(), 800);

        assert.equal(after.mountHealth, 800 - 400);
    });

    it('leaves the hull pool alone - the shot went into the mount', () => {
        const after = fireAtMount(
            attacker({ hitpoint: true, damageType: 'Damage_Ion' }), UNSHIELDED, pools(), 800);

        assert.equal(after.pools.hull, 7500);
    });

    it('kills a mount at EXACTLY its health, not a point later', () => {
        const after = fireAtMount(
            attacker({ hitpoint: true, damage: 200, damageType: 'Damage_Ion' }),
            UNSHIELDED, pools(), 800);

        // 200 x 4 = 800 against 800.
        assert.equal(after.mountHealth, 0);
        assert.equal(after.destroyed, true);
    });

    it('does not call a mount destroyed while a point is left', () => {
        const after = fireAtMount(
            attacker({ hitpoint: true, damage: 199, damageType: 'Damage_Ion' }),
            UNSHIELDED, pools(), 800);

        assert.equal(after.destroyed, false);
    });

    it('is stopped by the ship shield, because a mount sits behind it', () => {
        const after = fireAtMount(
            attacker({ shield: true, hitpoint: true, damage: 100, damageType: 'Damage_Ion' }),
            SHIELDED, pools(), 800);

        assert.equal(after.pools.shield, 2000 - 50);
        assert.equal(after.mountHealth, 800);
    });

    it('is NOT stopped by the shield when the bolt bypasses it', () => {
        // The hitpoint-only rule reaches a mount exactly as it reaches the hull.
        const after = fireAtMount(
            attacker({ hitpoint: true, damageType: 'Damage_Ion' }), SHIELDED, pools(), 800);

        assert.equal(after.pools.shield, 2000);
        assert.equal(after.mountHealth, 800 - 400);
    });

    it('reports a mount with no health of its own as indestructible rather than dead at zero', () => {
        // 210 of foc's hardpoints declare no Health. A null must not read as "already at zero".
        const after = fireAtMount(
            attacker({ hitpoint: true, damage: 999 }), UNSHIELDED, pools(), null);

        assert.equal(after.mountHealth, null);
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
     * A unit with no destructible mounts is damaged on its OWN pool, and the row has to show it.
     *
     * `hullPool` reports the declared `Tactical_Health` for such a unit - it has no mounts to sum,
     * and a maximum is the only thing it can honestly report. Reading that as the CURRENT value
     * pinned the bar to full: the damage landed in `pools.hull` every time and the row never moved,
     * which is exactly "units without hardpoints don't take any damage".
     */
    it('shows the live pool for a unit whose hull is not summed from mounts', () => {
        const rows = poolRows(
            { ...SHIELDED, tacticalHealth: 1000 },
            { shield: 0, hull: 640, energy: 0 },
            { current: 1000, max: 1000, fromHardpoints: false });

        assert.match(rows.find(r => r.id === 'hull')!.detail, /^640 of 1000/);
    });

    it('still shows the summed mounts when the hull IS the mounts', () => {
        const rows = poolRows(
            { ...SHIELDED, tacticalHealth: 7500 },
            { shield: 0, hull: 7500, energy: 0 },
            { current: 2525, max: 2850, fromHardpoints: true });

        assert.match(rows.find(r => r.id === 'hull')!.detail, /^2525 of 2850 - summed from hardpoints/);
    });

    it('reads out each pool against its capacity', () => {
        const rows = poolRows(SHIELDED, { shield: 1500, hull: 7500, energy: 8000 });

        assert.deepEqual(rows.map(r => r.id), ['shield', 'hull', 'energy']);
        assert.match(rows[0].detail, /1500 of 2000/);
    });

    it('names the armor each pool is defended by, which is what picks the factor', () => {
        const rows = poolRows(SHIELDED, { shield: 2000, hull: 7500, energy: 8000 });

        assert.match(rows[0].detail, /Shield_Capital/);
        assert.match(rows[1].detail, /Armor_Star_Destroyer/);
    });

    it('says a shield is not in play rather than drawing a full one', () => {
        // Shield_Points without SHIELDED is leftover data. Showing "2000 of 2000" would promise a
        // shield that no weapon can ever touch.
        const rows = poolRows(UNSHIELDED, { shield: 2000, hull: 7500, energy: 8000 });

        assert.match(rows[0].detail, /not in play/i);
    });

    it('leaves out a pool the target does not declare', () => {
        // Most objects declare no Energy_Capacity, and a row reading "0 of 0" is noise.
        const bare: PreviewTargetDefence = { ...SHIELDED, energyCapacity: null };
        const rows = poolRows(bare, { shield: 2000, hull: 7500, energy: 0 });

        assert.deepEqual(rows.map(r => r.id), ['shield', 'hull']);
    });

    it('is empty with nothing to report', () => {
        assert.deepEqual(poolRows(null, null), []);
    });

    it('reads the hull off the MOUNTS where the unit has them', () => {
        // A unit with hardpoints cannot be targeted itself, and most mods author its health as the
        // sum of its mounts - so the bar has to be that sum, not the Tactical_Health the file
        // happens to carry. On the Star Destroyer those are 4075 and 2000.
        const rows = poolRows(SHIELDED, { shield: 2000, hull: 7500, energy: 8000 },
            { current: 3150, max: 4075, fromHardpoints: true });

        assert.match(rows[1].detail, /3150 of 4075/);
    });

    it('says WHERE that hull number came from', () => {
        // The reader will see a number their XML does not contain. They have to be told which one
        // it is, because nobody knows how the engine reconciles the two.
        const rows = poolRows(SHIELDED, { shield: 2000, hull: 7500, energy: 8000 },
            { current: 4075, max: 4075, fromHardpoints: true });

        assert.match(rows[1].detail + rows[1].title, /hardpoint/i);
    });

    it('keeps a unit with no mounts on its own health', () => {
        const rows = poolRows(SHIELDED, { shield: 2000, hull: 7500, energy: 8000 },
            { current: 7500, max: 7500, fromHardpoints: false });

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
    const mounts = { A: 100, B: 100, C: 100 };

    it('damages every victim the blast caught', () => {
        const after = fireBlast(
            { ...DEFAULT_ATTACKER, damage: 0 }, UNSHIELDED,
            { shield: 0, hull: 1000, energy: 0 },
            [
                { id: 'A', directDamage: 0, blastDamage: 30, tier: null },
                { id: 'B', directDamage: 0, blastDamage: 30, tier: null },
            ],
            mounts);

        assert.equal(after.mountHealth.A, 70);
        assert.equal(after.mountHealth.B, 70);
        assert.equal(after.mountHealth.C, 100);
    });

    it('gives the aimed-at mount its direct damage on top', () => {
        const after = fireBlast(
            DEFAULT_ATTACKER, UNSHIELDED, { shield: 0, hull: 1000, energy: 0 },
            [
                { id: 'A', directDamage: 40, blastDamage: 30, tier: null },
                { id: 'B', directDamage: 0, blastDamage: 30, tier: null },
            ],
            mounts);

        assert.equal(after.mountHealth.A, 30);
        assert.equal(after.mountHealth.B, 70);
    });

    it('names every mount the shot destroyed', () => {
        const after = fireBlast(
            DEFAULT_ATTACKER, UNSHIELDED, { shield: 0, hull: 1000, energy: 0 },
            [
                { id: 'A', directDamage: 200, blastDamage: 0, tier: null },
                { id: 'B', directDamage: 0, blastDamage: 500, tier: null },
            ],
            mounts);

        assert.deepEqual([...after.destroyed].sort(), ['A', 'B']);
    });

    it('loses the surplus rather than carrying it to the next mount', () => {
        // The user's rule, and it is the whole difference between a blast and a chain: a projectile
        // with no AOE applies its damage to ONE target, and if that is more than the target has
        // left, the target simply disappears.
        const after = fireBlast(
            { ...DEFAULT_ATTACKER, damage: 5000 }, UNSHIELDED,
            { shield: 0, hull: 1000, energy: 0 },
            [{ id: 'A', directDamage: 5000, blastDamage: 0, tier: null }],
            mounts);

        assert.equal(after.mountHealth.A, 0);
        assert.equal(after.mountHealth.B, 100);
    });

    it('drains the shared shield ONCE per victim, in order', () => {
        // One shot, but each victim is resolved against the pools as they stand - so a blast that
        // catches three mounts through a thin shield gets through on the later ones.
        const after = fireBlast(
            { ...DEFAULT_ATTACKER, shield: true, hitpoint: true, damage: 0 }, SHIELDED,
            { shield: 50, hull: 1000, energy: 0 },
            [
                { id: 'A', directDamage: 0, blastDamage: 40, tier: null },
                { id: 'B', directDamage: 0, blastDamage: 40, tier: null },
            ],
            mounts);

        assert.equal(after.pools.shield, 0);
        // A was fully absorbed; B met a shield with 10 left and the other 30 reached the mount.
        assert.equal(after.mountHealth.A, 100);
        assert.equal(after.mountHealth.B, 70);
    });

    it('leaves a mount with no declared health alone', () => {
        const after = fireBlast(
            DEFAULT_ATTACKER, UNSHIELDED, { shield: 0, hull: 1000, energy: 0 },
            [{ id: 'N', directDamage: 999, blastDamage: 0, tier: null }],
            { N: null });

        assert.equal(after.mountHealth.N, null);
        assert.equal(after.destroyed.size, 0);
    });
});

// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import type { PreviewProjectile } from '../../protocol/modelPreview';

import { blastShare, blastVictims, candidatesFrom } from './blast';

function projectile(over: Partial<PreviewProjectile> = {}): PreviewProjectile {
    return {
        id: 'Proj_Test',
        render: 'Model',
        modelFile: null,
        width: null,
        length: null,
        textureSlot: null,
        laserColor: null,
        speed: null,
        maxFlightDistance: null,
        maxRateOfTurn: null,
        damage: 50,
        damageType: 'Damage_Default',
        category: null,
        doesShieldDamage: false,
        doesEnergyDamage: false,
        doesHitpointDamage: true,
        blastAreaDamage: null,
        blastAreaRange: null,
        blastAreaMaxVictims: null,
        blastAreaDropoff: false,
        blastAreaDropoffTiers: null,
        detonationParticles: null,
        shieldAbsorbParticles: null,
        detonateSfxEvent: null,
        ...over,
    };
}

const HARDPOINTS = [
    { id: 'target', distance: 0 },
    { id: 'near', distance: 20 },
    { id: 'mid', distance: 60 },
    { id: 'far', distance: 500 },
];

describe('blastVictims', () => {
    it('hits ONLY the target when the projectile has no blast area', () => {
        // The overwhelming majority - 63 of foc's projectiles declare one, out of 173.
        const hits = blastVictims(projectile(), 'target', HARDPOINTS);

        assert.deepEqual(hits.map(h => h.id), ['target']);
        assert.equal(hits[0].blastDamage, 0);
    });

    it('hits every hardpoint inside the range', () => {
        const hits = blastVictims(
            projectile({ blastAreaDamage: 30, blastAreaRange: 100 }), 'target', HARDPOINTS);

        assert.deepEqual(hits.map(h => h.id), ['target', 'near', 'mid']);
    });

    it('splits a flat blast equally among everything inside it', () => {
        // 53 of the 63 declare no dropoff at all, so flat is the normal case. It used to apply the
        // FULL blast to each victim, which made a blast stronger against a ship with more
        // hardpoints; the engine divides it instead, so the total delivered is constant.
        const hits = blastVictims(
            projectile({ blastAreaDamage: 30, blastAreaRange: 100 }), 'target', HARDPOINTS);

        assert.deepEqual(hits.map(h => h.blastDamage), [10, 10, 10]);
    });

    it('gives the target its direct damage AND the blast', () => {
        // Both, and the data forces it: Proj_Veers_AT_AT_Max_Power_Laser_Red declares
        // Projectile_Damage 0.0 with a blast of 40, so a target taking only the direct number
        // would take nothing at all from a weapon that is plainly meant to hurt.
        const hits = blastVictims(
            projectile({ damage: 50, blastAreaDamage: 30, blastAreaRange: 100 }), 'target', HARDPOINTS);

        assert.equal(hits[0].directDamage, 50);
        // Its SHARE of the blast - three victims, so a third each.
        assert.equal(hits[0].blastDamage, 10);
        assert.equal(hits[1].directDamage, 0);
    });

    it('caps the victims where the projectile says so, nearest first', () => {
        const hits = blastVictims(
            projectile({ blastAreaDamage: 30, blastAreaRange: 100, blastAreaMaxVictims: 2 }),
            'target', HARDPOINTS);

        assert.deepEqual(hits.map(h => h.id), ['target', 'near']);
    });

    it('always includes the target, even past its own blast range', () => {
        // A direct hit is a direct hit. The range governs who ELSE is caught.
        const hits = blastVictims(
            projectile({ blastAreaDamage: 30, blastAreaRange: 5 }), 'mid', [
                { id: 'mid', distance: 0 }, { id: 'far', distance: 500 },
            ]);

        assert.deepEqual(hits.map(h => h.id), ['mid']);
    });

    it('reports the tier each victim fell in, without varying their damage by it', () => {
        // 5 tiers over 200 units: bands of 40. Proj_Ship_Diamond_Boron_Missile is the real one.
        //
        // The BAND is still worth showing - it is what the dropoff tags describe - but within one
        // object every in-radius hardpoint takes an equal share. The falloff the engine applies is
        // the OBJECT's, taken once from its distance to the blast, and a hardpoint's own distance
        // sets only the delay before its share lands. This test used to assert a per-hardpoint
        // falloff, which is what the preview drew before the routing was decompiled.
        const missile = projectile({
            blastAreaDamage: 150, blastAreaRange: 200,
            blastAreaDropoff: true, blastAreaDropoffTiers: 5,
        });

        const hits = blastVictims(missile, 'a', [
            { id: 'a', distance: 0 },
            { id: 'b', distance: 60 },
            { id: 'c', distance: 190 },
        ]);

        assert.deepEqual(hits.map(h => h.tier), [0, 1, 4]);
        assert.deepEqual(hits.map(h => h.blastDamage), [50, 50, 50]);
    });

    it('still counts a destroyed hardpoint, whose share is then thrown away', () => {
        // It used to be filtered out, which quietly handed its share to the survivors. The engine
        // collects victims by Is_Destroyable and never asks Is_Destroyed, so the share is drawn and
        // lost - and the blast delivers less against a ship that has already lost hardpoints.
        const hits = blastVictims(
            projectile({ blastAreaDamage: 30, blastAreaRange: 100 }), 'target', HARDPOINTS,
            new Set(['near']));

        assert.deepEqual(hits.map(h => h.id), ['target', 'near', 'mid']);
        assert.equal(hits.find(h => h.id === 'near')!.wasted, true);
        assert.equal(hits.find(h => h.id === 'mid')!.blastDamage, 10);
    });
});

describe('blastShare', () => {
    it('is the full damage with no dropoff declared', () => {
        assert.equal(blastShare(80, 100, false, null), 1);
        assert.equal(blastShare(0, 100, false, null), 1);
    });

    it('steps down one band at a time', () => {
        assert.equal(blastShare(0, 100, true, 4), 1);
        assert.equal(blastShare(30, 100, true, 4), 0.75);
        assert.equal(blastShare(99, 100, true, 4), 0.25);
    });

    it('never goes below the last band or above the first', () => {
        assert.equal(blastShare(100, 100, true, 4), 0.25);
        assert.equal(blastShare(-5, 100, true, 4), 1);
    });

    it('is full damage where the tier count is nonsense', () => {
        // Rather than dividing by zero and blanking the panel.
        assert.equal(blastShare(50, 100, true, 0), 1);
        assert.equal(blastShare(50, 0, true, 4), 1);
    });
});

describe('candidatesFrom', () => {
    const positions = {
        a: { x: 0, y: 0, z: 0 },
        b: { x: 30, y: 40, z: 0 },
        c: { x: 0, y: 0, z: 200 },
    };

    it('measures every hardpoint from the one that was hit', () => {
        const from = candidatesFrom(positions, 'a');

        assert.deepEqual(from, [
            { id: 'a', distance: 0 },
            { id: 'b', distance: 50 },
            { id: 'c', distance: 200 },
        ]);
    });

    it('leaves out a hardpoint whose bone never loaded', () => {
        // A hardpoint with no position cannot be measured, and guessing zero would put it at the
        // epicentre of every blast.
        const from = candidatesFrom({ ...positions, d: null }, 'a');

        assert.deepEqual(from.map(c => c.id), ['a', 'b', 'c']);
    });

    it('is empty when the target itself has no position', () => {
        assert.deepEqual(candidatesFrom(positions, 'missing'), []);
    });
});

describe('blastVictims, when nothing can be located', () => {
    const bomb: PreviewProjectile = {
        id: 'Proj_Bomb', render: 'Model', damage: 100, damageType: 'Damage_Default',
        doesShieldDamage: false, doesEnergyDamage: false, doesHitpointDamage: true,
        blastAreaDamage: 40, blastAreaRange: 300, blastAreaDropoff: false,
    };

    /**
     * The target is hit whatever the candidate list says.
     *
     * `candidatesFrom` returns an EMPTY list when the target's own bone position is unknown - a
     * part whose geometry has not loaded, or a hardpoint with no attachment bone. Every shot goes
     * through here now that the attacker carries its own blast, so an empty list used to mean the
     * shot simply vanished: no direct damage, no blast, nothing said. This file's own rule already
     * read "the target is always included, even if it somehow sits outside its own blast radius",
     * and that was only true while it happened to be in the list.
     */
    it('still hits the target when the candidate list is empty', () => {
        const hits = blastVictims(bomb, 'HP_A', []);

        assert.equal(hits.length, 1);
        assert.equal(hits[0].id, 'HP_A');
        assert.equal(hits[0].directDamage, 100);
        assert.equal(hits[0].blastDamage, 40);
    });

    it('still hits the target when the list holds only other things', () => {
        const hits = blastVictims(bomb, 'HP_A', [{ id: 'HP_B', distance: 50 }]);

        assert.deepEqual(hits.map(h => h.id).sort(), ['HP_A', 'HP_B']);
        assert.equal(hits.find(h => h.id === 'HP_A')!.directDamage, 100);
    });

    it('does not hit the target twice when it IS in the list', () => {
        const hits = blastVictims(bomb, 'HP_A', [{ id: 'HP_A', distance: 0 }]);

        assert.equal(hits.filter(h => h.id === 'HP_A').length, 1);
    });
});

describe('a blast against a ship that has already lost hardpoints', () => {
    // Measured in Area_Blast_Surrounding_Objects: the victim list is collected by Is_Destroyable and
    // never asks Is_Destroyed, so wreckage inside the radius keeps drawing shares that nothing
    // receives. It is why splash decays against a battered ship.
    const PROJECTILE = projectile({
        damage: 0,
        blastAreaDamage: 400,
        blastAreaRange: 100,
        blastAreaDropoff: false,
    });

    const CANDIDATES = [
        { id: 'A', distance: 0 },
        { id: 'B', distance: 20 },
        { id: 'C', distance: 40 },
        { id: 'D', distance: 60 },
    ];

    it('still counts a destroyed hardpoint among the victims', () => {
        const hits = blastVictims(PROJECTILE, 'A', CANDIDATES, new Set(['C']));

        assert.deepEqual(hits.map(h => h.id), ['A', 'B', 'C', 'D']);
    });

    it('marks the share that lands on wreckage as wasted', () => {
        const hits = blastVictims(PROJECTILE, 'A', CANDIDATES, new Set(['C']));

        assert.equal(hits.find(h => h.id === 'C')!.wasted, true);
        assert.equal(hits.find(h => h.id === 'B')!.wasted, false);
    });

    it('does not hand the wasted share to anybody else', () => {
        // The division is over everything in range, wreckage included, so the survivors do NOT get
        // a bigger slice for the dead one - the blast simply delivers less.
        const hits = blastVictims(PROJECTILE, 'A', CANDIDATES, new Set(['C']));

        assert.equal(hits.find(h => h.id === 'B')!.blastDamage, 100);
    });
});

describe('a blast dividing its damage', () => {
    const PROJECTILE = projectile({
        damage: 0,
        blastAreaDamage: 400,
        blastAreaRange: 100,
        blastAreaDropoff: false,
    });

    it('splits the blast among everything it catches, rather than repeating it', () => {
        // The total delivered is the same whether it catches one hardpoint or four: `blast / count`
        // each. Repeating the full amount per victim would make a blast stronger against a ship
        // with more hardpoints, which is the opposite of what the engine does.
        const four = blastVictims(PROJECTILE, 'A', [
            { id: 'A', distance: 0 }, { id: 'B', distance: 20 },
            { id: 'C', distance: 40 }, { id: 'D', distance: 60 },
        ]);
        const one = blastVictims(PROJECTILE, 'A', [{ id: 'A', distance: 0 }]);

        assert.equal(four.reduce((sum, h) => sum + h.blastDamage, 0), 400);
        assert.equal(one[0].blastDamage, 400);
    });

    it('gives every hardpoint of one object an EQUAL share, however far apart', () => {
        // The falloff is the object's, taken once from its distance to the blast. A hardpoint's own
        // distance sets only the delay before its share lands, never the amount.
        const hits = blastVictims(PROJECTILE, 'A', [
            { id: 'A', distance: 0 }, { id: 'B', distance: 95 },
        ]);

        assert.equal(hits.find(h => h.id === 'A')!.blastDamage, 200);
        assert.equal(hits.find(h => h.id === 'B')!.blastDamage, 200);
    });
});

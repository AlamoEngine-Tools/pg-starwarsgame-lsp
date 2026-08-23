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

const MOUNTS = [
    { id: 'target', distance: 0 },
    { id: 'near', distance: 20 },
    { id: 'mid', distance: 60 },
    { id: 'far', distance: 500 },
];

describe('blastVictims', () => {
    it('hits ONLY the target when the projectile has no blast area', () => {
        // The overwhelming majority - 63 of foc's projectiles declare one, out of 173.
        const hits = blastVictims(projectile(), 'target', MOUNTS);

        assert.deepEqual(hits.map(h => h.id), ['target']);
        assert.equal(hits[0].blastDamage, 0);
    });

    it('hits every mount inside the range', () => {
        const hits = blastVictims(
            projectile({ blastAreaDamage: 30, blastAreaRange: 100 }), 'target', MOUNTS);

        assert.deepEqual(hits.map(h => h.id), ['target', 'near', 'mid']);
    });

    it('applies FULL blast damage everywhere inside a flat blast', () => {
        // 53 of the 63 declare no dropoff at all, so flat is the normal case.
        const hits = blastVictims(
            projectile({ blastAreaDamage: 30, blastAreaRange: 100 }), 'target', MOUNTS);

        assert.deepEqual(hits.map(h => h.blastDamage), [30, 30, 30]);
    });

    it('gives the target its direct damage AND the blast', () => {
        // Both, and the data forces it: Proj_Veers_AT_AT_Max_Power_Laser_Red declares
        // Projectile_Damage 0.0 with a blast of 40, so a target taking only the direct number
        // would take nothing at all from a weapon that is plainly meant to hurt.
        const hits = blastVictims(
            projectile({ damage: 50, blastAreaDamage: 30, blastAreaRange: 100 }), 'target', MOUNTS);

        assert.equal(hits[0].directDamage, 50);
        assert.equal(hits[0].blastDamage, 30);
        assert.equal(hits[1].directDamage, 0);
    });

    it('caps the victims where the projectile says so, nearest first', () => {
        const hits = blastVictims(
            projectile({ blastAreaDamage: 30, blastAreaRange: 100, blastAreaMaxVictims: 2 }),
            'target', MOUNTS);

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

    it('drops off by tier where the projectile declares tiers', () => {
        // 5 tiers over 200 units: bands of 40. Proj_Ship_Diamond_Boron_Missile is the real one.
        const missile = projectile({
            blastAreaDamage: 150, blastAreaRange: 200,
            blastAreaDropoff: true, blastAreaDropoffTiers: 5,
        });

        const hits = blastVictims(missile, 'a', [
            { id: 'a', distance: 0 },    // tier 0
            { id: 'b', distance: 50 },   // tier 1
            { id: 'c', distance: 170 },  // tier 4
        ]);

        assert.equal(hits[0].blastDamage, 150);
        assert.equal(hits[1].blastDamage, 120);
        assert.equal(hits[2].blastDamage, 30);
    });

    it('ignores a mount already destroyed', () => {
        const hits = blastVictims(
            projectile({ blastAreaDamage: 30, blastAreaRange: 100 }), 'target', MOUNTS,
            new Set(['near']));

        assert.deepEqual(hits.map(h => h.id), ['target', 'mid']);
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

    it('measures every mount from the one that was hit', () => {
        const from = candidatesFrom(positions, 'a');

        assert.deepEqual(from, [
            { id: 'a', distance: 0 },
            { id: 'b', distance: 50 },
            { id: 'c', distance: 200 },
        ]);
    });

    it('leaves out a mount whose bone never loaded', () => {
        // A mount with no position cannot be measured, and guessing zero would put it at the
        // epicentre of every blast.
        const from = candidatesFrom({ ...positions, d: null }, 'a');

        assert.deepEqual(from.map(c => c.id), ['a', 'b', 'c']);
    });

    it('is empty when the target itself has no position', () => {
        assert.deepEqual(candidatesFrom(positions, 'missing'), []);
    });
});

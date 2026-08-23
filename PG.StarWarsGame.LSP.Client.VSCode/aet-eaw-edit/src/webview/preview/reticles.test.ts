// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import type { PreviewHardpoint, PreviewReticles } from '../../protocol/modelPreview';

import {
    RETICLE_STATES, healthColour, reticleMarks, reticleSizePx, trackedStateOf,
} from './reticles';

const RETICLES: PreviewReticles = {
    byType: {
        HARD_POINT_WEAPON_LASER: {
            enemy: 'I_Hard_Point_Reticle_Weapons',
            enemyTracked: 'I_Hard_Point_Reticle_Weapons_Tracked',
            friendly: 'I_Hard_Point_Reticle_Weapons',
            friendlyTracked: 'I_Hard_Point_Reticle_Weapons_Tracked',
            friendlyRepairing: 'I_Hard_Point_Reticle_Weapons_Repair',
            friendlyDisabled: 'I_Hard_Point_Reticle_Weapons',
            friendlyDisabledTracked: 'I_Hard_Point_Reticle_Weapons_Tracked',
        },
        HARD_POINT_ENGINE: {
            enemy: 'I_Hard_Point_Reticle_Engines',
            enemyTracked: null,
        },
    },
    icons: {
        I_Hard_Point_Reticle_Weapons: 'data:image/png;base64,AAAA',
        I_Hard_Point_Reticle_Weapons_Tracked: 'data:image/png;base64,BBBB',
        I_Hard_Point_Reticle_Weapons_Repair: 'data:image/png;base64,CCCC',
        I_Hard_Point_Reticle_Engines: 'data:image/png;base64,DDDD',
    },
    enemyScreenSize: 0.03,
    friendlyScreenSize: 0.04,
};

function hardpoint(over: Partial<PreviewHardpoint> = {}): PreviewHardpoint {
    return {
        id: 'HP_Star_Destroyer_Weapon_FL',
        partId: 'hp:HP_Star_Destroyer_Weapon_FL',
        type: 'HARD_POINT_WEAPON_LASER',
        attachBone: 'HP_F-L_BONE',
        isDestroyable: true,
        isTargetable: true,
        engineDeathHidesEngineParticles: false,
        ...over,
    };
}

describe('reticleMarks', () => {
    it('marks a targetable hardpoint with the icon its type names', () => {
        const marks = reticleMarks([hardpoint()], RETICLES, 'enemy', new Set());

        assert.equal(marks.length, 1);
        assert.equal(marks[0].hardpointId, 'HP_Star_Destroyer_Weapon_FL');
        assert.equal(marks[0].iconUri, 'data:image/png;base64,AAAA');
    });

    it('looks the type up EXACTLY as the hardpoint spells it', () => {
        // The wire used to camel-case dictionary keys, so `HARD_POINT_WEAPON_LASER` arrived as
        // `harD_POINT_WEAPON_LASER` and this lookup found nothing - silently, because a type with no
        // mapping is a legitimate state. Measured against a live server; see the server-side guard.
        const mangled: PreviewReticles = {
            ...RETICLES,
            byType: { harD_POINT_WEAPON_LASER: RETICLES.byType.HARD_POINT_WEAPON_LASER },
        };

        assert.deepEqual(reticleMarks([hardpoint()], mangled, 'enemy', new Set()), []);
    });

    it('draws nothing over a mount the game will not let you target', () => {
        // 97 of foc's hardpoints say Is_Targetable No.
        assert.deepEqual(
            reticleMarks([hardpoint({ isTargetable: false })], RETICLES, 'enemy', new Set()), []);
    });

    it('draws nothing over a mount that has been shot away', () => {
        assert.deepEqual(
            reticleMarks([hardpoint()], RETICLES, 'enemy',
                new Set(['HP_Star_Destroyer_Weapon_FL'])), []);
    });

    it('anchors on the ATTACHMENT BONE, which is the hull s', () => {
        // Corrected 2026-08-23 off user feedback. It used to centre on the attached model's bounds,
        // which was both wrong - the game draws the mark on the attachment bone - and slow, since
        // it walked a whole part's geometry per mark per tick and made the viewport crawl.
        const mark = reticleMarks([hardpoint()], RETICLES, 'enemy', new Set())[0];

        assert.equal(mark.partId, 'hull');
        assert.equal(mark.bone, 'HP_F-L_BONE');
    });

    it('carries how worn the mount is, for the colour', () => {
        const marks = reticleMarks(
            [hardpoint({ health: 400 })], RETICLES, 'enemy', new Set(),
            { HP_Star_Destroyer_Weapon_FL: 100 });

        assert.equal(marks[0].healthFraction, 0.25);
    });

    it('has no health fraction for a mount that declares none', () => {
        // 210 of foc's hardpoints declare no Health, and they never wear down.
        assert.equal(
            reticleMarks([hardpoint({ health: null })], RETICLES, 'enemy', new Set())[0]
                .healthFraction, null);
    });

    it('falls back to the hull bone for a mount that attaches no model', () => {
        // 137 hardpoints name no Model_To_Attach - the tractor beam and the fighter bay among them -
        // and their attach bone is a bone of the hull.
        const marks = reticleMarks(
            [hardpoint({ partId: null, attachBone: 'HP_trac_bone' })], RETICLES, 'enemy', new Set());

        assert.equal(marks[0].partId, 'hull');
        assert.equal(marks[0].bone, 'HP_trac_bone');
    });

    it('skips a type the reticle map says nothing about', () => {
        // Shipped data never hits this - every type used has a mapping in both trees - but a mod
        // overriding GameConstants can.
        assert.deepEqual(
            reticleMarks([hardpoint({ type: 'HARD_POINT_INVENTED' })], RETICLES, 'enemy', new Set()),
            []);
    });

    it('skips a state the type leaves blank rather than drawing the wrong art', () => {
        const engine = hardpoint({ id: 'HP_Engines', type: 'HARD_POINT_ENGINE' });

        assert.equal(reticleMarks([engine], RETICLES, 'enemy', new Set()).length, 1);
        assert.deepEqual(reticleMarks([engine], RETICLES, 'enemyTracked', new Set()), []);
    });

    it('skips a name the server could not decode', () => {
        // Icons are absent when no game directory is configured. The map still arrives, so the
        // client must not draw a broken image over every mount.
        const noArt: PreviewReticles = { ...RETICLES, icons: {} };

        assert.deepEqual(reticleMarks([hardpoint()], noArt, 'enemy', new Set()), []);
    });

    it('has no marks at all without a reticle map', () => {
        assert.deepEqual(reticleMarks([hardpoint()], null, 'enemy', new Set()), []);
        assert.deepEqual(reticleMarks([hardpoint()], undefined, 'enemy', new Set()), []);
    });

    it('offers every state the engine declares, enemy first', () => {
        // Seven, though on shipped data the disabled pair reuses the plain and tracked art. A modder
        // giving them separate art needs somewhere to see it.
        assert.equal(RETICLE_STATES.length, 7);
        assert.equal(RETICLE_STATES[0].id, 'enemy');
    });
});

describe('reticleSizePx', () => {
    /** Binary floating point: 720 * 0.03 lands a whisker under 21.6. */
    const near = (actual: number, expected: number) =>
        assert.ok(Math.abs(actual - expected) < 1e-9, `${actual} is not ${expected}`);

    it('is a fraction of the viewport HEIGHT', () => {
        near(reticleSizePx(0.03, 1000, 720), 21.6);
        near(reticleSizePx(0.03, 400, 720), 21.6);
    });

    it('takes the enemy or friendly size according to the state being shown', () => {
        near(reticleSizePx(RETICLES.enemyScreenSize, 1000, 720), 21.6);
        near(reticleSizePx(RETICLES.friendlyScreenSize, 1000, 720), 28.8);
    });

    it('never collapses to nothing, whatever the data says', () => {
        assert.ok(reticleSizePx(0, 1000, 720) >= 8);
        assert.ok(reticleSizePx(null, 1000, 720) >= 8);
        assert.ok(reticleSizePx(undefined, 1000, 720) >= 8);
    });

    it('does not blow up on a viewport that has not been laid out yet', () => {
        assert.ok(reticleSizePx(0.03, 0, 0) >= 8);
    });
});

describe('healthColour', () => {
    it('runs green to red as the mount is worn down', () => {
        // The game's own ramp, as the user gave it: bright green at full health through yellow and
        // orange to red at nothing left.
        assert.equal(healthColour(1), '#3cd63c');
        assert.equal(healthColour(0), '#e02020');
    });

    it('passes through yellow and THEN orange', () => {
        // Order matters and it is the conventional one - yellow is the healthier of the two.
        const yellow = healthColour(0.66);
        const orange = healthColour(0.33);

        assert.notEqual(yellow, orange);
        assert.equal(yellow, '#d6d63c');
        assert.equal(orange, '#e08a20');
    });

    it('is green for a mount that declares no health at all', () => {
        // 210 of foc's hardpoints declare none. They cannot be worn down, so they are never
        // anything but whole - showing them red would report damage that cannot happen.
        assert.equal(healthColour(null), '#3cd63c');
    });

    it('clamps rather than inventing a colour off either end', () => {
        assert.equal(healthColour(2), healthColour(1));
        assert.equal(healthColour(-1), healthColour(0));
    });
});

describe('trackedStateOf', () => {
    it('gives the tracked twin of a plain state', () => {
        // The game swaps to it under the cursor, so hovering is the honest way to see it.
        assert.equal(trackedStateOf('enemy'), 'enemyTracked');
        assert.equal(trackedStateOf('friendly'), 'friendlyTracked');
        assert.equal(trackedStateOf('friendlyDisabled'), 'friendlyDisabledTracked');
    });

    it('leaves a state that is already tracked alone', () => {
        // Hovering it should change nothing rather than fall back to something unrelated.
        assert.equal(trackedStateOf('enemyTracked'), 'enemyTracked');
        assert.equal(trackedStateOf('friendlyRepairing'), 'friendlyRepairing');
    });
});

describe('the tracked art on a mark', () => {
    it('carries both, so the hover needs no second pass', () => {
        const mark = reticleMarks([hardpoint()], RETICLES, 'enemy', new Set())[0];

        assert.equal(mark.iconUri, 'data:image/png;base64,AAAA');
        assert.equal(mark.trackedUri, 'data:image/png;base64,BBBB');
    });

    it('falls back to the plain art where a type declares no tracked form', () => {
        const engine = hardpoint({ id: 'HP_Engines', type: 'HARD_POINT_ENGINE' });
        const mark = reticleMarks([engine], RETICLES, 'enemy', new Set())[0];

        assert.equal(mark.trackedUri, mark.iconUri);
    });
});

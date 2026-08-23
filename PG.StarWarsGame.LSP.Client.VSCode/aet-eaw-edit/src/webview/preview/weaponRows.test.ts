// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import type { PreviewHardpoint, PreviewWeapon } from '../../protocol/modelPreview';

import type { TreeItem } from './previewTree';

import {
    allBankIds, bankTitle, boneRowIndex, fireBoneTitle, visibleArcs, weaponRows,
} from './weaponRows';

function weapon(over: Partial<PreviewWeapon> = {}): PreviewWeapon {
    return {
        id: 'hardpoint:HP_Turbolaser',
        source: 'Hardpoint',
        hardpointId: 'HP_Turbolaser',
        label: 'HARD_POINT_WEAPON_LASER',
        fireBones: ['FP_00'],
        firePointMode: 'CycleBones',
        firesForward: false,
        inaccuracy: [],
        fireModes: ['Fire_When_Idle'],
        ...over,
    };
}

function hardpoint(over: Partial<PreviewHardpoint> = {}): PreviewHardpoint {
    return {
        id: 'HP_Turbolaser',
        partId: 'HP_Turbolaser',
        type: 'HARD_POINT_WEAPON_LASER',
        attachBone: 'HP_F-L_BONE',
        isDestroyable: true,
        isTargetable: true,
        engineDeathHidesEngineParticles: false,
        ...over,
    };
}

describe('weaponRows', () => {
    it('puts a hardpoint weapon on the part it is mounted on, not on the hull', () => {
        // The cone hangs off the fire bone of the ATTACHED model. Anchoring it on the hull would
        // draw a Star Destroyer's arcs from its centre, all 148 of them, in a fan.
        const rows = weaponRows(
            { weapons: [weapon()], hardpoints: [hardpoint()] }, new Set());

        assert.equal(rows.length, 1);
        assert.equal(rows[0].partId, 'HP_Turbolaser');
    });

    it('anchors a unit weapon on the hull, because that is where MuzzleA lives', () => {
        const rows = weaponRows({
            weapons: [weapon({
                id: 'bank:A',
                source: 'Unit',
                hardpointId: null,
                label: 'Bank A',
                fireBones: ['MuzzleA_00', 'MuzzleA_01'],
            })],
            hardpoints: [],
        }, new Set());

        assert.equal(rows[0].partId, 'hull');
        assert.deepEqual(rows[0].fireBones, ['MuzzleA_00', 'MuzzleA_01']);
    });

    it('falls back to the hull when the hardpoint contributed no part', () => {
        // A hardpoint with no Model_To_Attach yields no part - 137 of them - but it still declares
        // fire bones, and those are the HULL's own bones.
        const rows = weaponRows(
            { weapons: [weapon()], hardpoints: [hardpoint({ partId: null })] }, new Set());

        assert.equal(rows[0].partId, 'hull');
    });

    it('gives one arc per fire bone', () => {
        const rows = weaponRows({
            weapons: [weapon({
                fireBones: ['FP_00', 'FP_01'],
                range: 2000,
                coneWidthDegrees: 60,
                coneHeightDegrees: 30,
            })],
            hardpoints: [hardpoint()],
        }, new Set());

        assert.deepEqual(rows[0].arcs, [
            {
                weaponId: 'hardpoint:HP_Turbolaser',
                partId: 'HP_Turbolaser',
                bone: 'FP_00',
                widthDegrees: 60,
                heightDegrees: 30,
                range: 2000,
            },
            {
                weaponId: 'hardpoint:HP_Turbolaser',
                partId: 'HP_Turbolaser',
                bone: 'FP_01',
                widthDegrees: 60,
                heightDegrees: 30,
                range: 2000,
            },
        ]);
    });

    it('draws a bare ray for a weapon that declares reach but no cone', () => {
        // Fires_Forward weapons have no traverse at all, so a cone would be an invention. The ray
        // still answers the question the gizmo exists for - which way does this point.
        const rows = weaponRows({
            weapons: [weapon({ range: 500, firesForward: true })],
            hardpoints: [hardpoint()],
        }, new Set());

        assert.equal(rows[0].arcs.length, 1);
        assert.equal(rows[0].arcs[0].widthDegrees, 0);
        assert.equal(rows[0].arcs[0].heightDegrees, 0);
    });

    it('draws nothing for a weapon with no reach, rather than a zero-length stub', () => {
        const rows = weaponRows(
            { weapons: [weapon()], hardpoints: [hardpoint()] }, new Set());

        assert.deepEqual(rows[0].arcs, []);
    });

    it('marks a row whose mount has been shot away', () => {
        const rows = weaponRows(
            { weapons: [weapon()], hardpoints: [hardpoint()] }, new Set(['HP_Turbolaser']));

        assert.equal(rows[0].destroyed, true);
    });

    it('never marks a unit weapon destroyed - the hull is the subject', () => {
        const rows = weaponRows({
            weapons: [weapon({ id: 'bank:A', source: 'Unit', hardpointId: null })],
            hardpoints: [hardpoint()],
        }, new Set(['HP_Turbolaser']));

        assert.equal(rows[0].destroyed, false);
    });

    describe('the readout on the row', () => {
        function row(over: Partial<PreviewWeapon>) {
            return weaponRows(
                { weapons: [weapon(over)], hardpoints: [hardpoint()] }, new Set())[0];
        }

        it('reads a full cadence as a sentence', () => {
            assert.equal(
                row({ pulseCount: 3, pulseDelaySeconds: 0.1, rechargeSeconds: 2 }).cadence,
                '3 shots, 0.1s apart, 2s recharge');
        });

        it('does not pluralise a single shot, and drops a delay it cannot use', () => {
            assert.equal(
                row({ pulseCount: 1, pulseDelaySeconds: 0.1, rechargeSeconds: 4.5 }).cadence,
                '1 shot, 4.5s recharge');
        });

        it('says only what was declared', () => {
            assert.equal(row({ rechargeSeconds: 2 }).cadence, '2s recharge');
            assert.equal(row({ pulseCount: 4 }).cadence, '4 shots');
            assert.equal(row({}).cadence, null);
        });

        it('carries the damage and its type together', () => {
            assert.equal(
                row({ damage: 24, damageType: 'Damage_Normal' }).damage,
                '24 damage, Damage_Normal');
            assert.equal(row({ damage: 24 }).damage, '24 damage');
            assert.equal(row({ damageType: 'Damage_Normal' }).damage, 'Damage_Normal');
            assert.equal(row({}).damage, null);
        });

        it('reads a minimum range as the near end of a band', () => {
            assert.equal(row({ range: 2000, minRange: 200 }).reach, '200-2000 units');
            assert.equal(row({ range: 2000 }).reach, '2000 units');
            assert.equal(row({}).reach, null);
        });

        it('states the cone, because a wide fan and a narrow one are different weapons', () => {
            assert.equal(row({ coneWidthDegrees: 60, coneHeightDegrees: 30 }).cone,
                '60 x 30 deg');
            assert.equal(row({ coneWidthDegrees: 360, coneHeightDegrees: 45 }).cone,
                '360 x 45 deg');
            assert.equal(row({}).cone, null);
        });

        it('never writes a trailing zero at a modder', () => {
            // The XML says 2.0000 and 0.10; the row says 2 and 0.1.
            assert.equal(row({ range: 2000.0, minRange: 0 }).reach, '2000 units');
            assert.equal(row({ damage: 24.5 }).damage, '24.5 damage');
        });
    });
});

describe('visibleArcs', () => {
    const rows = weaponRows({
        weapons: [
            weapon({ range: 2000, coneWidthDegrees: 60, coneHeightDegrees: 30 }),
            weapon({
                id: 'hardpoint:HP_Missile',
                hardpointId: 'HP_Missile',
                label: 'HARD_POINT_WEAPON_MISSILE',
                fireBones: ['FP_01'],
                range: 1200,
                coneWidthDegrees: 20,
                coneHeightDegrees: 20,
            }),
        ],
        hardpoints: [hardpoint(), hardpoint({ id: 'HP_Missile', partId: 'HP_Missile' })],
    }, new Set());

    it('draws every bank when the master is on and nothing is switched off', () => {
        assert.deepEqual(
            visibleArcs(rows, true, new Set()).map(arc => arc.weaponId),
            ['hardpoint:HP_Turbolaser', 'hardpoint:HP_Missile']);
    });

    it('draws none at all when the master is off, whatever the rows say', () => {
        // The pill on the stage is the master, exactly as the effects pill is: one place to kill
        // the lot without losing which banks you had picked.
        assert.deepEqual(visibleArcs(rows, false, new Set()), []);
    });

    it('drops the banks that were switched off', () => {
        assert.deepEqual(
            visibleArcs(rows, true, new Set(['hardpoint:HP_Missile'])).map(arc => arc.weaponId),
            ['hardpoint:HP_Turbolaser']);
    });

    it('drops a bank whose mount has been shot away', () => {
        // A destroyed mount's model is hidden; leaving its cone hanging in the gap would say the
        // wreck still shoots.
        const damaged = weaponRows({
            weapons: [weapon({ range: 2000, coneWidthDegrees: 60, coneHeightDegrees: 30 })],
            hardpoints: [hardpoint()],
        }, new Set(['HP_Turbolaser']));

        assert.deepEqual(visibleArcs(damaged, true, new Set()), []);
    });
});

describe('boneRowIndex', () => {
    function bone(id: string, name: string): TreeItem {
        return { id, kind: 'bone', name, parentId: null, visible: true, gatedOff: false };
    }

    it('finds a fire bone whatever case the XML wrote it in', () => {
        // The engine uppercases bone names and the files do not, so `MuzzleA_00` off a weapon and
        // `muzzlea_00` off the model are the same bone.
        const index = boneRowIndex([bone('bone:4', 'MuzzleA_00')]);

        assert.equal(index.get('muzzlea_00'), 'bone:4');
        assert.equal(index.get('MUZZLEA_00'.toLowerCase()), 'bone:4');
    });

    it('ignores meshes and effects - a fire bone is a bone', () => {
        const index = boneRowIndex([
            { ...bone('mesh:1', 'MuzzleA_00'), kind: 'mesh' },
            bone('bone:4', 'MuzzleA_00'),
        ]);

        assert.equal(index.get('muzzlea_00'), 'bone:4');
    });

    it('resolves a repeated name the same way every time', () => {
        // Two bones may share a name. First in skeleton order wins, so the same click selects the
        // same bone on every reload rather than whichever row was built first this time.
        const index = boneRowIndex([bone('bone:9', 'FP_00'), bone('bone:2', 'FP_00')]);

        assert.equal(index.get('fp_00'), 'bone:9');
    });

    it('says nothing about a bone the hull does not carry', () => {
        // A hardpoint's fire bone lives on the MOUNTED model, and the tree is the hull's skeleton -
        // so the row's button is disabled rather than selecting the wrong thing.
        assert.equal(boneRowIndex([bone('bone:4', 'MuzzleA_00')]).get('fp_00'), undefined);
    });
});

describe('the tooltips on a weapon row', () => {
    function row(over: Partial<PreviewWeapon>) {
        return weaponRows({ weapons: [weapon(over)], hardpoints: [hardpoint()] }, new Set())[0];
    }

    it('offers the cone when there is one to draw', () => {
        assert.match(bankTitle(row({ range: 2000 })), /^Draw this bank/);
    });

    it('explains a tick that would do nothing rather than just greying it out', () => {
        assert.match(bankTitle(row({})), /no range/);
    });

    it('says the mount is gone before it says anything about cones', () => {
        const dead = weaponRows(
            { weapons: [weapon({ range: 2000 })], hardpoints: [hardpoint()] },
            new Set(['HP_Turbolaser']))[0];

        assert.match(bankTitle(dead), /shot away/);
    });

    it('tells a reader why a fire bone cannot be pointed at', () => {
        assert.match(fireBoneTitle('FP_00', false), /not of the hull/);
        assert.equal(fireBoneTitle('MuzzleA_00', true), 'Select MuzzleA_00 on the model');
    });
});

describe('naming a weapon row', () => {
    it('names a mounted weapon after its MOUNT, not after its type', () => {
        // Seen on the real Star Destroyer: `label` is the hardpoint's Type, and six laser mounts
        // gave six rows all reading HARD_POINT_WEAPON_LASER. The id is what tells them apart, and
        // it is what the Hardpoints list beside this one already shows.
        const rows = weaponRows({
            weapons: [
                weapon({ id: 'hardpoint:HP_FL', hardpointId: 'HP_Star_Destroyer_Weapon_FL' }),
                weapon({ id: 'hardpoint:HP_FR', hardpointId: 'HP_Star_Destroyer_Weapon_FR' }),
            ],
            hardpoints: [],
        }, new Set());

        assert.deepEqual(rows.map(row => row.name),
            ['HP_Star_Destroyer_Weapon_FL', 'HP_Star_Destroyer_Weapon_FR']);
    });

    it('keeps the type, because it is what picks the reticle and the tooltip', () => {
        assert.equal(weaponRows({ weapons: [weapon()], hardpoints: [] }, new Set())[0].label,
            'HARD_POINT_WEAPON_LASER');
    });

    it('names a unit bank after the bank, which is all it has', () => {
        const row = weaponRows({
            weapons: [weapon({ id: 'bank:A', source: 'Unit', hardpointId: null, label: 'Bank A' })],
            hardpoints: [],
        }, new Set())[0];

        assert.equal(row.name, 'Bank A');
    });
});

describe('the turret on a weapon row', () => {
    it('comes through, because a unit weapon is where a turret is usually declared', () => {
        // MEASURED on the live AT-AA: its turret is on `bank:A`, not on any hardpoint - it has
        // none. The sweep reads it from here.
        const row = weaponRows({
            weapons: [weapon({
                turret: {
                    turretBone: 'B_Turret_Base', barrelBone: 'B_Missile_Launcher',
                    rotateExtentDegrees: 360, elevateExtentDegrees: 45,
                },
            })],
            hardpoints: [],
        }, new Set())[0];

        assert.equal(row.turret?.turretBone, 'B_Turret_Base');
    });

    it('is null for a weapon that declares none', () => {
        assert.equal(weaponRows({ weapons: [weapon()], hardpoints: [] }, new Set())[0].turret, null);
    });
});

describe('which banks a subject opens with', () => {
    function drawable(id: string) {
        return weapon({ id, hardpointId: id, range: 1000, coneWidthDegrees: 175,
            coneHeightDegrees: 160 });
    }

    it('opens with EVERY bank off', () => {
        // The user's rule: the stage pill is all-or-nothing and everything starts off. Measured
        // reason it matters - the Nebulon B's four mounts each declare 175 by 160 degrees, very
        // nearly omnidirectional, so drawing them together fills the viewport however correct the
        // geometry is.
        const rows = weaponRows(
            { weapons: [drawable('a'), drawable('b'), drawable('c')], hardpoints: [] }, new Set());

        assert.deepEqual([...allBankIds(rows)].sort(), ['a', 'b', 'c']);
    });

    it('counts only the banks that can actually be DRAWN', () => {
        // A weapon with no reach has no cone, so there is nothing to switch either way and an id in
        // the hidden set for it would make the pill's "n of m" count lie.
        const rows = weaponRows({
            weapons: [weapon({ id: 'no-range' }), drawable('b')],
            hardpoints: [],
        }, new Set());

        assert.deepEqual([...allBankIds(rows)], ['b']);
    });

    it('is empty when nothing can be drawn at all', () => {
        assert.equal(allBankIds(weaponRows(
            { weapons: [weapon()], hardpoints: [] }, new Set())).size, 0);
    });
});

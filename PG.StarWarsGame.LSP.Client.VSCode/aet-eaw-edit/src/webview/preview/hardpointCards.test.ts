// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { type PreviewHardpoint, type PreviewWeapon } from '../../protocol/modelPreview';
import {
    hardpointCards, hardpointFacts, healthBar, muzzleLabel, unitWeaponFacts, unitWeapons,
    weaponFacts,
} from './hardpointCards';
import { weaponRows } from './weaponRows';

// The same shapes weaponRows.test.ts builds, so a field this join needs cannot be missing from
// one fixture and present in the other.
function hardpoint(over: Partial<PreviewHardpoint> = {}): PreviewHardpoint {
    return {
        id: 'HP_Turbolaser',
        partId: 'HP_Turbolaser',
        type: 'HARD_POINT_WEAPON_LASER',
        attachBone: 'HP_F-L_Bone',
        isDestroyable: true,
        isTargetable: true,
        health: 400,
        engineDeathHidesEngineParticles: false,
        ...over,
    };
}

function weapon(over: Partial<PreviewWeapon> = {}): PreviewWeapon {
    return {
        id: 'hardpoint:HP_Turbolaser',
        source: 'Hardpoint',
        hardpointId: 'HP_Turbolaser',
        label: 'HARD_POINT_WEAPON_LASER',
        fireBones: ['MuzzleA_00'],
        firePointMode: 'CycleBones',
        firesForward: false,
        inaccuracy: [],
        fireModes: ['Fire_When_Idle'],
        ...over,
    };
}

const scene = (over: Partial<{ weapons: PreviewWeapon[]; hardpoints: PreviewHardpoint[] }> = {}) => ({
    weapons: [weapon()],
    hardpoints: [hardpoint()],
    ...over,
});

describe('hardpointCards', () => {
    it('gives every hardpoint a card, armed or not', () => {
        // 137 shipped hardpoints attach no model and some carry no weapon at all. A shield
        // generator is still a thing you shoot off, so it still needs its card.
        const cards = hardpointCards(
            scene({ weapons: [], hardpoints: [hardpoint({ id: 'HP_Shield' })] }), new Set());

        assert.equal(cards.length, 1);
        assert.equal(cards[0].id, 'HP_Shield');
        assert.equal(cards[0].weapon, null);
    });

    it('joins each hardpoint to the weapon it carries', () => {
        // The whole point of the card: the Hardpoints list held the health and the Weapons list
        // held the arc, so destroying a hardpoint and drawing its cone were two rows apart.
        const cards = hardpointCards(scene(), new Set());

        assert.equal(cards[0].weapon?.id, 'hardpoint:HP_Turbolaser');
        assert.deepEqual(cards[0].weapon?.fireBones, ['MuzzleA_00']);
    });

    it('carries the game`s own words, not just our derived ones', () => {
        const cards = hardpointCards(
            scene({ hardpoints: [hardpoint({ tooltipText: 'Turbolaser Battery' })] }), new Set());

        assert.equal(cards[0].tooltip, 'Turbolaser Battery');
        assert.equal(cards[0].type, 'HARD_POINT_WEAPON_LASER');
    });

    it('knows which cards have been shot away', () => {
        const cards = hardpointCards(scene(), new Set(['HP_Turbolaser']));

        assert.equal(cards[0].destroyed, true);
    });

    /**
     * The sweep control belongs to the hardpoint that declares a traverse, not to the panel.
     *
     * It was one global button acting on everything at once, which on a hull whose hardpoints
     * declare different extents is a control that cannot say what it will do.
     */
    it('marks only the cards that declare a traverse as sweepable', () => {
        const cards = hardpointCards(scene({
            hardpoints: [
                hardpoint({ id: 'HP_Fixed' }),
                hardpoint({ id: 'HP_Turret', turret: { rotateExtentDegrees: 180 } }),
            ],
            weapons: [
                weapon({ id: 'w1', hardpointId: 'HP_Fixed' }),
                weapon({ id: 'w2', hardpointId: 'HP_Turret' }),
            ],
        }), new Set());

        assert.equal(cards.find(c => c.id === 'HP_Fixed')?.sweepable, false);
        assert.equal(cards.find(c => c.id === 'HP_Turret')?.sweepable, true);
    });

    it('takes a traverse declared on the WEAPON, not only on the hardpoint', () => {
        // The AT-AA declares its turret on the weapon bank and has no hardpoints at all. A card
        // that only read the hardpoint's own turret would call that one fixed.
        const cards = hardpointCards(scene({
            hardpoints: [hardpoint()],
            weapons: [weapon({ turret: { rotateExtentDegrees: 90 } })],
        }), new Set());

        assert.equal(cards[0].sweepable, true);
    });
});

describe('unitWeapons', () => {
    it('keeps the weapons without hardpoints', () => {
        // A fighter's guns are on the unit itself. Those have no card to live in, so they still
        // need a list of their own - and putting them in the Hardpoints section would be a lie.
        const rows = weaponRows(scene({
            weapons: [weapon(), weapon({ id: 'unit:A', source: 'Unit', hardpointId: null })],
        }), new Set());

        assert.deepEqual(unitWeapons(rows).map(r => r.id), ['unit:A']);
    });

    it('is empty when every weapon sits on a hardpoint', () => {
        assert.deepEqual(unitWeapons(weaponRows(scene(), new Set())), []);
    });
});

describe('hardpointFacts', () => {
    it('leaves the health to the BAR and reads the bone it is attached to', () => {
        // The bar carries "400/400 HP" on the face of the card. Repeating it in the info panel
        // underneath is the same number twice, three lines apart.
        const [card] = hardpointCards(scene(), new Set());

        assert.deepEqual(hardpointFacts(card), ['on HP_F-L_Bone']);
    });

    it('says a hardpoint cannot be shot off rather than leaving it unsaid', () => {
        // Is_Destroyable is off on 97 of the 355 shipped hardpoints. A disabled tick with no reason
        // beside it is the failure the disable-don`t-hide rule exists to avoid.
        const [card] = hardpointCards(
            scene({ hardpoints: [hardpoint({ isDestroyable: false, health: null })] }), new Set());

        assert.ok(hardpointFacts(card).includes('indestructible'));
    });

    it('omits what the file does not declare rather than printing a null', () => {
        const [card] = hardpointCards(
            scene({ hardpoints: [hardpoint({ health: null, attachBone: null })] }), new Set());

        assert.deepEqual(hardpointFacts(card), []);
    });

    it('reports a traverse in the degrees the XML writes', () => {
        const [card] = hardpointCards(scene({
            hardpoints: [hardpoint({
                turret: { rotateExtentDegrees: 180, elevateExtentDegrees: 30 },
            })],
        }), new Set());

        assert.ok(hardpointFacts(card).some(f => f.includes('180')));
    });
});

describe('the tooltip, key and text', () => {
    it('carries what a player reads and what the author wrote, separately', () => {
        // The server resolves the key; the card shows the text and keeps the key beside it. Before
        // this the card showed TEXT_WEAPON_TURBOLASER - the one thing on it a player never sees.
        const [card] = hardpointCards(scene({
            hardpoints: [hardpoint({
                tooltipKey: 'TEXT_WEAPON_TURBOLASER',
                tooltipText: 'Turbolaser Battery',
            })],
        }), new Set());

        assert.equal(card.tooltip, 'Turbolaser Battery');
        assert.equal(card.tooltipKey, 'TEXT_WEAPON_TURBOLASER');
    });

    it('keeps the key when nothing resolved, so the author can still find it', () => {
        const [card] = hardpointCards(
            scene({ hardpoints: [hardpoint({ tooltipKey: 'TEXT_MISSING' })] }), new Set());

        assert.equal(card.tooltip, null);
        assert.equal(card.tooltipKey, 'TEXT_MISSING');
    });
});

describe('healthBar', () => {
    const card = (over = {}) => hardpointCards(
        scene({ hardpoints: [hardpoint(over)] }), new Set())[0];

    it('reads the pool the reader has actually shot down', () => {
        assert.deepEqual(healthBar(card(), 12), {
            fraction: 0.03, label: '12/400 HP', colour: '#e02020', destroyed: false,
        });
    });

    it('is full and green before anything has hit it', () => {
        const bar = healthBar(card(), null);

        assert.equal(bar?.fraction, 1);
        assert.equal(bar?.colour, '#3cd63c');
        assert.equal(bar?.label, '400/400 HP');
    });

    it('greys out and empties once the hardpoint is gone', () => {
        // Not red: red is "nearly dead and still there". A destroyed hardpoint is not on a ramp at
        // all, and colouring it like the worst live state says it is still in the fight.
        const gone = hardpointCards(scene(), new Set(['HP_Turbolaser']))[0];
        const bar = healthBar(gone, 0);

        assert.equal(bar?.destroyed, true);
        assert.equal(bar?.fraction, 0);
        assert.notEqual(bar?.colour, '#e02020');
    });

    it('shows nothing for a hardpoint that declares no health', () => {
        // 97 shipped hardpoints are indestructible and many declare no Health at all. An empty bar
        // would claim a pool that does not exist.
        assert.equal(healthBar(card({ health: null }), null), null);
    });

    it('never runs past either end of its own bar', () => {
        assert.equal(healthBar(card(), 900)?.fraction, 1);
        assert.equal(healthBar(card(), -5)?.fraction, 0);
    });
});

describe('weaponFacts', () => {
    it('is a LIST of labelled values, not a sentence', () => {
        // It used to read "1100 units - 175 x 160 deg - 5 shots, 0.2s apart, 3s recharge - ..." on
        // the face of every card: three wrapped lines of prose, unreadable five cards deep.
        const [card] = hardpointCards(scene(), new Set());
        const facts = weaponFacts(card.weapon!);

        assert.ok(facts.length > 0);
        assert.ok(facts.every(pair => pair.length === 2), 'every fact is a label and a value');
    });

    it('omits what the file does not declare rather than printing a blank', () => {
        const [card] = hardpointCards(scene(), new Set());

        assert.ok(weaponFacts(card.weapon!).every(([, value]) => value.length > 0));
    });
});

describe('muzzleLabel', () => {
    it('names the slot the XML names, by position', () => {
        // Fire_Bone_A and Fire_Bone_B, in that order. The card showed only the bone name, which
        // says where the shot comes from but not which of the two slots declared it.
        assert.equal(muzzleLabel(0), 'Muzzle A');
        assert.equal(muzzleLabel(1), 'Muzzle B');
    });

    it('keeps going past the two the engine reads, rather than repeating a letter', () => {
        // MuzzleB is the last one the engine uses, but a mod can list more bones and two rows
        // both called "Muzzle B" would be worse than an honest C.
        assert.equal(muzzleLabel(2), 'Muzzle C');
    });
});

describe('unitWeaponFacts', () => {
    const unit = (over: Partial<PreviewWeapon> = {}) => weaponRows(scene({
        weapons: [weapon({
            id: 'unit:Laser', source: 'Unit', hardpointId: null, label: 'Anti-fighter laser',
            ...over,
        })],
        hardpoints: [],
    }), new Set())[0];

    /**
     * The hardpoint card's facts, minus the two that cannot apply to a unit weapon.
     *
     * The user, asking for this card: *"Reuse the hardpoint design cards for them. Health obviously
     * doesn't apply, they may have more muzzles, but the general idea is the same."* A unit weapon
     * is not a target - it is neither destructible nor indestructible and has no pool - and it hangs
     * off the hull rather than an attachment bone of its own.
     */
    it('says nothing about health or destructibility, which a unit weapon has not got', () => {
        assert.deepEqual(unitWeaponFacts(unit()), []);
    });

    /**
     * A turret IS one a unit weapon can declare, and the AT-AA is why this matters: its turret is on
     * the WEAPON and it has no hardpoints at all, so the hardpoint card was the only place a sweep
     * could be started and that unit could never start one.
     */
    it('reports a traverse in the degrees the XML writes', () => {
        const facts = unitWeaponFacts(unit({
            turret: {
                turretBone: 'B_Turret_Base',
                rotateExtentDegrees: 360,
                elevateExtentDegrees: 45,
            },
        }));

        assert.deepEqual(facts, ['swings 360 deg', 'elevates 45 deg']);
    });

    it('omits an extent the file leaves at zero rather than printing it', () => {
        const facts = unitWeaponFacts(unit({
            turret: { turretBone: 'B_Turret_Base', rotateExtentDegrees: 90 },
        }));

        assert.deepEqual(facts, ['swings 90 deg']);
    });
});

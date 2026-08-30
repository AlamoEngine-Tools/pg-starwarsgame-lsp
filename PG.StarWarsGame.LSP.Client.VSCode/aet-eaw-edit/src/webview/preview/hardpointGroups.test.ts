// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { type HardpointCard } from './hardpointCards';
import { groupHardpoints, groupOf, typeLabel } from './hardpointGroups';

const card = (id: string, type: string | null): HardpointCard => ({
    id, type, tooltip: null, tooltipKey: null, attachBone: null, health: null,
    isDestroyable: true, isTargetable: true, destroyed: false, weapon: null, turret: null,
    sweepable: false,
});

const DESTROYER = [
    card('HP_SD_Weapon_FL', 'HARD_POINT_WEAPON_LASER'),
    card('HP_SD_Engine', 'HARD_POINT_ENGINE'),
    card('HP_SD_Weapon_FR', 'HARD_POINT_WEAPON_LASER'),
    card('HP_SD_Shield', 'HARD_POINT_SHIELD_GENERATOR'),
];

describe('groupHardpoints', () => {
    it('gathers the cards of one type under one heading', () => {
        // A Star Destroyer's eight laser hardpoints are eight cards that differ only by which
        // corner they sit on. Read as one flat list they are eight things to scroll past.
        const groups = groupHardpoints(DESTROYER, '');

        assert.deepEqual(groups.map(g => g.type), [
            'HARD_POINT_WEAPON_LASER', 'HARD_POINT_ENGINE', 'HARD_POINT_SHIELD_GENERATOR',
        ]);
        assert.deepEqual(groups[0].cards.map(c => c.id), ['HP_SD_Weapon_FL', 'HP_SD_Weapon_FR']);
    });

    it('keeps the order the types first appear in, not an alphabet', () => {
        // The scene lists hardpoints in the order the XML does, and that order carries meaning a
        // sort would throw away - the author grouped them the way they think about the hull.
        assert.equal(groupHardpoints(DESTROYER, '')[0].type, 'HARD_POINT_WEAPON_LASER');
    });

    it('gives a hardpoint with no type a group of its own rather than dropping it', () => {
        const groups = groupHardpoints([card('HP_Odd', null)], '');

        assert.equal(groups.length, 1);
        assert.deepEqual(groups[0].cards.map(c => c.id), ['HP_Odd']);
    });

    it('filters on the TYPE, which is what the groups are', () => {
        const groups = groupHardpoints(DESTROYER, 'engine');

        assert.deepEqual(groups.map(g => g.type), ['HARD_POINT_ENGINE']);
    });

    it('filters on the NAME too, because that is the other thing a reader knows', () => {
        const groups = groupHardpoints(DESTROYER, 'shield');

        assert.deepEqual(groups.flatMap(g => g.cards.map(c => c.id)), ['HP_SD_Shield']);
    });

    it('ignores case, like every other filter in the panel', () => {
        assert.equal(groupHardpoints(DESTROYER, 'WEAPON_LASER')[0].cards.length, 2);
    });

    it('drops a group entirely when nothing in it matches', () => {
        assert.deepEqual(groupHardpoints(DESTROYER, 'zzz'), []);
    });

    it('keeps everything when the filter is blank', () => {
        assert.equal(groupHardpoints(DESTROYER, '   ').flatMap(g => g.cards).length, 4);
    });
});

describe('typeLabel', () => {
    it('reads the type as words, keeping the tag underneath it', () => {
        // HARD_POINT_WEAPON_LASER is what the file says and what a reader greps for; it is also
        // shouting. The heading says it plainly and the cards under it still carry the tag.
        assert.equal(typeLabel('HARD_POINT_WEAPON_LASER'), 'Weapon laser');
    });

    it('says so plainly when a hardpoint declares no type', () => {
        assert.equal(typeLabel(null), 'No type declared');
    });

    it('leaves a type it does not recognise alone rather than mangling it', () => {
        assert.equal(typeLabel('MY_MOD_THING'), 'My mod thing');
    });
});

describe('groupOf', () => {
    /**
     * Which heading a card lives under.
     *
     * Clicking a targeting mark on the model picks that hardpoint, and the list has to be able to
     * bring the card into view - which it cannot do while the group holding it is folded shut.
     */
    it('finds the group a card is in', () => {
        assert.equal(groupOf(groupHardpoints(DESTROYER, ''), 'HP_SD_Engine'), 'HARD_POINT_ENGINE');
    });

    it('is null for a card that is not in any group', () => {
        // Filtered out, or simply not a hardpoint. Either way there is nothing to reveal.
        assert.equal(groupOf(groupHardpoints(DESTROYER, 'engine'), 'HP_SD_Shield'), null);
    });

    it('finds a card whose hardpoint declares no type', () => {
        const groups = groupHardpoints([card('HP_Odd', null)], '');

        assert.equal(groupOf(groups, 'HP_Odd'), '');
    });
});

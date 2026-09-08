// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { passiveEffectPlaysNow, passiveEffects } from './passiveSubject';
import type { PreviewParticle } from '../../protocol/modelPreview';

const particle = (over: Partial<PreviewParticle> = {}): PreviewParticle => ({
    id: 'Star_Destroyer_Death_Clone#0',
    systemRef: 'p_imperial_explosion_big00',
    partId: 'Star_Destroyer_Death_Clone',
    bone: 'p_explosion',
    boneIndex: 4,
    gate: 'always',
    startsVisible: true,
    ...over,
});

describe('passiveEffects', () => {
    it('re-homes every effect onto the part that was actually loaded', () => {
        // The server describes a MODEL - it cannot know what the client will call the instance -
        // so the descriptor names the clone object and the client names the part.
        const moved = passiveEffects([particle(), particle({ id: 'x#1' })],
            'deathclone:Star_Destroyer_Death_Clone');

        assert.deepEqual(moved.map(p => p.partId),
            ['deathclone:Star_Destroyer_Death_Clone', 'deathclone:Star_Destroyer_Death_Clone']);
    });

    it('gives each one an id unique to this instance', () => {
        // The same prop can be dropped by two mirrored hardpoints at once. Keeping the descriptor's own
        // ids would have the second instance's effects collide with the first's.
        const left = passiveEffects([particle(), particle()], 'wreck:HP_Weapon_FL');
        const right = passiveEffects([particle(), particle()], 'wreck:HP_Weapon_FR');

        const ids = [...left, ...right].map(p => p.id);
        assert.equal(new Set(ids).size, 4);
    });

    it('changes nothing else about them', () => {
        const moved = passiveEffects([particle({ alt: 2, lod: 1, startsVisible: false })], 'w:1');

        assert.equal(moved[0].systemRef, 'p_imperial_explosion_big00');
        assert.equal(moved[0].bone, 'p_explosion');
        assert.equal(moved[0].boneIndex, 4);
        assert.equal(moved[0].alt, 2);
        assert.equal(moved[0].lod, 1);
        assert.equal(moved[0].startsVisible, false);
    });

    it('has nothing to place for a model that carries no proxies', () => {
        assert.deepEqual(passiveEffects([], 'w:1'), []);
    });
});

describe('passiveEffectPlaysNow', () => {
    it('plays what the model says is on', () => {
        // The opening rules hold a hull's ungated fires and dust back until they are asked for.
        // They are about the FIRST MOMENT of a preview, and a passive subject has no first moment:
        // a death clone exists because the ship died, and its explosions ARE the death.
        assert.equal(passiveEffectPlaysNow(particle()), true);
    });

    it('respects the model\'s own switch, which always has the last word', () => {
        assert.equal(passiveEffectPlaysNow(particle({ startsVisible: false })), false);
    });

    it('plays a level-tagged effect too, leaving the ALT gate to decide', () => {
        assert.equal(passiveEffectPlaysNow(particle({ alt: 1 })), true);
    });
});

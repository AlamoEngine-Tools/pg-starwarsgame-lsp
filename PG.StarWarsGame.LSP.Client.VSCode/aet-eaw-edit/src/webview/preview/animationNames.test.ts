// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { actionOf, familyOf, groupAnimations } from './animationNames';

describe('actionOf', () => {
    /** The model's own name is not information: every clip in the list starts with it. */
    it('drops the model prefix and the file extension', () => {
        assert.equal(actionOf('Ev_stardestroyer', 'Ev_stardestroyer_idle_00.ala'), 'idle_00');
    });

    it('matches the prefix whatever the case, since the index does', () => {
        assert.equal(actionOf('ev_stardestroyer', 'EV_STARDESTROYER_DIE_00.ALA'), 'DIE_00');
    });

    /**
     * 50 of the shipped animations sit beside a model whose name does not prefix them - the
     * `Ri_padowan_*` clips beside `Ri_obiwan` among them. The whole name is the best answer there;
     * hiding the clip because the rule did not fire would be worse.
     */
    it('keeps the whole stem when the prefix does not match', () => {
        assert.equal(actionOf('Ri_obiwan', 'Ri_padowan_attack_00.ala'), 'Ri_padowan_attack_00');
    });
});

describe('familyOf', () => {
    // Straight from the shipped corpus: 132 distinct actions across 1363 files.
    const cases: [string, string][] = [
        ['idle_00', 'idle'],
        ['attackidle_01', 'idle'],
        ['crouchidle_00', 'idle'],
        ['flylandidle_00', 'idle'],
        ['attention_00', 'idle'],
        ['move_00', 'move'],
        ['walkmove_00', 'move'],
        ['movestart_00', 'move'],
        ['move_endone_00', 'move'],
        ['run_00', 'move'],
        ['fly_00', 'move'],
        ['force_run_00', 'move'],
        ['turnl_00', 'turn'],
        ['turnr_quarter_00', 'turn'],
        ['crouchturnl_00', 'turn'],
        ['rotate_00', 'turn'],
        ['attack_00', 'attack'],
        ['fl_attack_00', 'attack'],
        ['flame_attack_00', 'attack'],
        ['bombtoss_00', 'attack'],
        ['block_blaster_00', 'attack'],
        ['die_00', 'death'],
        ['fw_die_02', 'death'],
        ['chokedeath_00', 'death'],
        ['crushed_00', 'death'],
        ['self_destruct_00', 'death'],
        ['flinchb_00', 'hit'],
        ['attackflinchl_00', 'hit'],
        ['build_00', 'state'],
        ['deploy_00', 'state'],
        ['undeploy_00', 'state'],
        ['powerup_00', 'state'],
        ['shield_off_00', 'state'],
        ['open_00', 'state'],
        ['land_00', 'travel'],
        ['takeoff_00', 'travel'],
        ['flylanddrop_00', 'travel'],
        ['ropeslide_00', 'travel'],
        ['cinematic_00', 'cinematic'],
        ['talkgesture_00', 'cinematic'],
        ['celebrate_00', 'cinematic'],
        ['hc_win_00', 'cinematic'],
        ['transition_00', 'cinematic'],
    ];

    for (const [action, family] of cases) {
        it(`puts ${action} in ${family}`, () => {
            assert.equal(familyOf(action), family);
        });
    }

    /** An action nobody has a rule for still has to land somewhere it can be found. */
    it('falls back to other rather than dropping an action it does not know', () => {
        assert.equal(familyOf('wibble_00'), 'other');
    });

    /** Case is not a signal: the corpus ships both. */
    it('reads the action whatever its case', () => {
        assert.equal(familyOf('IDLE_00'), 'idle');
    });

    /**
     * Order matters, and this is the pair that proves it. `attackidle` contains both "attack" and
     * "idle"; it is an idle stance held while armed, not an attack, so the idle rule has to win.
     */
    it('reads attackidle as an idle, not as an attack', () => {
        assert.equal(familyOf('attackidle_00'), 'idle');
        assert.notEqual(familyOf('attackidle_00'), familyOf('attack_00'));
    });
});

describe('groupAnimations', () => {
    const files = [
        'Ev_stardestroyer_die_00.ala',
        'Ev_stardestroyer_idle_00.ala',
        'Ev_stardestroyer_idle_01.ala',
        'Ev_stardestroyer_move_00.ala',
    ];

    it('gathers the clips into their families', () => {
        const groups = groupAnimations('Ev_stardestroyer', files);

        assert.deepEqual(groups.map(g => g.family), ['idle', 'move', 'death']);
        assert.deepEqual(groups[0].items.map(i => i.label), ['Idle 00', 'Idle 01']);
    });

    /**
     * A fixed family order, not one that follows whatever the model happens to carry. The reader
     * learns where Death sits once; a list that reorders itself per model teaches nothing.
     */
    it('keeps the families in a fixed order whatever the model has', () => {
        const order = (names: string[]): string[] =>
            groupAnimations('m', names).map(g => g.family);

        assert.deepEqual(order(['m_die_00.ala', 'm_idle_00.ala']), ['idle', 'death']);
        assert.deepEqual(order(['m_idle_00.ala', 'm_die_00.ala']), ['idle', 'death']);
    });

    it('offers no empty family', () => {
        for (const group of groupAnimations('Ev_stardestroyer', files)) {
            assert.ok(group.items.length > 0, `${group.family} is empty`);
        }
    });

    /** The file is what gets loaded, so it has to survive the grouping intact. */
    it('carries each clip`s filename through untouched', () => {
        const all = groupAnimations('Ev_stardestroyer', files).flatMap(g => g.items);

        assert.deepEqual(new Set(all.map(i => i.name)), new Set(files));
    });

    it('gives every family a readable heading', () => {
        const groups = groupAnimations('Ev_stardestroyer', files);

        assert.deepEqual(groups.map(g => g.label), ['Idle', 'Movement', 'Death']);
    });
});

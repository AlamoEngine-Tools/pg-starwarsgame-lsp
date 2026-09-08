// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { clipNamingModel, groupAnimations, readClip } from './animationNames';

const clip = (file: string) => readClip('Ei_trooper', file);

describe('readClip, what a filename says', () => {
    it('splits the model, the action and the take apart', () => {
        // The exporter writes `<model>_<action>_<take>.ala` and 3736 of the 3771 shipped files
        // follow it. The action is the only part worth reading; the model repeats on every row.
        const read = clip('Ei_trooper_idle_03.ala');

        assert.equal(read.action, 'idle');
        assert.equal(read.take, 3);
    });

    it('matches the model prefix however either side cased it', () => {
        // The index holds `EI_TROOPER_IDLE_00.ALA` and `Ei_trooper_idle_00.ala` alike.
        assert.equal(clip('EI_TROOPER_IDLE_00.ALA').action, 'idle');
    });

    it('keeps the whole stem when the model does not prefix it', () => {
        // 50 of the shipped animations verify against no model in either tree. Showing the full
        // name is a worse label than a trimmed one and a far better outcome than hiding the clip.
        assert.equal(readClip('Ev_stardestroyer', 'Nb_basepad_deploy_00.ala').action,
            'nb_basepad_deploy');
    });

    it('reads a clip with no take as having none', () => {
        // 35 files carry no `_NN` at all - `deploy`, `idle00`, `girder_rise`. They are their own
        // action rather than take zero of something, because nothing says which.
        assert.equal(clip('Ei_trooper_deploy.ala').take, null);
        assert.equal(clip('Ei_trooper_idle00.ala').take, null);
        assert.equal(clip('Ei_trooper_idle00.ala').action, 'idle00');
    });
});

describe('the stance an action is held in', () => {
    it('reads crouch as the SPREAD_OUT stance rather than as its own family', () => {
        // The reader's own words: crouchidle is the idle played while the unit is spread out, and
        // crouchattack would be the attack in that stance. Both are the ordinary action, held
        // differently - so the stance comes off and the action underneath decides the family.
        const read = clip('Ei_trooper_crouchidle_00.ala');

        assert.equal(read.stance, 'crouched');
        assert.equal(read.action, 'idle');
        assert.equal(read.family, 'idle');
    });

    it('takes the stance off a movement action too', () => {
        assert.equal(clip('Ei_trooper_crouchmove_00.ala').family, 'move');
        assert.equal(clip('Ei_trooper_crouchturnl_00.ala').family, 'move');
    });

    it('would read a crouched attack the same way, though none ships', () => {
        // The corpus has only crouchidle, crouchmove and crouchturnl/r. The rule is about the
        // stance, not about the four files that happen to use it.
        assert.equal(clip('Ei_trooper_crouchattack_00.ala').family, 'attack');
    });

    it('reads the deployed stance the same way', () => {
        assert.equal(clip('Ei_trooper_deployed_die_00.ala').stance, 'deployed');
        assert.equal(clip('Ei_trooper_deployed_die_00.ala').family, 'death');
    });

    it('leaves an ordinary action standing', () => {
        assert.equal(clip('Ei_trooper_idle_00.ala').stance, 'normal');
    });
});

describe('which family an action belongs to', () => {
    it('files TURNING under movement, which is what it is', () => {
        // 274 files. Turning was a family of its own, which put a unit's locomotion in two places.
        for (const action of ['turnl', 'turnr', 'turnl_half', 'turn_left', 'rotate']) {
            assert.equal(clip(`Ei_trooper_${action}_00.ala`).family, 'move', action);
        }
    });

    it('keeps attackidle an IDLE - a stance held while armed, not an attack', () => {
        assert.equal(clip('Ei_trooper_attackidle_00.ala').family, 'idle');
    });

    it('keeps a flinch a reaction, whatever caused it', () => {
        assert.equal(clip('Ei_trooper_attackflinchl_00.ala').family, 'hit');
        assert.equal(clip('Ei_trooper_dodge_left_00.ala').family, 'hit');
    });

    it('keeps a chokedeath a death, not an attack', () => {
        assert.equal(clip('Ei_trooper_chokedeath_00.ala').family, 'death');
    });

    it('reads a landing as an arrival rather than as locomotion', () => {
        assert.equal(clip('Ei_trooper_flylandidle_00.ala').family, 'idle');
        assert.equal(clip('Ei_trooper_ropeland_00.ala').family, 'travel');
    });
});

describe('whether an action is meant to loop', () => {
    it('loops the idles, which is the reader`s first rule', () => {
        for (const action of ['idle', 'attackidle', 'crouchidle', 'hold', 'attention']) {
            assert.equal(clip(`Ei_trooper_${action}_00.ala`).loops, true, action);
        }
    });

    it('loops locomotion', () => {
        for (const action of ['move', 'walkmove', 'crouchmove', 'run', 'fly', 'turnl']) {
            assert.equal(clip(`Ei_trooper_${action}_00.ala`).loops, true, action);
        }
    });

    it('never loops a TRANSITION, however it is spelled', () => {
        // The corpus marks its own: `turnl_begin`/`_end`/`_half`/`_quarter`, `movestart`,
        // `move_endone..four`, and `transition` - which ships misspelled as `transiotion` too.
        for (const action of [
            'turnl_begin', 'turnl_end', 'turnl_half', 'turnr_quarter', 'movestart',
            'move_endone', 'move_endfour', 'transition', 'transiotion', 'trans',
            'bomb_transition', 'flame_end',
        ]) {
            assert.equal(clip(`Ei_trooper_${action}_00.ala`).loops, false, action);
        }
    });

    it('never loops a death, a hit, a deploy or a special', () => {
        for (const action of [
            'die', 'death', 'crushed', 'flinchf', 'dodge_left', 'deploy', 'undeploy', 'build',
            'land', 'takeoff', 'open', 'close', 'bombtoss', 'block_blaster', 'self_destruct',
            'force_run', 'celebrate', 'talk',
        ]) {
            assert.equal(clip(`Ei_trooper_${action}_00.ala`).loops, false, action);
        }
    });

    it('loops anything the file itself calls a loop', () => {
        // `flame_loop`, `contaiminate_loop`, `force_reveal_loop` - the author said so in the name,
        // and none of the three is in a family that loops by default.
        for (const action of ['flame_loop', 'contaiminate_loop', 'force_reveal_loop']) {
            assert.equal(clip(`Ei_trooper_${action}_00.ala`).loops, true, action);
        }
    });
});

describe('groupAnimations', () => {
    const files = [
        'Ei_trooper_idle_00.ala', 'Ei_trooper_idle_01.ala', 'Ei_trooper_idle_02.ala',
        'Ei_trooper_crouchidle_00.ala',
        'Ei_trooper_move_00.ala',
        'Ei_trooper_turnl_00.ala', 'Ei_trooper_turnl_begin_00.ala',
        'Ei_trooper_die_00.ala', 'Ei_trooper_die_01.ala',
    ];

    const groups = groupAnimations('Ei_trooper', files);
    const find = (family: string) => groups.find(group => group.family === family);

    it('gathers the takes of one action into ONE row', () => {
        // Three idle takes were three sibling rows. They are one animation the engine picks a take
        // of - and the row that plays them at random is the whole point of the change.
        const idle = find('idle')?.actions.find(a => a.action === 'idle' && a.stance === 'normal');

        assert.equal(idle?.takes.length, 3);
        assert.deepEqual(idle?.takes.map(t => t.take), [0, 1, 2]);
    });

    it('keeps a stance as its own row rather than folding it into the plain action', () => {
        const idles = find('idle')?.actions.map(a => a.id) ?? [];

        assert.deepEqual(idles.sort(), ['crouched:idle', 'normal:idle']);
    });

    it('says so in the label, so two Idle rows are not a mystery', () => {
        const crouched = find('idle')?.actions.find(a => a.stance === 'crouched');

        assert.match(crouched?.label ?? '', /spread out/i);
    });

    it('puts turning in with the movement', () => {
        assert.deepEqual(find('move')?.actions.map(a => a.action).sort(),
            ['move', 'turnl', 'turnl_begin']);
    });

    it('carries the loop rule onto the row', () => {
        const move = find('move')?.actions ?? [];

        assert.equal(move.find(a => a.action === 'turnl')?.loops, true);
        assert.equal(move.find(a => a.action === 'turnl_begin')?.loops, false);
    });

    it('orders the families the same way whatever the model ships', () => {
        // So Death is always in the same place, whatever was opened.
        assert.deepEqual(groups.map(g => g.family), ['idle', 'move', 'death']);
    });

    it('leaves out a family the model has nothing for', () => {
        // A heading over nothing is a claim that the model should have had one.
        assert.equal(find('attack'), undefined);
    });

    it('says nothing about a model with no clips', () => {
        assert.deepEqual(groupAnimations('Ei_trooper', []), []);
    });
});

describe('clipNamingModel', () => {
    /**
     * A unit that borrows another model's animation set has clips named after the SOURCE.
     *
     * The panel used to key the library off the hull, which stripped no prefix at all: every action
     * on `NI_SandPeople_C` would have read `ri_infantry_idle` and landed in one undifferentiated
     * family. It never showed, because those clips were being dropped before they reached the
     * client at all - the identical-skeleton rule the user reported. With the clips loading, the
     * naming has to follow them.
     */
    it('names the override model when there is one', () => {
        assert.equal(
            clipNamingModel({
                animationSource: 'RI_INFANTRY.ALO',
                parts: [{ origin: 'Hull', modelRef: 'NI_SandPeople_C.ALO' }],
            }),
            'RI_INFANTRY');
    });

    it('falls back to the hull when nothing is overridden', () => {
        assert.equal(
            clipNamingModel({
                animationSource: null,
                parts: [{ origin: 'Hull', modelRef: 'EV_StarDestroyer.ALO' }],
            }),
            'EV_StarDestroyer');
    });

    it('takes the first part when none is marked as the hull', () => {
        assert.equal(
            clipNamingModel({ parts: [{ origin: 'Turret', modelRef: 'Turret.alo' }] }),
            'Turret');
    });

    it('is empty when there is no scene and when there are no parts', () => {
        assert.equal(clipNamingModel(null), '');
        assert.equal(clipNamingModel({ parts: [] }), '');
    });
});

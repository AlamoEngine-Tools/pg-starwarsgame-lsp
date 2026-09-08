// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import type { PreviewHardpoint, PreviewParticle } from '../../protocol/modelPreview';
import {
    describeGate, effectPrefix, groupState, listedInModelTree, particleGroups, replacedByAbility,
    type GroupingContext,
} from './particleScene';

function particle(over: Partial<PreviewParticle> = {}): PreviewParticle {
    return {
        id: 'hull#0',
        systemRef: 'p_hp_imperial_damage',
        partId: 'hull',
        bone: 'p_hp_imperial_damage',
        boneIndex: 2,
        gate: 'Always',
        hardpointId: null,
        startsVisible: true,
        ...over,
    };
}

function context(
    particles: readonly PreviewParticle[], hardpoints: readonly PreviewHardpoint[] = [],
): GroupingContext {
    return { particles, hardpoints };
}

describe('effectPrefix', () => {
    it('reads the marker token an effect name starts with', () => {
        // The artist's convention, measured across both shipped trees: 66 `pe_`, 6 `pte_`, 5
        // `pptw_`, 3 each of `prs_`, `pgw_` and `pi_`.
        assert.equal(effectPrefix('pe_homeoneengines_lrg'), 'pe');
        assert.equal(effectPrefix('pptw_ptwsa'), 'pptw');
        assert.equal(effectPrefix('pi_damage_elec_SD00'), 'pi');
    });

    it('keeps the second token when the first is the bare particle marker', () => {
        // `p` alone says only "this is a particle" - 502 of the 1957 shipped assets start with it,
        // so it groups nothing. The token after it is what names the effect.
        assert.equal(effectPrefix('p_hp_stardestroyer_damage'), 'p_hp');
        assert.equal(effectPrefix('p_explosion_big00'), 'p_explosion');
    });

    it('reads a name however it is cased', () => {
        // The same system arrives as `P_HP_STARDESTROYER_DAMAGE` on the Calamari Cruiser and
        // `p_hp_stardestroyer_damage` on the Nebulon-B.
        assert.equal(effectPrefix('P_HP_STARDESTROYER_DAMAGE'), 'p_hp');
        assert.equal(effectPrefix('Pte_tartanengine_sml'), 'pte');
    });

    it('takes a name with no underscore whole', () => {
        assert.equal(effectPrefix('pion'), 'pion');
        assert.equal(effectPrefix(''), '');
    });
});

describe('particleGroups', () => {
    it('finds the engines by the gate the MODEL declares', () => {
        // `Engine_Particles` on the object is the model's own statement about what these are for.
        const groups = particleGroups(context([
            particle({ id: 'a', systemRef: 'pe_homeoneengines_lrg', gate: 'HardpointAlive' }),
            particle({ id: 'b', systemRef: 'pe_homeoneengines_sml', gate: 'HardpointAlive' }),
        ]));

        const engines = groups.find(group => group.label === 'Engines');
        assert.notEqual(engines, undefined);
        assert.deepEqual(engines!.ids.sort(), ['a', 'b']);
    });

    it('finds them by PREFIX too, on a ship whose engines are on no hardpoint', () => {
        // The Tartan and the Corvette. Their engine effects carry gate `Always`, because neither
        // declares an engine hardpoint - so a gate-only rule finds no engines at all on them, which
        // is the "there is no toggle group for the engines" the reader reported.
        const groups = particleGroups(context([
            particle({ id: 'a', systemRef: 'pe_tartanengine_lrg', gate: 'Always' }),
            particle({ id: 'b', systemRef: 'pe_tartanengine_sml', gate: 'Always' }),
        ]));

        const engines = groups.find(group => group.label === 'Engines');
        assert.deepEqual(engines?.ids.sort(), ['a', 'b']);
    });

    it('is ONE group when the gate and the prefix agree, not two', () => {
        // The reported fault, exactly: on the Nebulon-B `p_hp_stardestroyer_damage` and
        // `Hardpoint damage` were two rows over the same six systems, so ticking one moved the
        // other and nothing said why. A meaning is one group however many rules recognise it.
        const groups = particleGroups(context(
            Array.from({ length: 6 }, (_, i) => particle({
                id: `hull#${i}`,
                systemRef: 'p_hp_stardestroyer_damage',
                gate: 'HardpointDestroyed',
            }))));

        assert.deepEqual(groups.map(group => group.label), ['Hardpoint damage']);
        assert.equal(groups[0].ids.length, 6);
    });

    it('takes the UNION when the gate and the prefix disagree', () => {
        // Neither rule is the whole answer, so a system either names is in.
        const groups = particleGroups(context([
            particle({ id: 'gated', systemRef: 'p_wash', gate: 'HardpointAlive' }),
            particle({ id: 'named', systemRef: 'pe_engines', gate: 'Always' }),
        ]));

        assert.deepEqual(groups.find(g => g.label === 'Engines')?.ids.sort(), ['gated', 'named']);
    });

    it('keeps a named family of ONE, which is the engine switch the reader asked for', () => {
        // The Nebulon-B carries a single `pe_nebulonengines`. The old floor of two dropped it, so
        // the one group a reader wanted on that ship was the one group it never showed.
        const groups = particleGroups(context([
            particle({ id: 'a', systemRef: 'pe_nebulonengines', gate: 'HardpointAlive' }),
        ]));

        assert.deepEqual(groups.map(group => group.label), ['Engines']);
    });

    it('names the ability families off their own prefixes', () => {
        const groups = particleGroups(context([
            particle({ id: 'a', systemRef: 'pte_corvetteengines', gate: 'Always' }),
            particle({ id: 'b', systemRef: 'pptw_ptwsa', gate: 'Always' }),
            particle({ id: 'c', systemRef: 'prs_at-aa_fx', gate: 'Always' }),
            particle({ id: 'd', systemRef: 'pgw_grav_well', gate: 'Always' }),
            particle({ id: 'e', systemRef: 'pi_damage_elec_SD00', gate: 'Always' }),
        ]));

        assert.deepEqual(groups.map(group => group.label), [
            'Turbo engines', 'Power to weapons', 'Missile shield', 'Gravity well',
            'Ion stun',
        ]);
    });

    it('never reads pte_ as pe_', () => {
        // The Corvette and the Tartan carry both. Matching on the leading characters rather than
        // the whole marker token would file every turbo effect under Engines.
        const groups = particleGroups(context([
            particle({ id: 'a', systemRef: 'PE_Corvetteengines', gate: 'Always' }),
            particle({ id: 'b', systemRef: 'PTE_Corvetteengines', gate: 'Always' }),
        ]));

        assert.deepEqual(groups.find(g => g.label === 'Engines')?.ids, ['a']);
        assert.deepEqual(groups.find(g => g.label === 'Turbo engines')?.ids, ['b']);
    });

    it('gathers what it cannot name under its own prefix, at two or more', () => {
        // A mod's effects, or a shipped family nobody has named yet. The prefix is all that can be
        // honestly said about them, so it is what the heading says.
        const groups = particleGroups(context([
            particle({ id: 'a', systemRef: 'p_krayt_roar', gate: 'Always' }),
            particle({ id: 'b', systemRef: 'p_krayt_dust', gate: 'Always' }),
        ]));

        const other = groups.find(group => group.source === 'prefix');
        assert.deepEqual(other?.ids.sort(), ['a', 'b']);
        assert.match(other!.label, /krayt/i);
    });

    it('drops an UNNAMED group of one, which says nothing its row does not', () => {
        // The floor stays for these. `Pas_sprint` alone is one system and one row; a heading over
        // it that can only repeat its prefix is the second list the tree was meant to replace.
        assert.deepEqual(particleGroups(context([
            particle({ id: 'a', systemRef: 'p_lonely', gate: 'Always' }),
        ])), []);
    });

    it('offers no catch-all for the effects that are simply always on', () => {
        // `Ambient` was every ungated system on the model under one heading - which on the Tartan
        // was all seven of them, engines and ability effects together. A group that means "the
        // rest" is not a bulk switch anyone reaches for.
        const groups = particleGroups(context([
            particle({ id: 'a', systemRef: 'pe_x', gate: 'Always' }),
            particle({ id: 'b', systemRef: 'pptw_y', gate: 'Always' }),
        ]));

        assert.equal(groups.some(group => /ambient/i.test(group.label)), false);
    });

    it('keeps the families in a fixed order, so the list never shuffles', () => {
        const groups = particleGroups(context([
            particle({ id: 'a', systemRef: 'pi_damage_elec_SD00', gate: 'Always' }),
            particle({ id: 'b', systemRef: 'pe_victoryengines', gate: 'HardpointAlive' }),
            particle({ id: 'c', systemRef: 'p_hp_imperial_damage', gate: 'HardpointDestroyed' }),
        ]));

        assert.deepEqual(groups.map(group => group.label),
            ['Engines', 'Hardpoint damage', 'Ion stun']);
    });

    it('says nothing about an empty scene', () => {
        assert.deepEqual(particleGroups(context([])), []);
    });

    it('keys groups stably, so a reload does not reset the panel', () => {
        const particles = [
            particle({ id: 'a', gate: 'HardpointDestroyed' }),
            particle({ id: 'b', gate: 'HardpointDestroyed' }),
        ];

        assert.deepEqual(
            particleGroups(context(particles)).map(group => group.id),
            particleGroups(context(particles)).map(group => group.id));
    });
});

describe('groupState', () => {
    const group = { id: 'family:engines', label: 'Engines', source: 'family' as const, ids: ['a', 'b'] };

    it('reads all when every system in it is shown', () => {
        assert.equal(groupState(group, new Set(['a', 'b'])), 'all');
    });

    it('reads none when not one is', () => {
        assert.equal(groupState(group, new Set()), 'none');
    });

    it('reads some when the group is split', () => {
        // The state that makes the panel a VIEW of the tree rather than a second source of truth:
        // switching one system off in the tree has to show up here as a mixed group.
        assert.equal(groupState(group, new Set(['a'])), 'some');
    });

    it('reads none for a group with nothing in it', () => {
        assert.equal(groupState({ ...group, ids: [] }, new Set(['a'])), 'none');
    });
});

describe('describeGate', () => {
    it('says what switches each kind on, in words rather than a code', () => {
        assert.match(describeGate('HardpointDestroyed', 'HP_Left'), /HP_Left/);
        assert.match(describeGate('HardpointDestroyed', 'HP_Left'), /destroyed/i);
        assert.match(describeGate('HardpointAlive', 'HP_Engine'), /HP_Engine/);
        assert.equal(describeGate('Always', null), '');
    });
});

describe('listedInModelTree', () => {
    it('lists an effect the model itself declares', () => {
        // A proxy on a bone. It is part of the asset, so it is part of the tree of the asset.
        assert.equal(listedInModelTree('model'), true);
    });

    it('keeps a gameplay effect out of it', () => {
        // The reported fault: `p_explosion_big00` appeared under Root on a Nebulon-B, and ticking it
        // did nothing. A death explosion and a wreck's trailing fire are asked for BY NAME at the
        // moment they are needed, so they are in no proxy list and hang off no bone - `ownerOf`
        // falls back to the root bone and files them there. They are what the game DOES to the
        // model, not what the model is made of.
        assert.equal(listedInModelTree('gameplay'), false);
    });
});

// TURBO on the CR90, reported as "not toggleable". The key was enabled the whole time and the turbo
// plume did light; what never happened is the plain engine plume going OUT, so both burned at once
// out of the same nozzles and the press looked like it did nothing.
//
// The user owns this rule: turbo shows the PTE_ (power to engine) effect and HIDES the normal engine
// effect. Only that pair - whether power-to-weapons replaces anything is not known and is not
// assumed here.
describe('replacedByAbility', () => {
    const engines = particle({ id: 'pe', systemRef: 'PE_Corvetteengines', gate: 'Always' });
    const turbo = particle({ id: 'pte', systemRef: 'PTE_Corvetteengines', gate: 'Always' });
    const proxies = new Map([['TURBO', ['pte']]]);

    it('holds the plain engines back while turbo is active', () => {
        const held = replacedByAbility([engines, turbo], proxies, new Set(['TURBO']));

        assert.deepEqual([...held], ['pe']);
    });

    it('holds nothing back while turbo is off', () => {
        // The whole scene at rest: the engines are the one effect exempt from quiet-on-open, and
        // suppressing them with no ability active would put the ship in space with dead nozzles.
        assert.deepEqual([...replacedByAbility([engines, turbo], proxies, new Set())], []);
    });

    it('never holds back the effect doing the replacing', () => {
        const held = replacedByAbility([engines, turbo], proxies, new Set(['TURBO']));

        assert.equal(held.has('pte'), false);
    });

    it('needs the replacing family to be what the ability actually drives', () => {
        // An active ability claiming something else entirely must not silence the engines. The
        // Tartan carries pe_, pte_ AND pptw_ together, so this is a real arrangement, not a guard
        // against an imaginary one.
        const weapons = particle({ id: 'pptw', systemRef: 'pptw_ptwsa', gate: 'Always' });
        const held = replacedByAbility(
            [engines, turbo, weapons], new Map([['POWER_TO_WEAPONS', ['pptw']]]),
            new Set(['POWER_TO_WEAPONS']));

        assert.deepEqual([...held], []);
    });

    it('replaces only the pair the user named', () => {
        // Power to weapons replaces nothing until someone says otherwise - see the note above.
        const weapons = particle({ id: 'pptw', systemRef: 'pptw_ptwsa', gate: 'Always' });
        const guns = particle({ id: 'pw', systemRef: 'pe_guns', gate: 'HardpointAlive' });
        const held = replacedByAbility(
            [weapons, guns], new Map([['POWER_TO_WEAPONS', ['pptw']]]),
            new Set(['POWER_TO_WEAPONS']));

        assert.deepEqual([...held], []);
    });

    it('holds back every member of the replaced family, not just the first', () => {
        // Home One carries three engine systems and the Tartan two.
        const lrg = particle({ id: 'lrg', systemRef: 'pe_tartanengine_lrg', gate: 'Always' });
        const sml = particle({ id: 'sml', systemRef: 'pe_tartanengine_sml', gate: 'Always' });
        const held = replacedByAbility(
            [lrg, sml, turbo], proxies, new Set(['TURBO']));

        assert.deepEqual([...held].sort(), ['lrg', 'sml']);
    });
});

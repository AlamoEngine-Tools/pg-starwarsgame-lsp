// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import type { PreviewHardpoint, PreviewParticle } from '../../protocol/modelPreview';
import {
    describeGate, groupState, particleGroups, type GroupingContext,
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

describe('particleGroups', () => {
    it('gathers engine-gated systems under one role group', () => {
        const groups = particleGroups(context([
            particle({ id: 'a', systemRef: 'p_engine_glow', gate: 'HardpointAlive' }),
            particle({ id: 'b', systemRef: 'p_engine_wash', gate: 'HardpointAlive' }),
        ]));

        const engines = groups.find(group => group.label === 'Engines');
        assert.notEqual(engines, undefined);
        assert.deepEqual(engines!.ids.sort(), ['a', 'b']);
        assert.equal(engines!.source, 'role');
    });

    it('gathers destruction-gated systems under hardpoint damage', () => {
        const groups = particleGroups(context([
            particle({ id: 'a', gate: 'HardpointDestroyed', hardpointId: 'HP_Left' }),
            particle({ id: 'b', gate: 'HardpointDestroyed', hardpointId: 'HP_Right' }),
        ]));

        const damage = groups.find(group => group.label === 'Hardpoint damage');
        assert.notEqual(damage, undefined);
        assert.deepEqual(damage!.ids.sort(), ['a', 'b']);
    });

    it('gathers ungated systems under ambient', () => {
        const groups = particleGroups(context([
            particle({ id: 'a', systemRef: 'p_dust' }),
            particle({ id: 'b', systemRef: 'p_steam' }),
        ]));

        assert.notEqual(groups.find(group => group.label === 'Ambient'), undefined);
    });

    it('collapses the twenty copies of one system into a group named for it', () => {
        // A Star Destroyer carries twenty p_hp_imperial_damage proxies. Switching all of them at
        // once is the affordance this panel exists for.
        const groups = particleGroups(context(
            Array.from({ length: 20 }, (_, i) =>
                particle({ id: `hull#${i}`, gate: 'HardpointDestroyed' }))));

        const byName = groups.find(group => group.label === 'p_hp_imperial_damage');
        assert.notEqual(byName, undefined);
        assert.equal(byName!.ids.length, 20);
        assert.equal(byName!.source, 'name');
    });

    it('lists a system in every group it belongs to, because groups overlap', () => {
        // The whole reason grouping is a bulk switch rather than a second tree: one damage effect is
        // both "hardpoint damage" and "p_hp_imperial_damage", and neither reading is wrong.
        const groups = particleGroups(context([
            particle({ id: 'a', gate: 'HardpointDestroyed' }),
            particle({ id: 'b', gate: 'HardpointDestroyed' }),
        ]));

        const holding = groups.filter(group => group.ids.includes('a')).map(group => group.label);
        assert.deepEqual(holding.sort(), ['Hardpoint damage', 'p_hp_imperial_damage']);
    });

    it('drops a group of one, which is not a bulk switch', () => {
        // One system in a group says exactly what its own tree row already says, and a panel of
        // those is the second list the tree was meant to replace.
        const groups = particleGroups(context([
            particle({ id: 'a', systemRef: 'p_lonely', gate: 'HardpointAlive' }),
        ]));

        assert.deepEqual(groups, []);
    });

    it('orders role groups before name groups so the list does not shuffle', () => {
        const groups = particleGroups(context([
            particle({ id: 'a', systemRef: 'p_zzz', gate: 'HardpointAlive' }),
            particle({ id: 'b', systemRef: 'p_zzz', gate: 'HardpointAlive' }),
            particle({ id: 'c', systemRef: 'p_aaa', gate: 'HardpointAlive' }),
            particle({ id: 'd', systemRef: 'p_aaa', gate: 'HardpointAlive' }),
        ]));

        assert.deepEqual(groups.map(group => group.label), ['Engines', 'p_aaa', 'p_zzz']);
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
    const group = { id: 'role:engines', label: 'Engines', source: 'role' as const, ids: ['a', 'b'] };

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

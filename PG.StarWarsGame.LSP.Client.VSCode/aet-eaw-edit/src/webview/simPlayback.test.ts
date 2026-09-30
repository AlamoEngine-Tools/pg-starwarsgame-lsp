// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import {describe, it} from 'node:test';

import {StorySimStepDto} from '../protocol/story';
import {Adjacent, fireDelta, groupByTick, isLifecycleStep, resolvePath} from './simPlayback';

function step(tick: number, seq: number, nodeId: string, to: string | null, cause: string, source: string | null = null): StorySimStepDto {
    return {tick, seq, nodeId, from: null, to, sourceNodeId: source, cause, detail: null};
}

describe('groupByTick', () => {
    it('keeps trace order and splits on tick changes', () => {
        const groups = groupByTick([
            step(0, 0, 'a', 'Armed', 'load'), step(0, 1, 'b', 'Armed', 'load'),
            step(1, 2, 'a', 'Fired', 'poll'), step(3, 3, 'b', 'Fired', 'poll'),
        ]);

        assert.deepEqual(groups.map(g => g.map(s => s.seq)), [[0, 1], [2], [3]]);
        assert.deepEqual(groups.map(g => g[0].tick), [0, 1, 3]);
    });

    it('returns nothing for an empty delta', () => {
        assert.deepEqual(groupByTick([]), []);
    });
});

describe('resolvePath', () => {
    const adjacency = new Map<string, Adjacent[]>([
        ['a', [{to: 'a#g0', connectionId: 'c1'}, {to: 'x', connectionId: 'c9'}]],
        ['a#g0', [{to: 'b', connectionId: 'c2'}]],
        ['x', [{to: 'b', connectionId: 'c10'}]],
        ['b', [{to: 'c', connectionId: 'c3'}]],
    ]);
    const junctions = new Set(['a#g0']);

    it('walks through a junction but never through another event', () => {
        assert.deepEqual(resolvePath('a', 'b', adjacency, id => junctions.has(id)), ['c1', 'c2']);
    });

    it('takes a direct control edge when there is one', () => {
        assert.deepEqual(resolvePath('b', 'c', adjacency, id => junctions.has(id)), ['c3']);
    });

    it('is empty when the target is unreachable within the hop limit', () => {
        assert.deepEqual(resolvePath('a', 'c', adjacency, id => junctions.has(id), 2), []);
        assert.deepEqual(resolvePath('c', 'a', adjacency, id => junctions.has(id)), []);
    });
});

describe('fireDelta', () => {
    it('counts fires by cause and ignores arming, resets and notes', () => {
        const delta = fireDelta([
            step(0, 0, 'a', 'Armed', 'load'),
            step(1, 1, 'a', 'Fired', 'poll'),
            step(1, 2, 'b', 'Armed', 'prereq', 'a'),
            step(1, 3, 'b', 'Fired', 'prereq', 'a'),
            step(1, 4, 'b', null, 'flag'),
            step(2, 5, 'b', 'Waiting', 'reset', 'r'),
            step(2, 6, 'b', 'Fired', 'trigger', 'r'),
        ]);

        assert.deepEqual([...delta.entries()], [['a', 1], ['b', 2]]);
    });
});

describe('isLifecycleStep', () => {
    it('is false for a note and true for a transition', () => {
        assert.equal(isLifecycleStep(step(1, 0, 'a', null, 'flag')), false);
        assert.equal(isLifecycleStep(step(1, 0, 'a', 'Fired', 'poll')), true);
    });
});

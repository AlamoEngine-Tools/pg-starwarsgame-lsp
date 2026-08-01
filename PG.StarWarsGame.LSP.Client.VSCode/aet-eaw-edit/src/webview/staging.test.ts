// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// staging.ts was written framework-free so it could be tested, and then never was. These pin the
// behaviour the story editor's Edit mode depends on before a second editor starts copying the
// pattern - a wrong optimistic patch shows the user an edit that will not survive Save.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import {
    eventNodeId, optimisticEdit, PatchableEventNode, PREVIEW_KINDS, STAGED_KINDS, upsertParams,
} from './staging';

describe('eventNodeId', () => {
    // Must match the server's StoryGraphBuilder.EventNodeId, which lowercases - a mismatch means
    // patchEventNode silently finds no node and the edit appears to do nothing.
    it('lowercases the event name', () => {
        assert.equal(eventNodeId('file:///a.xml', 'Intro_Event'), 'file:///a.xml#intro_event');
    });

    it('treats a null thread uri as an empty prefix', () => {
        assert.equal(eventNodeId(null, 'Evt'), '#evt');
        assert.equal(eventNodeId(undefined, 'Evt'), '#evt');
    });
});

describe('upsertParams', () => {
    it('adds a slot and keeps positions sorted', () => {
        const result = upsertParams(
            [{ position: 2, value: 'two' }],
            [{ position: 0, value: 'zero' }],
        );

        assert.deepEqual(result, [
            { position: 0, value: 'zero' },
            { position: 2, value: 'two' },
        ]);
    });

    it('overwrites the value at an existing position', () => {
        const result = upsertParams(
            [{ position: 1, value: 'old' }],
            [{ position: 1, value: 'new' }],
        );

        assert.deepEqual(result, [{ position: 1, value: 'new' }]);
    });

    // Clearing a param slot is how a user removes an argument; an empty string must delete the slot
    // rather than write an empty argument the server would then have to interpret.
    it('removes a slot when the value is empty or null', () => {
        assert.deepEqual(
            upsertParams([{ position: 0, value: 'a' }], [{ position: 0, value: '' }]),
            [],
        );
        assert.deepEqual(
            upsertParams([{ position: 0, value: 'a' }], [{ position: 0, value: null }]),
            [],
        );
    });

    it('leaves the original list untouched', () => {
        const existing = [{ position: 0, value: 'a' }];
        upsertParams(existing, [{ position: 1, value: 'b' }]);

        assert.deepEqual(existing, [{ position: 0, value: 'a' }]);
    });
});

describe('optimisticEdit', () => {
    it('returns null when the payload names no event', () => {
        assert.equal(optimisticEdit({ kind: 'setBranch', value: 'x' }), null);
    });

    // Structural gestures reconcile through a server preview instead; claiming a local patch for
    // them would show the user a graph the preview then contradicts.
    it('returns null for a structural kind', () => {
        assert.equal(optimisticEdit({ kind: 'createEvent', eventName: 'E' }), null);
    });

    it('sets branch, and clears it when the value is empty', () => {
        const set = optimisticEdit({ kind: 'setBranch', eventName: 'E', value: 'Rebel' });
        assert.deepEqual(set!.apply({ branch: null }), { branch: 'Rebel' });

        const cleared = optimisticEdit({ kind: 'setBranch', eventName: 'E', value: '' });
        assert.deepEqual(cleared!.apply({ branch: 'Rebel' }), { branch: null });
    });

    // Anything other than a literal true is false - the flag arrives from an untyped webview
    // message, so a truthy string must not be read as "on".
    it('treats perpetual as a strict boolean', () => {
        const node: PatchableEventNode = { perpetual: false };

        const on = optimisticEdit({ kind: 'setPerpetual', eventName: 'E', flag: true });
        assert.equal(on!.apply(node).perpetual, true);

        const off = optimisticEdit({ kind: 'setPerpetual', eventName: 'E', flag: 'yes' });
        assert.equal(off!.apply(node).perpetual, false);
    });

    // Clearing a type must drop its params too - leaving them would show arguments belonging to a
    // type the node no longer has.
    it('clears the event type and its params together', () => {
        const edit = optimisticEdit({ kind: 'clearEventType', eventName: 'E' });
        const next = edit!.apply({ eventType: 'T', eventParams: [{ position: 0, value: 'a' }] });

        assert.equal(next.eventType, null);
        assert.deepEqual(next.eventParams, []);
    });

    it('routes setParams to the event or reward list by paramKind', () => {
        const reward = optimisticEdit({
            kind: 'setParams', eventName: 'E', paramKind: 'reward',
            params: [{ position: 0, value: 'r' }],
        });
        const next = reward!.apply({ eventParams: [], rewardParams: [] });

        assert.deepEqual(next.rewardParams, [{ position: 0, value: 'r' }]);
        assert.deepEqual(next.eventParams, []);
    });

    it('carries the node id derived from the payload', () => {
        const edit = optimisticEdit({ kind: 'setBranch', eventName: 'Evt', threadUri: 'u', value: 'b' });

        assert.equal(edit!.nodeId, 'u#evt');
    });
});

describe('kind sets', () => {
    it('stages every preview kind as well as the property kinds', () => {
        for (const kind of PREVIEW_KINDS) {
            assert.ok(STAGED_KINDS.has(kind), `${kind} must be staged`);
        }
        assert.ok(STAGED_KINDS.has('setBranch'));
    });

    // Nothing may reach disk outside Save; an unstaged kind would write immediately.
    it('does not stage an unknown kind', () => {
        assert.equal(STAGED_KINDS.has('somethingElse'), false);
    });
});

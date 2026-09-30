// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import {describe, it} from 'node:test';
import type {StoryGraphNodeDto} from '../../protocol/story';
import {editModeBlockedBy, readOnlyMessage, readOnlyOwnerOf, readOnlyThreadIndex} from './dependencyEdit';

function node(id: string, threadUri: string | null, owner?: string | null): StoryGraphNodeDto {
    return {id, kind: 'Event', label: id, threadUri, reachable: true, readOnlyOwner: owner};
}

describe('readOnlyThreadIndex', () => {
    it('maps a thread to the referenced project that owns it', () => {
        const index = readOnlyThreadIndex([node('a', 'file:///core/story.xml', 'EaWX Core')]);

        assert.equal(index.get('file:///core/story.xml'), 'EaWX Core');
    });

    it('leaves an editable thread out, so a lookup on it finds nothing', () => {
        const index = readOnlyThreadIndex([
            node('a', 'file:///rev/story.xml', null),
            node('b', 'file:///core/story.xml', 'EaWX Core'),
        ]);

        assert.equal(index.has('file:///rev/story.xml'), false);
        assert.equal(index.size, 1);
    });

    // A node with no thread of its own - a portal, a junction the server placed outside a file -
    // says nothing about any thread, and keying it under the empty string would block every
    // command whose payload happens to omit the thread.
    it('ignores a node that has no thread', () => {
        assert.equal(readOnlyThreadIndex([node('a', null, 'EaWX Core')]).size, 0);
    });
});

describe('readOnlyOwnerOf', () => {
    it('is null for a thread the leaf owns', () => {
        const index = readOnlyThreadIndex([node('a', 'file:///core/story.xml', 'EaWX Core')]);

        assert.equal(readOnlyOwnerOf(index, 'file:///rev/story.xml'), null);
    });

    it('names the owner for a referenced thread', () => {
        const index = readOnlyThreadIndex([node('a', 'file:///core/story.xml', 'EaWX Core')]);

        assert.equal(readOnlyOwnerOf(index, 'file:///core/story.xml'), 'EaWX Core');
    });

    // A command that carries no thread cannot be judged here; the server decides.
    it('is null when there is no thread to judge', () => {
        const index = readOnlyThreadIndex([node('a', 'file:///core/story.xml', 'EaWX Core')]);

        assert.equal(readOnlyOwnerOf(index, undefined), null);
        assert.equal(readOnlyOwnerOf(index, null), null);
    });
});

describe('editModeBlockedBy', () => {
    // The whole point, and the thing three rounds of per-node affordances did not deliver: a graph
    // you cannot edit must not let you into Edit mode at all. Disabling the fields on each node
    // still leaves the CANVAS taking drops, and the error arrives after the gesture.
    it('blocks Edit when every thread belongs to a referenced project', () => {
        const blocked = editModeBlockedBy([
            node('a', 'file:///core/one.xml', 'EaWX Core'),
            node('b', 'file:///core/two.xml', 'EaWX Core'),
        ]);

        assert.ok(blocked);
        assert.match(blocked, /EaWX Core/);
    });

    it('names every owner when more than one referenced project is in play', () => {
        const blocked = editModeBlockedBy([
            node('a', 'file:///core/one.xml', 'EaWX Core'),
            node('b', 'file:///fx/two.xml', 'EaWX Effects'),
        ]);

        assert.match(blocked ?? '', /EaWX Core/);
        assert.match(blocked ?? '', /EaWX Effects/);
    });

    // A MIXED graph blocks too, and it is the worse case rather than the easier one: the canvas
    // takes a drop wherever it is aimed, a new event lands in whichever thread is nearest, and a
    // prereq drawn to a referenced event writes to THAT event's file. None of that can be enforced
    // by disabling controls on the nodes that happen to be read-only.
    it('blocks Edit when only PART of the graph is read-only', () => {
        const blocked = editModeBlockedBy([
            node('a', 'file:///core/one.xml', 'EaWX Core'),
            node('b', 'file:///rev/two.xml', null),
        ]);

        assert.ok(blocked);
        assert.match(blocked, /Part of this graph/);
        assert.match(blocked, /EaWX Core/);
    });

    it('allows Edit for an ordinary single-project graph', () => {
        assert.equal(editModeBlockedBy([node('a', 'file:///rev/one.xml', null)]), null);
    });

    // Junctions and portals are drawn from the events around them and own no file, so a graph of
    // nothing but those says nothing either way - and must not be reported as read-only.
    it('says nothing about a graph with no threads at all', () => {
        assert.equal(editModeBlockedBy([node('a', null, null)]), null);
        assert.equal(editModeBlockedBy([]), null);
    });
});

describe('readOnlyMessage', () => {
    it('names the owner and says where the edit belongs', () => {
        const message = readOnlyMessage('EaWX Core');

        assert.ok(message.includes('EaWX Core'));
        // Straight quotes and ASCII only - this is user-facing text.
        assert.match(message, /^[\x20-\x7e]+$/);
    });
});

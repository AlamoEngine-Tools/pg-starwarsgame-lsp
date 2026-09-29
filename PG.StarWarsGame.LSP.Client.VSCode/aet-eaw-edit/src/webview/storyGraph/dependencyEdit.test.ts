// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import {describe, it} from 'node:test';
import type {StoryGraphNodeDto} from '../../protocol/story';
import {readOnlyMessage, readOnlyOwnerOf, readOnlyThreadIndex} from './dependencyEdit';

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

describe('readOnlyMessage', () => {
    it('names the owner and says where the edit belongs', () => {
        const message = readOnlyMessage('EaWX Core');

        assert.ok(message.includes('EaWX Core'));
        // Straight quotes and ASCII only - this is user-facing text.
        assert.match(message, /^[\x20-\x7e]+$/);
    });
});

// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import {describe, it} from 'node:test';

import {noteBadge, worstNote} from './typeNotes';

describe('worstNote', () => {
    // The server ranks before sending, so the first note is the worst one. Trusting that is the
    // point: the client does not get to disagree with the schema about what matters most.
    it('takes the first note, which is the worst the schema knows', () => {
        const note = worstNote([
            {kind: 'BuggedInEngine', text: 'Does nothing.'},
            {kind: 'Remark', text: 'Rare.'},
        ]);

        assert.equal(note?.kind, 'BuggedInEngine');
    });

    it('has nothing to say about a type with no notes', () => {
        assert.equal(worstNote([]), null);
        assert.equal(worstNote(undefined), null);
    });
});

describe('noteBadge', () => {
    it('marks an engine bug as the loudest thing a node can carry', () => {
        const badge = noteBadge([{kind: 'BuggedInEngine', text: 'The engine ignores this.'}]);

        assert.equal(badge?.className, 'note-bugged');
        assert.match(badge!.title, /does not work/i);
        assert.match(badge!.title, /The engine ignores this\./);
    });

    it('keeps the older dashed-border look for an untested type', () => {
        assert.equal(noteBadge([{kind: 'Untested'}])?.className, 'untested');
    });

    it('marks a deprecated type', () => {
        assert.equal(noteBadge([{kind: 'Deprecated'}])?.className, 'note-deprecated');
    });

    // A remark is worth reading on hover and is not worth changing how a node looks: every node
    // carrying one would make the graph noisier without telling anyone anything actionable.
    it('gives a remark a tooltip but no styling', () => {
        const badge = noteBadge([{kind: 'Remark', text: 'Rarely used.'}]);

        assert.equal(badge?.className, '');
        assert.match(badge!.title, /Rarely used\./);
    });

    // Since says when something appeared. On a node it is pure noise.
    it('ignores a version note', () => {
        assert.equal(noteBadge([{kind: 'Since', value: 'FoC 1.1'}]), null);
    });

    it('has nothing for a type with no notes', () => {
        assert.equal(noteBadge([]), null);
    });

    // A kind this build has never heard of must not crash the graph or vanish silently: the schema
    // may be newer than the extension, and the reader is better off seeing the words.
    it('still shows an unknown kind it cannot classify', () => {
        const badge = noteBadge([{kind: 'SomethingNew', text: 'Who knows.'}]);

        assert.equal(badge?.className, '');
        assert.match(badge!.title, /Who knows\./);
    });

    it('falls back to the kind when a note carries no words', () => {
        assert.match(noteBadge([{kind: 'Deprecated'}])!.title, /deprecated/i);
    });
});

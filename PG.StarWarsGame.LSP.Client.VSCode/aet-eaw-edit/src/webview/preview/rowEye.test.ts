// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { rowEye, type EyeFacts } from './rowEye';

describe('rowEye, the three states of the tree`s eye', () => {
    const row = (over: Partial<EyeFacts> = {}): EyeFacts =>
        ({ visible: true, decidedBy: 'file', authored: true, ...over });

    it('is an OPEN eye for a row that is drawn', () => {
        assert.equal(rowEye(row({ visible: true })).state, 'shown');
    });

    it('is a CLOSED eye for a row the reader hid', () => {
        assert.equal(rowEye(row({ visible: false, decidedBy: 'you' })).state, 'hidden');
    });

    it('is a closed eye for a row the FILE or the level hides', () => {
        // The row's own state, whoever set it. What matters for the glyph is that this control can
        // change it: clicking a file-hidden collision hull shows it, which is exactly what the
        // reader reaches for.
        for (const decidedBy of ['file', 'level', 'animation', 'idle'] as const) {
            assert.equal(rowEye(row({ visible: false, decidedBy })).state, 'hidden', decidedBy);
        }
    });

    it('is a HALF eye when something above the row decided', () => {
        // The user's "visibility inherited". A hidden ancestor and a master toggle are not this
        // row's state and this control cannot change them - which is the whole difference between
        // the half eye and the closed one.
        assert.equal(rowEye(row({ visible: false, decidedBy: 'ancestor' })).state, 'inherited');
        assert.equal(rowEye(row({ visible: false, decidedBy: 'master' })).state, 'inherited');
    });

    it('leaves the half eye INERT, because pressing it could not do anything', () => {
        // Disable-don`t-hide: the control stays on the row, and its title says what would make it
        // available. Writing an override here would store a word the chain outranks anyway, so the
        // button would look broken.
        assert.equal(rowEye(row({ visible: false, decidedBy: 'ancestor' })).canAct, false);
        assert.equal(rowEye(row({ visible: false, decidedBy: 'file' })).canAct, true);
        assert.equal(rowEye(row({ visible: true })).canAct, true);
    });

    it('titles the half eye with the STATE and nothing more', () => {
        // The row's own tooltip already carries `becauseText`, which names the ancestor. Saying it
        // again here made a title that had to be read rather than glanced at.
        assert.equal(
            rowEye(row({ visible: false, decidedBy: 'ancestor' })).title,
            'State inherited from parent');
    });

    it('says what pressing it will do, in the two states where it does anything', () => {
        assert.equal(rowEye(row({ visible: true })).title, 'Hide this and everything under it');
        assert.equal(rowEye(row({ visible: false })).title, 'Show this and everything under it');
    });

    it('marks a row that stands against the model, in either direction', () => {
        // What the italics already say, carried on the same object so the row has one source for
        // how it looks. A shadow volume the reader asked for is drawn and NOT authored; a mesh they
        // hid is undrawn and authored.
        assert.equal(rowEye(row({ visible: true, authored: false })).againstModel, true);
        assert.equal(rowEye(row({ visible: false, decidedBy: 'you', authored: true })).againstModel,
            true);
        assert.equal(rowEye(row({ visible: true, authored: true })).againstModel, false);
    });
});

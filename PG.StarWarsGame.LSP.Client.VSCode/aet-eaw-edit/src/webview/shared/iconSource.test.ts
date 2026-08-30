// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { CODICON_NAMES, codiconFor } from './iconSource';

describe('codiconFor', () => {
    /**
     * The rule: an action the EDITOR already has a glyph for keeps the editor's glyph.
     *
     * Tabler was picked for this panel because codicons has no 3D vocabulary - no ground plane, no
     * skeleton, no LOD, no axes. That argument covers the domain and stops there. "Go to where this
     * is defined" is an editor action the story graph already draws as `go-to-file` in three places,
     * and answering it with a different picture here makes one action look like two.
     */
    it('uses the editor`s own glyph for going to a definition', () => {
        assert.equal(codiconFor('definition'), 'go-to-file');
    });

    it('leaves the domain icons to Tabler', () => {
        // These are the reason this panel does not simply use codicons throughout.
        for (const name of ['skeleton', 'mesh', 'detail', 'axes', 'ground'] as const) {
            assert.equal(codiconFor(name), null, name);
        }
    });

    it('names every meaning it answers, so the set can be read at a glance', () => {
        for (const name of CODICON_NAMES) {
            assert.notEqual(codiconFor(name), null, name);
        }
    });
});

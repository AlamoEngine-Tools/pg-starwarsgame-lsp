// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { EMPTY_SUBJECT_STATE, subjectStateFrom } from './subjectState';

describe('subjectStateFrom', () => {
    it('gives an empty state for a subject never opened this session', () => {
        assert.deepEqual(subjectStateFrom(undefined), EMPTY_SUBJECT_STATE);
        assert.deepEqual(subjectStateFrom(null), EMPTY_SUBJECT_STATE);
    });

    it('keeps what the reader had set', () => {
        const stored = subjectStateFrom({
            alt: 2, lod: 3, destroyed: ['HP_F-L'], hiddenEmitters: [0, 2], animation: 'fly_00',
        });

        assert.equal(stored.alt, 2);
        assert.equal(stored.lod, 3);
        assert.deepEqual(stored.destroyed, ['HP_F-L']);
        assert.deepEqual(stored.hiddenEmitters, [0, 2]);
        assert.equal(stored.animation, 'fly_00');
    });

    /** Same reasoning as the viewer settings: one bad value costs its own control, not the panel. */
    it('refuses values of the wrong shape rather than passing them on', () => {
        const nonsense = subjectStateFrom({
            alt: 'two', lod: null, destroyed: 'HP_F-L', hiddenEmitters: [0, 'x'], animation: 7,
        });

        assert.deepEqual(nonsense, EMPTY_SUBJECT_STATE);
    });

    it('survives a stored value that is not an object', () => {
        for (const junk of ['', 7, [], true]) {
            assert.deepEqual(subjectStateFrom(junk), EMPTY_SUBJECT_STATE);
        }
    });

    /**
     * Meshes are on until switched off, so recording the hidden ones round-trips them. Particle
     * systems are the other way up: a model opens quiet, so almost every system starts off and it is
     * switching one ON that the reader will expect to find again. Recording only `hidden` lost every
     * effect anyone had deliberately lit.
     */
    it('keeps the rows switched on by hand as well as the ones switched off', () => {
        const stored = subjectStateFrom({
            hidden: ['mesh:hull:0:0'], shown: ['particle:hull#3'],
        });

        assert.deepEqual(stored.hidden, ['mesh:hull:0:0']);
        assert.deepEqual(stored.shown, ['particle:hull#3']);
    });

    /**
     * A level the model does not have would show nothing at all. The state is per subject, but the
     * subject can be EDITED between one open and the next - a mesh's `_ALT2` suffix removed - and
     * restoring a level that no longer exists is indistinguishable from a broken model.
     */
    it('drops a level the model no longer defines', () => {
        const stored = subjectStateFrom({ alt: 2, lod: 3 });

        assert.deepEqual(levelsWithin(stored, { alt: [0, 1], lod: [0, 1, 2, 3] }),
            { alt: null, lod: 3 });
    });

    it('keeps a level the model still defines', () => {
        const stored = subjectStateFrom({ alt: 1, lod: 0 });

        assert.deepEqual(levelsWithin(stored, { alt: [0, 1], lod: [0, 1] }), { alt: 1, lod: 0 });
    });
});

/** Reads the two levels back, dropping either that the model no longer offers. */
function levelsWithin(
    state: { alt: number; lod: number },
    defined: { alt: number[]; lod: number[] },
): { alt: number | null; lod: number | null } {
    return {
        alt: defined.alt.includes(state.alt) ? state.alt : null,
        lod: defined.lod.includes(state.lod) ? state.lod : null,
    };
}

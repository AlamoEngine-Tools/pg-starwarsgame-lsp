// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { creditsBaselineValues } from './creditsBaselineFill';

// The game's own credits: the same line in several languages.
const baseline = [
    {
        key: 'HEADER',
        values: [{ language: 'ENGLISH', value: 'Lead Designer' }, { language: 'GERMAN', value: 'Chefdesigner' }],
    },
    {
        key: 'HEADER',
        values: [{ language: 'ENGLISH', value: 'Voice Cast' }, { language: 'GERMAN', value: 'Sprecher' }],
    },
    {
        key: 'CENTER',
        values: [{ language: 'ENGLISH', value: 'Petroglyph' }, { language: 'GERMAN', value: 'Petroglyph' }],
    },
];

/**
 * What a one-language file can and cannot be filled from - the rule the "Fill language" tile is
 * gated on.
 *
 * Matching by text means a line is identified by what it says in *another* language. That makes
 * these two cases opposites, which is not obvious from the outside and is why they are pinned here:
 * a language the file already has cannot be filled (its own text is the thing being replaced, and
 * there is nothing else to look the line up by), while a language it does not have yet can be
 * (the existing language's text is the lookup key).
 */
describe('creditsBaselineValues, on a file with one language', () => {
    // The reported dead end: on a GERMAN-only credits file the Fill dialog offered "From the game",
    // then reported "there is nothing left to fill in for GERMAN" - and always would have.
    it('can never fill the only language the file has', () => {
        const rows = [
            { index: 0, key: 'HEADER', values: [{ language: 'GERMAN', value: '' }] },
            { index: 1, key: 'CENTER', values: [{ language: 'GERMAN', value: 'Petroglyph' }] },
        ];

        assert.deepEqual(creditsBaselineValues('GERMAN', rows, baseline), []);
    });

    // The Add-language dialog's fill offer, which is why baselineFillCount stays available even
    // when the Fill tile is not.
    it('can fill a language the file does not have yet', () => {
        const rows = [
            { index: 0, key: 'HEADER', values: [{ language: 'ENGLISH', value: 'Voice Cast' }] },
        ];

        assert.deepEqual(creditsBaselineValues('GERMAN', rows, baseline), [
            { index: 0, key: 'HEADER', value: 'Sprecher' },
        ]);
    });
});

describe('creditsBaselineValues', () => {
    // Matched by the text, not by the key: a credits key is a formatting directive that hundreds of
    // rows share, so it identifies nothing.
    it('fills a row whose text the game already translates', () => {
        const rows = [
            { index: 0, key: 'HEADER', values: [{ language: 'ENGLISH', value: 'Voice Cast' }, { language: 'GERMAN', value: '' }] },
        ];

        assert.deepEqual(creditsBaselineValues('GERMAN', rows, baseline), [
            { index: 0, key: 'HEADER', value: 'Sprecher' },
        ]);
    });

    // A mod's own names are not in the game's credits and cannot be filled from them.
    it('leaves a line the game does not have alone', () => {
        const rows = [
            { index: 0, key: 'CENTER', values: [{ language: 'ENGLISH', value: 'My Mod Team' }, { language: 'GERMAN', value: '' }] },
        ];

        assert.deepEqual(creditsBaselineValues('GERMAN', rows, baseline), []);
    });

    it('never overwrites a translation the row already has', () => {
        const rows = [
            { index: 0, key: 'HEADER', values: [{ language: 'ENGLISH', value: 'Voice Cast' }, { language: 'GERMAN', value: 'Stimmen' }] },
        ];

        assert.deepEqual(creditsBaselineValues('GERMAN', rows, baseline), []);
    });

    it('matches the text case-insensitively and ignores surrounding space', () => {
        const rows = [
            { index: 0, key: 'HEADER', values: [{ language: 'ENGLISH', value: '  voice cast ' }, { language: 'GERMAN', value: '' }] },
        ];

        assert.equal(creditsBaselineValues('GERMAN', rows, baseline)[0]?.value, 'Sprecher');
    });

    // Matching on any language the row does carry, not just the first column - a row translated
    // into German but not French can still be filled from the game's French.
    it('matches on whichever language the row actually has', () => {
        const rows = [
            { index: 0, key: 'HEADER', values: [{ language: 'ENGLISH', value: '' }, { language: 'GERMAN', value: 'Sprecher' }] },
        ];

        assert.deepEqual(creditsBaselineValues('ENGLISH', rows, baseline), [
            { index: 0, key: 'HEADER', value: 'Voice Cast' },
        ]);
    });

    // The blank-line marker is not text to look up; a spacer stays a spacer.
    it('does not try to translate the blank-line marker', () => {
        const rows = [
            { index: 0, key: 'CENTER', values: [{ language: 'ENGLISH', value: '[TBL]' }, { language: 'GERMAN', value: '' }] },
        ];

        assert.deepEqual(creditsBaselineValues('GERMAN', rows, baseline), []);
    });

    it('is empty when the baseline has not loaded', () => {
        const rows = [
            { index: 0, key: 'HEADER', values: [{ language: 'ENGLISH', value: 'Voice Cast' }, { language: 'GERMAN', value: '' }] },
        ];

        assert.deepEqual(creditsBaselineValues('GERMAN', rows, []), []);
    });

    // A name spelled the same in both is still worth filling - it is what the game ships.
    it('fills a line the game translates identically', () => {
        const rows = [
            { index: 0, key: 'CENTER', values: [{ language: 'ENGLISH', value: 'Petroglyph' }, { language: 'GERMAN', value: '' }] },
        ];

        assert.equal(creditsBaselineValues('GERMAN', rows, baseline)[0]?.value, 'Petroglyph');
    });
});

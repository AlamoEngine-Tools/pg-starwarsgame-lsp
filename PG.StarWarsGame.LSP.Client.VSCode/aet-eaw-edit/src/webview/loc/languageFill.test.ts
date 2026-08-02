// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { languageFillValues } from './languageFill';

const rows = [
    { index: 0, key: 'TEXT_A', values: [{ language: 'ENGLISH', value: 'Alpha' }] },
    { index: 1, key: 'TEXT_B', values: [{ language: 'ENGLISH', value: 'Beta' }] },
    { index: 2, key: 'TEXT_MINE', values: [{ language: 'ENGLISH', value: 'My own key' }] },
];

const baseline = [
    { key: 'TEXT_A', values: [{ language: 'ENGLISH', value: 'Alpha' }, { language: 'GERMAN', value: 'Alfa' }] },
    { key: 'TEXT_B', values: [{ language: 'ENGLISH', value: 'Beta' }, { language: 'GERMAN', value: 'Beta DE' }] },
    { key: 'TEXT_UNUSED', values: [{ language: 'GERMAN', value: 'Nicht benutzt' }] },
];

describe('languageFillValues', () => {
    it('takes the new language from the baseline for the keys the file has', () => {
        assert.deepEqual(languageFillValues('GERMAN', rows, baseline), [
            { key: 'TEXT_A', value: 'Alfa' },
            { key: 'TEXT_B', value: 'Beta DE' },
        ]);
    });

    // The point of the feature: a key the game does not define cannot be filled, and inventing
    // something for it would be worse than leaving it visibly empty.
    it('leaves a key the baseline does not have alone', () => {
        const keys = languageFillValues('GERMAN', rows, baseline).map(v => v.key);

        assert.ok(!keys.includes('TEXT_MINE'));
    });

    // The baseline is the whole game - tens of thousands of keys. Only what this file actually
    // holds may be filled, or adding a language would silently import the entire game text.
    it('never adds a key the file does not have', () => {
        const keys = languageFillValues('GERMAN', rows, baseline).map(v => v.key);

        assert.ok(!keys.includes('TEXT_UNUSED'));
    });

    it('skips a baseline value that is blank', () => {
        const withBlank = [
            { key: 'TEXT_A', values: [{ language: 'GERMAN', value: '' }] },
        ];

        assert.deepEqual(languageFillValues('GERMAN', rows, withBlank), []);
    });

    // Language identifiers reach us upper-cased from the dialog and from the server, but a file's
    // own heading may be spelled differently; the game reads one column per language regardless.
    it('matches the language case-insensitively', () => {
        assert.equal(languageFillValues('german', rows, baseline).length, 2);
    });

    it('is empty when the baseline has not loaded', () => {
        assert.deepEqual(languageFillValues('GERMAN', rows, []), []);
    });

    // A duplicate key in a malformed file must not produce two conflicting fills for one entry -
    // the keyed command vocabulary addresses by key, so the second would simply overwrite the first.
    it('yields one value per key even if a row repeats', () => {
        const duplicated = [...rows, { index: 3, key: 'TEXT_A', values: [] }];

        assert.equal(
            languageFillValues('GERMAN', duplicated, baseline).filter(v => v.key === 'TEXT_A').length,
            1);
    });
});

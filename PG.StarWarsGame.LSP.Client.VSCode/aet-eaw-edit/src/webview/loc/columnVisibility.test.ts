// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { defaultHiddenLanguages, emptyLanguages } from './columnVisibility';

const languages = ['ENGLISH', 'GERMAN', 'FRENCH'];

function row(index: number, values: Record<string, string>) {
    return {
        index, key: `TEXT_${index}`,
        values: Object.entries(values).map(([language, value]) => ({ language, value })),
    };
}

describe('defaultHiddenLanguages', () => {
    const populated = [
        row(0, { ENGLISH: 'Alpha', GERMAN: 'Alfa', FRENCH: 'Alpha FR' }),
    ];

    // The bug this pins: the grid rendered the focus default while the column menu's first click
    // seeded itself from emptyLanguages instead. On a set opened on one language, ticking a second
    // language started from the wrong set and revealed every column at once.
    it('hides everything but the focus language', () => {
        assert.deepEqual(
            defaultHiddenLanguages(populated, languages, 'ENGLISH'),
            ['GERMAN', 'FRENCH']);
    });

    it('matches the focus language regardless of casing', () => {
        assert.deepEqual(
            defaultHiddenLanguages(populated, languages, 'english'),
            ['GERMAN', 'FRENCH']);
    });

    // Without a focus the rule is the other one: get emptiness out of the way, nothing else.
    it('hides only the empty columns when nothing is focused', () => {
        const rows = [row(0, { ENGLISH: 'Alpha', GERMAN: 'Alfa', FRENCH: '' })];

        assert.deepEqual(defaultHiddenLanguages(rows, languages, null), ['FRENCH']);
        assert.deepEqual(defaultHiddenLanguages(rows, languages, undefined), ['FRENCH']);
    });

    it('hides nothing when every column has something and nothing is focused', () => {
        assert.deepEqual(defaultHiddenLanguages(populated, languages, null), []);
    });

    // A focus that is not one of the file's languages would otherwise hide the entire table.
    it('still shows the focus language when it is the only one', () => {
        assert.deepEqual(defaultHiddenLanguages(populated, ['ENGLISH'], 'ENGLISH'), []);
    });
});

describe('emptyLanguages', () => {
    it('names a language nothing in the file says anything in', () => {
        const rows = [
            row(0, { ENGLISH: 'Alpha', GERMAN: 'Alfa', FRENCH: '' }),
            row(1, { ENGLISH: 'Beta', GERMAN: 'Beta DE', FRENCH: '' }),
        ];

        assert.deepEqual(emptyLanguages(rows, languages), ['FRENCH']);
    });

    // One value is enough to make a column worth showing - that is the language being worked on.
    it('does not name a language with even a single value', () => {
        const rows = [
            row(0, { ENGLISH: 'Alpha', GERMAN: '', FRENCH: '' }),
            row(1, { ENGLISH: 'Beta', GERMAN: 'Beta DE', FRENCH: '' }),
        ];

        assert.deepEqual(emptyLanguages(rows, languages), ['FRENCH']);
    });

    it('treats whitespace as empty', () => {
        const rows = [row(0, { ENGLISH: 'Alpha', GERMAN: '   ', FRENCH: '' })];

        assert.deepEqual(emptyLanguages(rows, languages), ['GERMAN', 'FRENCH']);
    });

    // The blank-line marker is a spacer, not content: a credits file whose German column holds
    // nothing but markers has no German in it.
    it('does not count the blank-line marker as content', () => {
        const rows = [
            row(0, { ENGLISH: 'Alice', GERMAN: '[TBL]', FRENCH: '' }),
            row(1, { ENGLISH: '[TBL]', GERMAN: '[TBL]', FRENCH: '' }),
        ];

        assert.deepEqual(emptyLanguages(rows, languages), ['GERMAN', 'FRENCH']);
    });

    // An empty file says nothing about any language - hiding every column would leave a grid of
    // keys and no way to see that.
    it('names nothing when the file has no rows', () => {
        assert.deepEqual(emptyLanguages([], languages), []);
    });

    // Never all of them: a table with no value columns is not a view anyone wants, and the point of
    // hiding is to get the noise out of the way, not the content.
    it('never names every language', () => {
        const rows = [row(0, { ENGLISH: '', GERMAN: '', FRENCH: '' })];

        assert.deepEqual(emptyLanguages(rows, languages), []);
    });

    it('handles a row that has no cell for a language at all', () => {
        const rows = [row(0, { ENGLISH: 'Alpha' })];

        assert.deepEqual(emptyLanguages(rows, languages), ['GERMAN', 'FRENCH']);
    });
});

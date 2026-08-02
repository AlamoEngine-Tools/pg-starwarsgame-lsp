// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { LocRow } from './loc/locRow';
import { countDuplicateKeys, nextSort, sortRows } from './translationView';

function row(index: number, key: string, ...values: [string, string][]): LocRow {
    return { index, key, values: values.map(([language, value]) => ({ language, value })) };
}

function keysOf(rows: LocRow[]): string[] {
    return rows.map(r => r.key);
}

function indicesOf(rows: LocRow[]): number[] {
    return rows.map(r => r.index);
}

describe('sortRows', () => {
    const rows = [
        row(0, 'B_KEY', ['ENGLISH', 'beta'], ['GERMAN', '']),
        row(1, 'A_KEY', ['ENGLISH', 'alpha'], ['GERMAN', 'alpha_de']),
        row(2, 'C_KEY', ['ENGLISH', 'gamma'], ['GERMAN', 'gamma_de']),
    ];

    it('leaves rows in document order when nothing is sorted', () => {
        assert.deepEqual(keysOf(sortRows(rows, null)), ['B_KEY', 'A_KEY', 'C_KEY']);
    });

    it('sorts by key ascending', () => {
        const sorted = sortRows(rows, { column: 'key', direction: 'asc' });

        assert.deepEqual(keysOf(sorted), ['A_KEY', 'B_KEY', 'C_KEY']);
    });

    it('sorts by key descending', () => {
        const sorted = sortRows(rows, { column: 'key', direction: 'desc' });

        assert.deepEqual(keysOf(sorted), ['C_KEY', 'B_KEY', 'A_KEY']);
    });

    // Sorting a language column is how an untranslated row gets found, so an empty cell has to sort
    // to one end rather than being scattered among the translated ones.
    it('sorts by a language column with empty cells first ascending', () => {
        const sorted = sortRows(rows, { column: 'GERMAN', direction: 'asc' });

        assert.deepEqual(keysOf(sorted), ['B_KEY', 'A_KEY', 'C_KEY']);
    });

    it('treats a language the row does not carry as empty', () => {
        const mixed = [
            row(0, 'HAS', ['ENGLISH', 'x'], ['FRENCH', 'oui']),
            row(1, 'LACKS', ['ENGLISH', 'y']),
        ];

        const sorted = sortRows(mixed, { column: 'FRENCH', direction: 'asc' });

        assert.deepEqual(keysOf(sorted), ['LACKS', 'HAS']);
    });

    // Row identity is the document index, so equal sort keys must not shuffle: an edit addresses
    // the index behind the row, and a user re-reading the list expects it to look the same.
    it('is stable across equal keys', () => {
        const duplicates = [
            row(0, 'SAME', ['ENGLISH', 'first']),
            row(1, 'OTHER', ['ENGLISH', 'z']),
            row(2, 'SAME', ['ENGLISH', 'second']),
        ];

        const sorted = sortRows(duplicates, { column: 'key', direction: 'asc' });

        // OTHER sorts first; the point is that the two SAME rows stay in document order behind it.
        assert.deepEqual(indicesOf(sorted), [1, 0, 2]);
    });

    it('orders embedded numbers naturally', () => {
        const numbered = [
            row(0, 'TEXT_ITEM_10', ['ENGLISH', 'ten']),
            row(1, 'TEXT_ITEM_2', ['ENGLISH', 'two']),
        ];

        const sorted = sortRows(numbered, { column: 'key', direction: 'asc' });

        assert.deepEqual(keysOf(sorted), ['TEXT_ITEM_2', 'TEXT_ITEM_10']);
    });

    it('does not mutate the rows it is given', () => {
        const original = [...rows];

        sortRows(rows, { column: 'key', direction: 'asc' });

        assert.deepEqual(keysOf(rows), keysOf(original));
    });
});

describe('nextSort', () => {
    it('starts a column ascending', () => {
        assert.deepEqual(nextSort(null, 'key'), { column: 'key', direction: 'asc' });
    });

    it('turns ascending into descending on the same column', () => {
        assert.deepEqual(
            nextSort({ column: 'key', direction: 'asc' }, 'key'),
            { column: 'key', direction: 'desc' });
    });

    // A third click restores document order rather than cycling back to ascending: in a keyed file
    // that is the order on disk, and there would otherwise be no way back to it.
    it('clears the sort on the third click', () => {
        assert.equal(nextSort({ column: 'key', direction: 'desc' }, 'key'), null);
    });

    it('starts a different column ascending again', () => {
        assert.deepEqual(
            nextSort({ column: 'key', direction: 'desc' }, 'GERMAN'),
            { column: 'GERMAN', direction: 'asc' });
    });
});

describe('countDuplicateKeys', () => {
    it('counts repeats beyond the first occurrence', () => {
        const rows = [row(0, 'A'), row(1, 'B'), row(2, 'A'), row(3, 'A')];

        assert.equal(countDuplicateKeys(rows), 2);
    });

    it('is zero when every key is unique', () => {
        assert.equal(countDuplicateKeys([row(0, 'A'), row(1, 'B')]), 0);
    });
});

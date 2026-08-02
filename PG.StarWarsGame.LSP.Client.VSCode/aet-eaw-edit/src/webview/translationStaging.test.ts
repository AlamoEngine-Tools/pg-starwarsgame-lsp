// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { LocRow } from './loc/locRow';
import { applyStaged, coalesce, TranslationCommand } from './translationStaging';

function rows(): LocRow[] {
    return [
        { index: 0, key: 'TEXT_A', values: [{ language: 'ENGLISH', value: 'a' }] },
        { index: 1, key: 'TEXT_B', values: [{ language: 'ENGLISH', value: 'b' }] },
        { index: 2, key: 'TEXT_C', values: [{ language: 'ENGLISH', value: 'c' }] },
    ];
}

function keysOf(list: LocRow[]): string[] {
    return list.map(r => r.key);
}

function setValue(key: string, value: string, language = 'ENGLISH'): TranslationCommand {
    return { kind: 'setValue', key, language, value };
}

describe('applyStaged', () => {
    it('shows an edit immediately, addressed by key', () => {
        const result = applyStaged(rows(), setValue('TEXT_B', 'changed'));

        assert.equal(result[1].values[0].value, 'changed');
    });

    it('ignores an edit to a key the file does not have', () => {
        const result = applyStaged(rows(), setValue('TEXT_MISSING', 'x'));

        assert.deepEqual(keysOf(result), ['TEXT_A', 'TEXT_B', 'TEXT_C']);
    });

    it('adds a language column to a row that lacks one', () => {
        const result = applyStaged(rows(), setValue('TEXT_A', 'Alfa', 'GERMAN'));

        assert.deepEqual(result[0].values, [
            { language: 'ENGLISH', value: 'a' },
            { language: 'GERMAN', value: 'Alfa' },
        ]);
    });

    it('appends a new entry at the end', () => {
        const result = applyStaged(rows(), {
            kind: 'addEntry', key: 'TEXT_NEW', values: [{ language: 'ENGLISH', value: 'n' }],
        });

        assert.deepEqual(keysOf(result), ['TEXT_A', 'TEXT_B', 'TEXT_C', 'TEXT_NEW']);
        assert.deepEqual(result.map(r => r.index), [0, 1, 2, 3]);
    });

    it('removes an entry by key and renumbers what is left', () => {
        const result = applyStaged(rows(), { kind: 'deleteEntry', key: 'TEXT_A' });

        assert.deepEqual(keysOf(result), ['TEXT_B', 'TEXT_C']);
        assert.deepEqual(result.map(r => r.index), [0, 1]);
    });

    it('renames a key in place, keeping its values', () => {
        const result = applyStaged(rows(), {
            kind: 'renameKey', key: 'TEXT_B', newKey: 'TEXT_RENAMED',
        });

        assert.deepEqual(keysOf(result), ['TEXT_A', 'TEXT_RENAMED', 'TEXT_C']);
        assert.equal(result[1].values[0].value, 'b');
    });

    it('gives every row a cell when a language is added', () => {
        const result = applyStaged(rows(), { kind: 'addLanguage', language: 'GERMAN' });

        assert.ok(result.every(r => r.values.some(v => v.language === 'GERMAN' && v.value === '')));
    });
});

describe('coalesce', () => {
    it('folds repeated edits of one cell down to the last', () => {
        const result = coalesce([
            setValue('TEXT_A', 'x'), setValue('TEXT_A', 'xy'), setValue('TEXT_A', 'xyz'),
        ]);

        assert.equal(result.length, 1);
        assert.equal(result[0].value, 'xyz');
    });

    it('keeps edits to different languages of one entry apart', () => {
        const result = coalesce([
            setValue('TEXT_A', 'x'), setValue('TEXT_A', 'y', 'GERMAN'),
        ]);

        assert.equal(result.length, 2);
    });

    /**
     * A rename changes what a key addresses, so an edit staged before one must not be folded into
     * an edit staged after it - the two are about different entries.
     */
    it('does not fold across a rename', () => {
        const result = coalesce([
            setValue('TEXT_A', 'before'),
            { kind: 'renameKey', key: 'TEXT_A', newKey: 'TEXT_Z' },
            setValue('TEXT_Z', 'after'),
        ]);

        assert.equal(result.length, 3);
    });

    it('does not fold across a delete', () => {
        const result = coalesce([
            setValue('TEXT_A', 'before'),
            { kind: 'deleteEntry', key: 'TEXT_B' },
            setValue('TEXT_A', 'after'),
        ]);

        assert.equal(result.length, 3);
    });

    it('replaying a coalesced queue gives the same result as replaying it whole', () => {
        const queue: TranslationCommand[] = [
            setValue('TEXT_A', 'one'),
            setValue('TEXT_A', 'two'),
            { kind: 'addEntry', key: 'TEXT_D', values: [{ language: 'ENGLISH', value: 'd' }] },
            setValue('TEXT_D', 'dd'),
            { kind: 'deleteEntry', key: 'TEXT_B' },
        ];

        const whole = queue.reduce(applyStaged, rows());
        const folded = coalesce(queue).reduce(applyStaged, rows());

        assert.deepEqual(folded, whole);
    });
});

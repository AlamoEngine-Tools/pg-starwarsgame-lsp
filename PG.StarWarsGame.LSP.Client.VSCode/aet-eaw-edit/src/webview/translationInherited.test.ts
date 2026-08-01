// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import {
    baselineValuesFor, findInheritedKeys, hideInherited, toBaselineRows,
} from './translationInherited';
import { LocRow } from './loc/locRow';

function row(index: number, key: string, english: string, german = ''): LocRow {
    return {
        index, key,
        values: [{ language: 'ENGLISH', value: english }, { language: 'GERMAN', value: german }],
    };
}

const LANGUAGES = ['ENGLISH', 'GERMAN'];

const baseline = toBaselineRows([
    { key: 'SHARED', translations: { ENGLISH: 'same', GERMAN: 'gleich' } },
    { key: 'CHANGED', translations: { ENGLISH: 'original', GERMAN: 'original_de' } },
    { key: 'ONLY_IN_BASE', translations: { ENGLISH: 'base', GERMAN: 'base_de' } },
]);

describe('toBaselineRows', () => {
    // The server sends these as a dictionary, and OmniSharp camel-cases dictionary keys on the way
    // out - "ENGLISH" arrives as "eNGLISH". Matching against the grid's languages without
    // normalising would find nothing in common and report every row as locally overridden.
    it('normalises camel-cased language keys', () => {
        const rows = toBaselineRows([{ key: 'K', translations: { eNGLISH: 'v', gERMAN: 'w' } }]);

        assert.deepEqual(rows[0].values, [
            { language: 'ENGLISH', value: 'v' },
            { language: 'GERMAN', value: 'w' },
        ]);
    });

    it('tolerates an entry with no translations', () => {
        const rows = toBaselineRows([{ key: 'K' } as { key: string; translations: undefined }]);

        assert.deepEqual(rows[0].values, []);
    });
});

describe('findInheritedKeys', () => {
    it('finds a row identical to the baseline in every language', () => {
        const rows = [row(0, 'SHARED', 'same', 'gleich')];

        assert.deepEqual([...findInheritedKeys(rows, baseline, LANGUAGES)], ['SHARED']);
    });

    it('does not count a row that differs in any language', () => {
        const rows = [row(0, 'CHANGED', 'original', 'mine')];

        assert.equal(findInheritedKeys(rows, baseline, LANGUAGES).size, 0);
    });

    it('does not count a row the baseline does not have', () => {
        const rows = [row(0, 'LOCAL_ONLY', 'x', 'y')];

        assert.equal(findInheritedKeys(rows, baseline, LANGUAGES).size, 0);
    });

    // A language the grid does not display is not part of what the user can see or compare, so it
    // must not decide whether a row counts as untouched.
    it('compares only the languages on screen', () => {
        const rows = [row(0, 'CHANGED', 'original', 'mine')];

        assert.deepEqual([...findInheritedKeys(rows, baseline, ['ENGLISH'])], ['CHANGED']);
    });

    it('treats a missing cell and an empty cell alike', () => {
        const rows = [{ index: 0, key: 'SHARED', values: [{ language: 'ENGLISH', value: 'same' }] }];
        const sparse = toBaselineRows([{ key: 'SHARED', translations: { ENGLISH: 'same' } }]);

        assert.deepEqual([...findInheritedKeys(rows, sparse, LANGUAGES)], ['SHARED']);
    });

    it('finds nothing when no baseline has loaded', () => {
        assert.equal(findInheritedKeys([row(0, 'SHARED', 'same', 'gleich')], [], LANGUAGES).size, 0);
    });
});

describe('hideInherited', () => {
    const rows = [row(0, 'SHARED', 'same', 'gleich'), row(1, 'CHANGED', 'mine', 'meins')];
    const inherited = new Set(['SHARED']);

    it('drops rows that only repeat an inherited value', () => {
        assert.deepEqual(hideInherited(rows, inherited, false).map(r => r.key), ['CHANGED']);
    });

    it('keeps everything when asked to show them', () => {
        assert.deepEqual(hideInherited(rows, inherited, true).map(r => r.key), ['SHARED', 'CHANGED']);
    });

    it('keeps every row when nothing is inherited', () => {
        assert.equal(hideInherited(rows, new Set(), false).length, 2);
    });
});

describe('baselineValuesFor', () => {
    it('returns the inherited value for each displayed language', () => {
        assert.deepEqual(baselineValuesFor('SHARED', baseline, LANGUAGES), [
            { language: 'ENGLISH', value: 'same' },
            { language: 'GERMAN', value: 'gleich' },
        ]);
    });

    it('returns nothing for a key the baseline does not have', () => {
        assert.equal(baselineValuesFor('LOCAL_ONLY', baseline, LANGUAGES), null);
    });

    it('yields an empty string for a language the baseline lacks', () => {
        const sparse = toBaselineRows([{ key: 'K', translations: { ENGLISH: 'v' } }]);

        assert.deepEqual(baselineValuesFor('K', sparse, LANGUAGES), [
            { language: 'ENGLISH', value: 'v' },
            { language: 'GERMAN', value: '' },
        ]);
    });
});

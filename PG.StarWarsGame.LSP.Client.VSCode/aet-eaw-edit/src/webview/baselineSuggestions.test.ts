// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { suggestBaselineKeys } from './baselineSuggestions';
import { toBaselineRows } from './translationInherited';

const baseline = toBaselineRows([
    { key: 'TEXT_UNIT_XWING', translations: { ENGLISH: 'X-Wing', GERMAN: 'X-Fluegler' } },
    { key: 'TEXT_UNIT_YWING', translations: { ENGLISH: 'Y-Wing' } },
    { key: 'TEXT_PLANET_HOTH', translations: { ENGLISH: 'Hoth', GERMAN: 'Hoth' } },
    { key: 'TEXT_ALREADY_MINE', translations: { ENGLISH: 'Mine' } },
]);

const existing = ['TEXT_ALREADY_MINE'];
const LANGUAGES = ['ENGLISH', 'GERMAN'];

function keysOf(suggestions: { key: string }[]): string[] {
    return suggestions.map(s => s.key);
}

describe('suggestBaselineKeys', () => {
    /**
     * The point of the feature: a key the game defines but this file does not is exactly what the
     * user is looking for when they want to override something.
     */
    it('offers a baseline key the file does not have', () => {
        const result = suggestBaselineKeys('XWING', baseline, existing, LANGUAGES);

        assert.deepEqual(keysOf(result), ['TEXT_UNIT_XWING']);
    });

    it('never offers a key the file already has', () => {
        const result = suggestBaselineKeys('ALREADY', baseline, existing, LANGUAGES);

        assert.deepEqual(keysOf(result), []);
    });

    it('matches without regard to case', () => {
        assert.deepEqual(keysOf(suggestBaselineKeys('xwing', baseline, existing, LANGUAGES)),
            ['TEXT_UNIT_XWING']);
    });

    /**
     * A prefix match is what the user usually means, so those come first; a substring match is
     * still useful when only the middle of a key is remembered.
     */
    it('puts prefix matches before substring matches', () => {
        const result = suggestBaselineKeys('TEXT_UNIT', baseline, existing, LANGUAGES);

        assert.deepEqual(keysOf(result), ['TEXT_UNIT_XWING', 'TEXT_UNIT_YWING']);
    });

    // A 19,000-key baseline would otherwise dump the whole game into a dropdown.
    it('suggests nothing until something is typed', () => {
        assert.deepEqual(keysOf(suggestBaselineKeys('', baseline, existing, LANGUAGES)), []);
        assert.deepEqual(keysOf(suggestBaselineKeys('   ', baseline, existing, LANGUAGES)), []);
    });

    it('caps how many it returns', () => {
        const many = toBaselineRows(
            Array.from({ length: 500 }, (_, i) => ({
                key: `TEXT_MANY_${i}`, translations: { ENGLISH: `v${i}` },
            })));

        assert.ok(suggestBaselineKeys('TEXT_MANY', many, [], LANGUAGES).length <= 20);
    });

    it('carries the inherited values for every displayed language', () => {
        const [suggestion] = suggestBaselineKeys('XWING', baseline, existing, LANGUAGES);

        assert.deepEqual(suggestion.values, [
            { language: 'ENGLISH', value: 'X-Wing' },
            { language: 'GERMAN', value: 'X-Fluegler' },
        ]);
    });

    it('yields an empty string for a language the baseline entry lacks', () => {
        const [suggestion] = suggestBaselineKeys('YWING', baseline, existing, LANGUAGES);

        assert.deepEqual(suggestion.values, [
            { language: 'ENGLISH', value: 'Y-Wing' },
            { language: 'GERMAN', value: '' },
        ]);
    });

    it('finds nothing when no baseline has loaded', () => {
        assert.deepEqual(keysOf(suggestBaselineKeys('TEXT', [], [], LANGUAGES)), []);
    });
});

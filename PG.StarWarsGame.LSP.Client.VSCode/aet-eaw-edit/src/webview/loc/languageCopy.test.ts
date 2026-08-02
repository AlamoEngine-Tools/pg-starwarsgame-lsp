// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { languageCopyValues } from './languageCopy';

const rows = [
    {
        index: 0, key: 'HEADER',
        values: [{ language: 'ENGLISH', value: 'Lead Designer' }, { language: 'GERMAN', value: 'Chefdesigner' }],
    },
    {
        index: 1, key: 'CENTER',
        values: [{ language: 'ENGLISH', value: 'Alice Smith' }, { language: 'GERMAN', value: '' }],
    },
    {
        index: 2, key: 'CENTER',
        values: [{ language: 'ENGLISH', value: '[TBL]' }, { language: 'GERMAN', value: '' }],
    },
    {
        index: 3, key: 'CENTER',
        values: [{ language: 'ENGLISH', value: '' }, { language: 'GERMAN', value: '' }],
    },
];

describe('languageCopyValues', () => {
    // The reason the feature exists: a name is the same in every language, so a German column left
    // empty is not a translation decision, it is unfinished work.
    it('copies into cells the target language has not filled in', () => {
        const copied = languageCopyValues('ENGLISH', 'GERMAN', rows);

        assert.deepEqual(copied.map(c => [c.index, c.value]), [[1, 'Alice Smith'], [2, '[TBL]']]);
    });

    it('never overwrites something the target language already says', () => {
        const copied = languageCopyValues('ENGLISH', 'GERMAN', rows);

        assert.ok(!copied.some(c => c.index === 0));
    });

    // A spacer must be copied across too. The DAT export drops empty values, so a gap marked only
    // in English disappears from the German crawl - and the crawl closes up around it.
    it('copies the blank-line marker as well', () => {
        const copied = languageCopyValues('ENGLISH', 'GERMAN', rows);

        assert.equal(copied.find(c => c.index === 2)?.value, '[TBL]');
    });

    it('has nothing to copy where the source is empty too', () => {
        const copied = languageCopyValues('ENGLISH', 'GERMAN', rows);

        assert.ok(!copied.some(c => c.index === 3));
    });

    it('carries the key, so a keyed editor can address the same rows', () => {
        assert.deepEqual(
            languageCopyValues('ENGLISH', 'GERMAN', rows).map(c => c.key), ['CENTER', 'CENTER']);
    });

    it('copies nothing onto itself', () => {
        assert.deepEqual(languageCopyValues('ENGLISH', 'ENGLISH', rows), []);
    });

    it('matches language names case-insensitively', () => {
        assert.equal(languageCopyValues('english', 'german', rows).length, 2);
    });

    it('is empty when the source language is not in the file', () => {
        assert.deepEqual(languageCopyValues('FRENCH', 'GERMAN', rows), []);
    });

    // Whitespace is not content - a cell holding only spaces is as unfilled as an empty one.
    it('treats a whitespace-only cell as unfilled', () => {
        const blank = [{
            index: 0, key: 'CENTER',
            values: [{ language: 'ENGLISH', value: 'Bob' }, { language: 'GERMAN', value: '  ' }],
        }];

        assert.equal(languageCopyValues('ENGLISH', 'GERMAN', blank).length, 1);
    });

    // A row that has no cell at all for the target language still needs one.
    it('fills a row that has no cell for the target language yet', () => {
        const missing = [{
            index: 0, key: 'CENTER', values: [{ language: 'ENGLISH', value: 'Bob' }],
        }];

        assert.deepEqual(
            languageCopyValues('ENGLISH', 'GERMAN', missing).map(c => c.value), ['Bob']);
    });
});

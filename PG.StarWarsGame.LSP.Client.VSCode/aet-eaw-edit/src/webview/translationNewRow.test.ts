// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { blankDraft, draftToCommandValues, normaliseKey, validateNewKey } from './translationNewRow';

const EXISTING = ['TEXT_ALPHA', 'TEXT_BETA'];

describe('validateNewKey', () => {
    it('accepts a key the file does not have', () => {
        assert.equal(validateNewKey('TEXT_GAMMA', EXISTING), null);
    });

    it('rejects an empty key', () => {
        assert.notEqual(validateNewKey('', EXISTING), null);
    });

    it('rejects a key that is only whitespace', () => {
        assert.notEqual(validateNewKey('   ', EXISTING), null);
    });

    it('rejects a duplicate', () => {
        const error = validateNewKey('TEXT_ALPHA', EXISTING);

        assert.ok(error?.includes('TEXT_ALPHA'), `message should name the key, got '${error}'`);
    });

    // The server compares keys case-insensitively when it validates a batch, so accepting a
    // different-cased duplicate here would only move the rejection to Save.
    it('rejects a duplicate differing only in case', () => {
        assert.notEqual(validateNewKey('text_alpha', EXISTING), null);
    });

    // The key is trimmed before it is stored, so surrounding spaces must not sneak a duplicate past.
    it('rejects a duplicate padded with whitespace', () => {
        assert.notEqual(validateNewKey('  TEXT_ALPHA  ', EXISTING), null);
    });

    it('accepts the first key in an empty file', () => {
        assert.equal(validateNewKey('TEXT_FIRST', []), null);
    });
});

describe('normaliseKey', () => {
    it('trims surrounding whitespace', () => {
        assert.equal(normaliseKey('  TEXT_A  '), 'TEXT_A');
    });

    it('leaves the inner text alone', () => {
        assert.equal(normaliseKey('TEXT WITH SPACE'), 'TEXT WITH SPACE');
    });
});

describe('blankDraft', () => {
    it('has an entry for every loaded language', () => {
        assert.deepEqual(blankDraft(['ENGLISH', 'GERMAN']), {
            key: '',
            values: [{ language: 'ENGLISH', value: '' }, { language: 'GERMAN', value: '' }],
        });
    });
});

describe('draftToCommandValues', () => {
    it('keeps every language, including the ones left empty', () => {
        const draft = {
            key: 'K',
            values: [{ language: 'ENGLISH', value: 'hello' }, { language: 'GERMAN', value: '' }],
        };

        assert.deepEqual(draftToCommandValues(draft), [
            { language: 'ENGLISH', value: 'hello' },
            { language: 'GERMAN', value: '' },
        ]);
    });
});

// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import {
    blankDraft, draftToCommandValues, foldToEngineKey, normaliseKey, validateNewKey,
} from './translationNewRow';

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

    /**
     * The engine keys an entry by the CRC32 of its ASCII bytes, and that hash is case-sensitive - so
     * a differently-cased key is a second entry the game reads perfectly well, not a duplicate.
     * Refusing it here contradicted the server, which now reports it as a warning instead.
     */
    it('allows a key differing only in case, which the engine reads as a separate entry', () => {
        assert.equal(validateNewKey('text_alpha', EXISTING), null);
    });

    /**
     * The collision no string comparison finds: keys are encoded as ASCII, which folds every
     * character above 0x7F to '?', so these two land on the same entry however different they look.
     * This is the case that used to sail past the dialog and only fail at Save.
     */
    it('rejects a key that collides only after ASCII folding', () => {
        const error = validateNewKey('TEST_Ö', ['TEST_Ä']);

        assert.notEqual(error, null);
        assert.match(error!, /ASCII/);
    });

    it('accepts two keys whose non-ASCII characters sit in different places', () => {
        assert.equal(validateNewKey('TEST_ÄX', ['TEST_XÄ']), null);
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

describe('foldToEngineKey', () => {
    it('leaves a plain ASCII key alone', () => {
        assert.equal(foldToEngineKey('TEXT_ALPHA'), 'TEXT_ALPHA');
    });

    // Matches .NET's ASCII encoder, which substitutes '?' for anything it cannot represent.
    it('replaces every character above ASCII with a question mark', () => {
        assert.equal(foldToEngineKey('TEST_Ä'), 'TEST_?');
        assert.equal(foldToEngineKey('TEST_Ö'), 'TEST_?');
    });

    it('keeps case, because the engine hash is case-sensitive', () => {
        assert.notEqual(foldToEngineKey('TEXT_A'), foldToEngineKey('text_a'));
    });

    it('trims, since the key is stored trimmed', () => {
        assert.equal(foldToEngineKey('  TEXT_A  '), 'TEXT_A');
    });
});

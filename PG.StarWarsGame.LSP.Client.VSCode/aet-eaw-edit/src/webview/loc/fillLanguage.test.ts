// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import {
    canCopyBetweenLanguages, copyFromOptions, FillSource, nextCopyFrom, resolveFillSource,
} from './fillLanguage';

describe('resolveFillSource', () => {
    const everything = { canCopy: true, canSeed: true };

    it('honours each choice the file can satisfy', () => {
        assert.equal(resolveFillSource('language', everything), 'language');
        assert.equal(resolveFillSource('baseline', everything), 'baseline');
        assert.equal(resolveFillSource('file', everything), 'file');
    });

    // The bug this pins: the dialog derived one boolean per source and read "from another
    // language" as `!fromGame`. With a third source that was true at the same time as "from
    // another file" - two radios selected at once, two "Copy from" dropdowns side by side.
    it('is only ever one thing at a time', () => {
        const choices: FillSource[] = ['language', 'baseline', 'file'];
        const offers = [
            { canCopy: true, canSeed: true },
            { canCopy: true, canSeed: false },
            { canCopy: false, canSeed: true },
            { canCopy: false, canSeed: false },
        ];

        for (const chosen of choices) {
            for (const offer of offers) {
                const resolved = resolveFillSource(chosen, offer);
                const flags = [resolved === 'language', resolved === 'baseline', resolved === 'file'];

                assert.equal(
                    flags.filter(Boolean).length, 1,
                    `${chosen} with ${JSON.stringify(offer)} resolved to ${resolved}`);
            }
        }
    });

    // Sitting on a source the file cannot satisfy is what left Fill permanently disabled.
    it('falls back rather than sitting on a source that can produce nothing', () => {
        assert.equal(resolveFillSource('language', { canCopy: false, canSeed: false }), 'baseline');
        assert.equal(resolveFillSource('file', { canCopy: true, canSeed: false }), 'baseline');
    });

    // A one-language file that has a sibling to start from must not be forced onto the baseline.
    it('keeps a seed choice when copying between languages is impossible', () => {
        assert.equal(resolveFillSource('file', { canCopy: false, canSeed: true }), 'file');
    });
});

describe('copyFromOptions', () => {
    // The reported bug: on a German-only credits file the "Copy from" dropdown offered GERMAN -
    // the very language being filled in - so the only thing on offer could never be confirmed.
    it('offers nothing when the file has only the language being filled in', () => {
        assert.deepEqual(copyFromOptions(['GERMAN'], 'GERMAN'), []);
    });

    it('leaves out the language being filled in', () => {
        assert.deepEqual(copyFromOptions(['ENGLISH', 'GERMAN'], 'GERMAN'), ['ENGLISH']);
        assert.deepEqual(copyFromOptions(['ENGLISH', 'GERMAN'], 'ENGLISH'), ['GERMAN']);
    });

    it('keeps the file order for the rest', () => {
        assert.deepEqual(
            copyFromOptions(['ENGLISH', 'GERMAN', 'FRENCH'], 'GERMAN'),
            ['ENGLISH', 'FRENCH']);
    });

    // The declared language and the dropdown's value have travelled through different layers.
    it('matches the target regardless of casing', () => {
        assert.deepEqual(copyFromOptions(['English', 'GERMAN'], 'english'), ['GERMAN']);
    });

    it('offers everything when nothing is being filled in yet', () => {
        assert.deepEqual(copyFromOptions(['ENGLISH', 'GERMAN'], ''), ['ENGLISH', 'GERMAN']);
    });
});

describe('canCopyBetweenLanguages', () => {
    it('is false for a single-language file', () => {
        assert.equal(canCopyBetweenLanguages(['GERMAN']), false);
    });

    it('is false for a file with no languages at all', () => {
        assert.equal(canCopyBetweenLanguages([]), false);
    });

    it('is true once there are two to copy between', () => {
        assert.equal(canCopyBetweenLanguages(['ENGLISH', 'GERMAN']), true);
    });

    // A file that somehow declares one language twice still has nothing to copy between.
    it('does not count the same language twice', () => {
        assert.equal(canCopyBetweenLanguages(['GERMAN', 'german']), false);
    });
});

describe('nextCopyFrom', () => {
    it('keeps the current source when it is still offerable', () => {
        assert.equal(
            nextCopyFrom(['ENGLISH', 'GERMAN', 'FRENCH'], 'FRENCH', 'ENGLISH'),
            'ENGLISH');
    });

    // Changing what you are filling in must not leave the source pointing at it.
    it('moves off a source the new target has taken', () => {
        assert.equal(nextCopyFrom(['ENGLISH', 'GERMAN'], 'ENGLISH', 'ENGLISH'), 'GERMAN');
    });

    it('gives up on a file with nothing to copy from', () => {
        assert.equal(nextCopyFrom(['GERMAN'], 'GERMAN', 'GERMAN'), '');
    });
});

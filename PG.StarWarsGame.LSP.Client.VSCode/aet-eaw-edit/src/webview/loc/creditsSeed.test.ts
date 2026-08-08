// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { creditsBaselineSeed, creditsFileSeed } from './creditsSeed';

const baseline = [
    {
        key: 'HEADER',
        values: [{ language: 'ENGLISH', value: 'Voice Cast' }, { language: 'GERMAN', value: 'Sprecher' }],
    },
    {
        key: 'CENTER',
        values: [{ language: 'ENGLISH', value: 'Petroglyph' }, { language: 'GERMAN', value: 'Petroglyph' }],
    },
    {
        key: 'CENTER',
        values: [{ language: 'ENGLISH', value: '[TBL]' }, { language: 'GERMAN', value: '[TBL]' }],
    },
];

describe('creditsBaselineSeed', () => {
    it('takes the game\'s credits for the language, in crawl order', () => {
        assert.deepEqual(creditsBaselineSeed('GERMAN', baseline), [
            { key: 'HEADER', value: 'Sprecher' },
            { key: 'CENTER', value: 'Petroglyph' },
            { key: 'CENTER', value: '[TBL]' },
        ]);
    });

    // Order IS the content of a credits file, and duplicate directives are how it is written -
    // so neither may be collapsed or sorted on the way in.
    it('keeps repeated directives as separate lines', () => {
        assert.equal(creditsBaselineSeed('GERMAN', baseline).filter(r => r.key === 'CENTER').length, 2);
    });

    // The spacer is what gives the crawl its pauses; dropping it would run the sections together.
    it('keeps the blank-line marker', () => {
        assert.ok(creditsBaselineSeed('GERMAN', baseline).some(r => r.value === '[TBL]'));
    });

    // An empty line is dropped by the export anyway, so seeding one only makes a row to delete.
    it('leaves out lines the game does not have in this language', () => {
        const partial = [
            { key: 'HEADER', values: [{ language: 'ENGLISH', value: 'Voice Cast' }, { language: 'GERMAN', value: '' }] },
            { key: 'CENTER', values: [{ language: 'ENGLISH', value: 'Petroglyph' }, { language: 'GERMAN', value: 'Petroglyph' }] },
        ];

        assert.deepEqual(creditsBaselineSeed('GERMAN', partial), [
            { key: 'CENTER', value: 'Petroglyph' },
        ]);
    });

    it('is empty for a language the game does not ship', () => {
        assert.deepEqual(creditsBaselineSeed('KLINGON', baseline), []);
    });

    it('is empty when there is no baseline to draw on', () => {
        assert.deepEqual(creditsBaselineSeed('GERMAN', []), []);
    });

    it('matches the language regardless of casing', () => {
        assert.equal(creditsBaselineSeed('german', baseline).length, 3);
    });
});

describe('creditsFileSeed', () => {
    const english = [
        { index: 0, key: 'HEADER', values: [{ language: 'ENGLISH', value: 'Voice Cast' }] },
        { index: 1, key: 'CENTER', values: [{ language: 'ENGLISH', value: 'ALICE' }] },
        { index: 2, key: 'CENTER', values: [{ language: 'ENGLISH', value: '[TBL]' }] },
        { index: 3, key: 'CENTER', values: [{ language: 'ENGLISH', value: 'BOB' }] },
    ];

    it('copies every line, in the order the source file has them', () => {
        assert.deepEqual(creditsFileSeed(english, 'ENGLISH'), [
            { key: 'HEADER', value: 'Voice Cast' },
            { key: 'CENTER', value: 'ALICE' },
            { key: 'CENTER', value: '[TBL]' },
            { key: 'CENTER', value: 'BOB' },
        ]);
    });

    // The directive is what makes a line a heading or a name; without it the crawl loses its shape.
    it('brings the formatting directive across with the text', () => {
        assert.deepEqual(
            creditsFileSeed(english, 'ENGLISH').map(r => r.key),
            ['HEADER', 'CENTER', 'CENTER', 'CENTER']);
    });

    // Unlike the baseline seed, which skips what it has nothing for: here the source is the
    // template, and a line it leaves blank is still a line of the running order.
    it('keeps a line the source says nothing in, so the order survives', () => {
        const withGap = [
            { index: 0, key: 'HEADER', values: [{ language: 'ENGLISH', value: 'Voice Cast' }] },
            { index: 1, key: 'CENTER', values: [{ language: 'ENGLISH', value: '' }] },
        ];

        assert.deepEqual(creditsFileSeed(withGap, 'ENGLISH'), [
            { key: 'HEADER', value: 'Voice Cast' },
            { key: 'CENTER', value: '' },
        ]);
    });

    it('reads the source language regardless of casing', () => {
        assert.equal(creditsFileSeed(english, 'english').length, 4);
    });

    it('takes the language it was asked for out of a multi-language row', () => {
        const both = [
            { index: 0, key: 'HEADER', values: [
                { language: 'ENGLISH', value: 'Voice Cast' },
                { language: 'GERMAN', value: 'Sprecher' },
            ] },
        ];

        assert.deepEqual(creditsFileSeed(both, 'GERMAN'), [{ key: 'HEADER', value: 'Sprecher' }]);
    });

    it('is empty for a source with no rows', () => {
        assert.deepEqual(creditsFileSeed([], 'ENGLISH'), []);
    });
});

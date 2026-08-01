// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { buildCrawl, isSpacerRow } from './creditsCrawlModel';
import { LocRow } from './loc/locRow';

function row(index: number, key: string, value: string, language = 'ENGLISH'): LocRow {
    return { index, key, values: [{ language, value }] };
}

function multi(index: number, key: string, ...values: [string, string][]): LocRow {
    return { index, key, values: values.map(([language, value]) => ({ language, value })) };
}

describe('isSpacerRow', () => {
    it('treats a row that is the sentinel in every language as a spacer', () => {
        assert.equal(
            isSpacerRow(multi(0, 'CENTER', ['ENGLISH', '[TBL]'], ['GERMAN', '[TBL]'])), true);
    });

    it('does not treat a partly translated row as a spacer', () => {
        assert.equal(
            isSpacerRow(multi(0, 'CENTER', ['ENGLISH', '[TBL]'], ['GERMAN', 'Regie'])), false);
    });

    it('does not treat a row with no languages as a spacer', () => {
        assert.equal(isSpacerRow({ index: 0, key: 'CENTER', values: [] }), false);
    });
});

describe('buildCrawl', () => {
    it('renders each row in file order', () => {
        const blocks = buildCrawl(
            [row(0, 'A', 'Directed by'), row(1, 'B', 'Someone')], 'ENGLISH');

        assert.deepEqual(blocks.map(b => b.kind), ['line', 'line']);
        assert.deepEqual(blocks.map(b => b.text), ['Directed by', 'Someone']);
    });

    // Blank rows are how a credits file paces the crawl; rendering them as empty lines is the whole
    // reason the ordered model preserves them.
    it('turns a blank value into a gap', () => {
        const blocks = buildCrawl(
            [row(0, 'A', 'Alpha'), row(1, 'SPACER', ''), row(2, 'B', 'Beta')], 'ENGLISH');

        assert.deepEqual(blocks.map(b => b.kind), ['line', 'gap', 'line']);
    });

    // Several blank rows in a row are a single visual pause, not a run of empty screens.
    it('collapses consecutive blanks into one gap', () => {
        const blocks = buildCrawl(
            [row(0, 'A', 'Alpha'), row(1, 'S1', ''), row(2, 'S2', ''), row(3, 'B', 'Beta')],
            'ENGLISH');

        assert.deepEqual(blocks.map(b => b.kind), ['line', 'gap', 'line']);
    });

    // Leading and trailing blanks would show as dead time at either end of the crawl.
    it('drops blanks at the start and end', () => {
        const blocks = buildCrawl(
            [row(0, 'S1', ''), row(1, 'A', 'Alpha'), row(2, 'S2', '')], 'ENGLISH');

        assert.deepEqual(blocks.map(b => b.kind), ['line']);
    });

    it('keeps duplicate keys as separate lines', () => {
        const blocks = buildCrawl(
            [row(0, 'ROLE', 'Director'), row(1, 'ROLE', 'Producer')], 'ENGLISH');

        assert.deepEqual(blocks.map(b => b.text), ['Director', 'Producer']);
    });

    it('reads the requested language', () => {
        const multi: LocRow = {
            index: 0,
            key: 'A',
            values: [
                { language: 'ENGLISH', value: 'Directed by' },
                { language: 'GERMAN', value: 'Regie' },
            ],
        };

        assert.equal(buildCrawl([multi], 'GERMAN')[0].text, 'Regie');
    });

    // An untranslated row must not silently vanish from the preview - showing the key makes the
    // gap in the translation obvious, which is exactly what someone previewing wants to see.
    it('falls back to the key when the chosen language has no value', () => {
        // Translated in English, not yet in German: the row exists, the German text does not.
        const partly: LocRow = {
            index: 0,
            key: 'CREDIT_LEAD',
            values: [
                { language: 'ENGLISH', value: 'Lead Designer' },
                { language: 'GERMAN', value: '' },
            ],
        };

        const blocks = buildCrawl([partly], 'GERMAN');

        assert.equal(blocks.length, 1);
        assert.equal(blocks[0].text, 'CREDIT_LEAD');
        assert.equal(blocks[0].untranslated, true);
    });

    // A row that is blank in every language is a spacer, not a missing translation - it must stay
    // a gap rather than becoming its own key on screen.
    it('treats a row blank in every language as a gap, not a fallback', () => {
        const spacer: LocRow = {
            index: 0,
            key: '',
            values: [{ language: 'ENGLISH', value: '' }, { language: 'GERMAN', value: '' }],
        };
        const blocks = buildCrawl(
            [row(0, 'A', 'Alpha'), spacer, row(2, 'B', 'Beta')], 'GERMAN');

        assert.deepEqual(blocks.map(b => b.kind), ['line', 'gap', 'line']);
    });

    it('returns nothing for an empty file rather than throwing', () => {
        assert.deepEqual(buildCrawl([], 'ENGLISH'), []);
    });

    // ── the engine's own formatting model ────────────────────────────────────
    //
    // In a credits file the key is a formatting directive, not an identifier - which is why the
    // format allows duplicates. The shipped creditstext_english.dat uses exactly two: HEADER for a
    // label line (a role, or a section title) and CENTER for the centred line under it.

    it('marks a HEADER row as a header block', () => {
        const blocks = buildCrawl(
            [row(0, 'HEADER', 'Producer'), row(1, 'CENTER', 'CHUCK KROEGEL')], 'ENGLISH');

        assert.deepEqual(blocks.map(b => b.style), ['header', 'line']);
        assert.deepEqual(blocks.map(b => b.text), ['Producer', 'CHUCK KROEGEL']);
    });

    it('matches the directive whatever its casing', () => {
        assert.equal(buildCrawl([row(0, 'header', 'X')], 'ENGLISH')[0].style, 'header');
    });

    // [TBL] is the engine's blank-line sentinel - it appears 182 times in the shipped file and is
    // never text. Rendering it literally would put "[TBL]" on screen throughout the crawl.
    it('treats the [TBL] sentinel as a gap, not as text', () => {
        const blocks = buildCrawl(
            [row(0, 'CENTER', 'Alpha'), row(1, 'CENTER', '[TBL]'), row(2, 'CENTER', 'Beta')],
            'ENGLISH');

        assert.deepEqual(blocks.map(b => b.kind), ['line', 'gap', 'line']);
        assert.equal(blocks.some(b => b.text.includes('TBL')), false);
    });

    it('accepts the sentinel with padding or different casing', () => {
        for (const sentinel of ['[TBL]', ' [TBL] ', '[tbl]']) {
            const blocks = buildCrawl(
                [row(0, 'CENTER', 'A'), row(1, 'CENTER', sentinel), row(2, 'CENTER', 'B')], 'ENGLISH');
            assert.deepEqual(blocks.map(b => b.kind), ['line', 'gap', 'line'], sentinel);
        }
    });

    // Consecutive sentinels are one pause, exactly as consecutive blanks are - the shipped file
    // opens with two in a row.
    it('collapses consecutive sentinels into one gap', () => {
        const blocks = buildCrawl(
            [row(0, 'CENTER', 'A'), row(1, 'CENTER', '[TBL]'), row(2, 'CENTER', '[TBL]'),
                row(3, 'CENTER', 'B')],
            'ENGLISH');

        assert.deepEqual(blocks.map(b => b.kind), ['line', 'gap', 'line']);
    });

    // A sentinel is a spacer, not a missing translation, so it must never fall back to its key.
    it('does not treat a sentinel row as untranslated', () => {
        const partly: LocRow = {
            index: 0,
            key: 'CENTER',
            values: [
                { language: 'ENGLISH', value: '[TBL]' },
                { language: 'GERMAN', value: '' },
            ],
        };

        assert.deepEqual(buildCrawl([partly], 'GERMAN').map(b => b.kind), []);
    });
});

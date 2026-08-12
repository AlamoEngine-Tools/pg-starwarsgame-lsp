// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { fitLabel, labelLayout, MIN_LABEL_PX, wrapLabel } from './lodLabel';

describe('fitLabel', () => {
    // One character is one unit wide here, so the arithmetic is obvious in the assertions.
    const measure = (s: string): number => s.length;

    it('leaves a label that already fits', () => {
        assert.equal(fitLabel('Alpha', 10, measure), 'Alpha');
    });

    it('trims a label that does not fit', () => {
        assert.equal(fitLabel('AlphaBetaGamma', 5, measure), 'Alpha');
    });

    it('returns nothing when there is no room at all', () => {
        assert.equal(fitLabel('Alpha', 0, measure), '');
    });

    it('keeps the label exactly as authored - no assumptions about naming', () => {
        assert.equal(fitLabel('Story_Alpha', 11, measure), 'Story_Alpha');
    });
});

describe('labelLayout', () => {
    // 0.5px of advance per 1px of font size - so an N-character label is N * 0.5 * fontPx wide.
    const ADVANCE = 0.5;

    it('does not shrink below the cap when the label already fits', () => {
        // 2 chars at 13px need 13px of the 500 available.
        assert.equal(labelLayout('ab', 500, 60, ADVANCE, 13).fontPx, 13);
    });

    // The case that started this: a 35-character name in a node roughly 63 x 36.
    it('trades size for lines so a long name fits the node', () => {
        const name = 'Story_Empire_Intro_Capture_Alderaan';
        const { fontPx, maxLines } = labelLayout(name, 63, 36, ADVANCE, 13);
        assert.ok(fontPx >= MIN_LABEL_PX, `font ${fontPx} below the floor`);
        const lines = wrapLabel(name, 63, maxLines, s => s.length * ADVANCE * fontPx);
        assert.equal(lines.join(''), name, `dropped text: ${JSON.stringify(lines)}`);
    });

    // Sizing from an idealised grid picks a font one step too large here, because breaking at the
    // underscores leaves every line short of the nominal capacity.
    it('accounts for the ragged ends that separator breaks leave', () => {
        const name = 'Story_Empire_Intro_Capture_Alderaan';
        const { fontPx, maxLines } = labelLayout(name, 63, 36, ADVANCE, 13);
        const perLine = Math.floor(63 / (ADVANCE * fontPx));
        assert.ok(perLine * maxLines > name.length,
            'expected the chosen size to leave slack a grid calculation would have spent');
    });

    it('reports the lines that actually fit the height', () => {
        // 50 chars fit the cap exactly: 10 per line across 60 / (10 * 1.15) = 5 lines.
        assert.deepEqual(labelLayout('a'.repeat(50), 50, 60, ADVANCE, 10),
            { fontPx: 10, maxLines: 5 });
    });

    // Past this the text is texture, not a label - better to truncate than to render a smudge.
    it('stops shrinking at the legibility floor', () => {
        assert.equal(labelLayout('a'.repeat(2000), 40, 20, ADVANCE, 13).fontPx, MIN_LABEL_PX);
    });

    it('always leaves room for at least one line', () => {
        assert.equal(labelLayout('abc', 50, 1, ADVANCE, 13).maxLines, 1);
    });

    it('falls back to the cap when it cannot compute a size', () => {
        assert.equal(labelLayout('', 50, 60, ADVANCE, 13).fontPx, 13);
        assert.equal(labelLayout('abc', 50, 60, 0, 13).fontPx, 13);
    });
});

describe('wrapLabel', () => {
    const measure = (s: string): number => s.length;

    it('keeps a short label on one line', () => {
        assert.deepEqual(wrapLabel('Alpha', 10, 3, measure), ['Alpha']);
    });

    // The case that started this: 35 characters into a node about fifteen wide.
    it('spreads a long event name across the lines available', () => {
        assert.deepEqual(
            wrapLabel('Story_Empire_Late_Death_Star', 14, 4, measure),
            ['Story_Empire_', 'Late_Death_', 'Star']);
    });

    it('breaks after separators so lines start on a word', () => {
        assert.deepEqual(wrapLabel('One_Two_Three', 8, 3, measure), ['One_Two_', 'Three']);
    });

    it('hard-splits a single word too long for a line', () => {
        assert.deepEqual(wrapLabel('Supercalifragilistic', 6, 3, measure),
            ['Superc', 'alifra', 'gilist']);
    });

    it('stops at the line budget rather than overflowing the node', () => {
        assert.equal(wrapLabel('One_Two_Three_Four_Five', 6, 2, measure).length, 2);
    });

    it('returns nothing when there is no room', () => {
        assert.deepEqual(wrapLabel('Alpha', 0, 3, measure), []);
        assert.deepEqual(wrapLabel('Alpha', 10, 0, measure), []);
    });
});

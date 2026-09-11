// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import {
    createLabelSizer, fitLabel, labelLayout, LINE_RATIO, linesThatFit, MIN_LABEL_PX, wrapLabel,
} from './lodLabel';

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

describe('createLabelSizer', () => {
    const ADVANCE = 0.5; // one character is half the font size wide, as in the labelLayout tests
    const LONGEST = 'Empire_ActI_Mission_Two_Failed_04';

    // The frame used to pick ONE size, from the graph's longest label against its LARGEST node.
    // Every shorter node inherited a line budget its box could not hold, and since the block is
    // centred the text spilled out of both ends into the rows above and below - invisible while
    // auto-arrange spread nodes thousands of pixels apart, obvious once they sit 60px apart.
    // Capping the budget stopped the collision but truncated those labels instead; sizing per box
    // keeps the name AND keeps it inside, because a smaller font fits both more characters per
    // line and more lines.
    it('shrinks the font until the whole name fits a short box', () => {
        const sizer = createLabelSizer(LONGEST, ADVANCE, 13);

        const tall = sizer(60, 120);
        const short = sizer(60, 30);
        assert.ok(short.fontPx < tall.fontPx,
            `a 30px box takes a smaller font than a 120px one (${short.fontPx} vs ${tall.fontPx})`);

        const wrapped = wrapLabel(LONGEST, 60, short.maxLines,
            s => s.length * ADVANCE * short.fontPx);
        assert.equal(wrapped.join(''), LONGEST, 'the whole name is drawn, not a truncation of it');
    });

    // The property that keeps text inside the box: the block is (lines - 1) * fontPx * LINE_RATIO
    // tall plus one line of glyphs, centred on the node.
    it('never returns a layout taller than the box it was asked about', () => {
        const sizer = createLabelSizer(LONGEST, ADVANCE, 13);
        for (let height = 12; height <= 400; height += 7) {
            const { fontPx, maxLines } = sizer(60, height);
            const block = (maxLines - 1) * fontPx * LINE_RATIO + fontPx;
            assert.ok(block <= Math.max(height, fontPx),
                `${maxLines} lines at ${fontPx}px is ${block.toFixed(1)}px, inside a ${height}px box`);
        }
    });

    // Hundreds of nodes are drawn per frame and they share a handful of box sizes, so the walk
    // down from the maximum runs once per size rather than once per node.
    it('answers the same box from cache', () => {
        const sizer = createLabelSizer(LONGEST, ADVANCE, 13);
        assert.equal(sizer(60, 120), sizer(60, 120));
        assert.notEqual(sizer(60, 120), sizer(60, 30));
    });

    it('sizes from the graph-wide longest label, so equal boxes agree', () => {
        const sizer = createLabelSizer(LONGEST, ADVANCE, 13);
        assert.deepEqual(sizer(60, 90), labelLayout(LONGEST, 60, 90, ADVANCE, 13));
    });
});

describe('linesThatFit', () => {
    it('is the line count the sizing pass and the draw pass share', () => {
        assert.equal(linesThatFit(100, 10), Math.floor(100 / (10 * LINE_RATIO)));
        assert.equal(linesThatFit(5, 10), 1, 'never fewer than one');
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

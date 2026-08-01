// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { CREDITS_DIRECTIVES } from './creditsCrawlModel';
import {
    CREDITS_STEPS, directiveLabel, directiveOptionText, dropTargetAt, stepById,
} from './creditsSteps';

/** Three rows, 20px tall, starting at y=100 - shaped like what the grid reports. */
const rows = [
    { index: 0, top: 100, bottom: 120 },
    { index: 1, top: 120, bottom: 140 },
    { index: 2, top: 140, bottom: 160 },
];

describe('CREDITS_STEPS', () => {
    /**
     * The whole shipped credits file is built from these three: a label line, the name under it,
     * and a blank line between blocks.
     */
    it('offers the three line types a credits file is made of', () => {
        assert.deepEqual(CREDITS_STEPS.map(s => s.id), ['header', 'center', 'blank']);
    });

    it('gives a blank line the sentinel value rather than an empty one', () => {
        assert.equal(stepById('blank')?.value, '[TBL]');
    });

    it('gives the text lines their directive and no value', () => {
        assert.equal(stepById('header')?.key, 'HEADER');
        assert.equal(stepById('center')?.key, 'CENTER');
        assert.equal(stepById('center')?.value, '');
    });

    it('has no step under an unknown id', () => {
        assert.equal(stepById('nonsense'), null);
    });

    /**
     * The library and the Format column must name the same thing the same way. A tile called "Name"
     * that produces a row reading CENTER, with nothing connecting the two, is how a user ends up
     * unsure which tile they want.
     */
    it('names its steps exactly as the Format column names their directives', () => {
        for (const step of CREDITS_STEPS) {
            if (step.id === 'blank') { continue; }
            assert.equal(step.label, directiveLabel(step.key),
                `the '${step.id}' tile and the Format column disagree`);
        }
    });

    it('shows the token that identifies each kind of line in the file', () => {
        assert.equal(stepById('header')?.token, 'HEADER');
        assert.equal(stepById('center')?.token, 'CENTER');
        // A blank line is CENTER too, so its directive would not tell them apart - the sentinel does.
        assert.equal(stepById('blank')?.token, '[TBL]');
    });
});

describe('directiveLabel', () => {
    it('names every directive the engine defines', () => {
        for (const directive of CREDITS_DIRECTIVES) {
            assert.ok(directiveLabel(directive) !== null, `no plain name for '${directive}'`);
        }
    });

    it('is tolerant of the casing and padding a file may carry', () => {
        assert.equal(directiveLabel(' header '), 'Label');
    });

    it('has no name for something that is not a directive', () => {
        assert.equal(directiveLabel('CENTRE'), null);
    });
});

describe('directiveOptionText', () => {
    it('leads with the token, since that is what the file holds', () => {
        assert.equal(directiveOptionText('HEADER'), 'HEADER - Label');
        assert.equal(directiveOptionText('CENTER'), 'CENTER - Name');
    });

    /** A file may already contain a directive we do not know; it is shown as-is, never rewritten. */
    it('leaves an unrecognised directive alone', () => {
        assert.equal(directiveOptionText('CENTRE'), 'CENTRE');
    });
});

describe('dropTargetAt', () => {
    it('drops above a row when the pointer is in its top half', () => {
        assert.deepEqual(dropTargetAt(125, rows, 3), { index: 1, y: 120 });
    });

    it('drops below a row when the pointer is in its bottom half', () => {
        assert.deepEqual(dropTargetAt(135, rows, 3), { index: 2, y: 140 });
    });

    it('drops at the very top when the pointer is above every row', () => {
        assert.deepEqual(dropTargetAt(40, rows, 3), { index: 0, y: 100 });
    });

    it('appends when the pointer is below every row', () => {
        assert.deepEqual(dropTargetAt(400, rows, 3), { index: 3, y: 160 });
    });

    it('appends into an empty file', () => {
        assert.deepEqual(dropTargetAt(200, [], 0), { index: 0, y: 0 });
    });

    /**
     * With a filter on, the rows on screen are not consecutive. The drop still has to name a real
     * position in the document, so it is taken from the row it was dropped against rather than from
     * how far down the list it happened to be.
     */
    it('uses the document position of the row it was dropped against', () => {
        const filtered = [
            { index: 4, top: 100, bottom: 120 },
            { index: 90, top: 120, bottom: 140 },
        ];

        // Above the row shown first: its own index, 4 - not 0, its position on screen.
        assert.deepEqual(dropTargetAt(105, filtered, 200), { index: 4, y: 100 });
        // Below it: 5, the position after it in the document.
        assert.deepEqual(dropTargetAt(115, filtered, 200), { index: 5, y: 120 });
        // Above the row shown second: 90, not 1.
        assert.deepEqual(dropTargetAt(125, filtered, 200), { index: 90, y: 120 });
    });

    it('places the indicator on the boundary it will insert at', () => {
        assert.equal(dropTargetAt(101, rows, 3).y, 100);
        assert.equal(dropTargetAt(119, rows, 3).y, 120);
    });
});

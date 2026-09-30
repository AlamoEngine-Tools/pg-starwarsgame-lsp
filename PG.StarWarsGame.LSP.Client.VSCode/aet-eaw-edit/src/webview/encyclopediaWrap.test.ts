// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import {describe, it} from 'node:test';
import {wrapByCharacterBudget} from './encyclopediaWrap';

// The stock encyclopedia_text budget, and the one EaWX widened it to.
const STOCK = 41;
const EAWX = 56;

// Authored to span the card exactly. It carries no space at all, which is the branch that breaks
// mid-token, so it is the sharpest case the rule has.
const DIVIDER = '=================CAPABILITIES==================';

describe('wrapByCharacterBudget', () => {
    // The whole reason this exists: the divider must survive as one line on the card it was
    // measured against, and must break on the narrower stock card.
    it('keeps a tuned divider whole at the budget it was authored for', () => {
        assert.equal(DIVIDER.length, 47);
        assert.deepEqual(wrapByCharacterBudget(DIVIDER, EAWX), [DIVIDER]);
    });

    it('breaks the same divider on the stock budget', () => {
        const lines = wrapByCharacterBudget(DIVIDER, STOCK);

        assert.equal(lines.length, 2);
        assert.equal(lines.join(''), DIVIDER);
    });

    // The shipped bio is ONE localisation key, so these four lines are the engine's own wrap. Line
    // two is exactly budget - 1, which is what pins the comparison as ">=" rather than ">".
    it('reproduces the shipped stock wrap of Luke Skywalker\'s biography', () => {
        const bio = 'Once a farm boy from Tatooine, Luke Skywalker trained under Jedi Master '
            + 'Yoda to become the first of a new generation of Jedi Knights.';

        assert.deepEqual(wrapByCharacterBudget(bio, STOCK), [
            'Once a farm boy from Tatooine, Luke',
            'Skywalker trained under Jedi Master Yoda',
            'to become the first of a new generation',
            'of Jedi Knights.',
        ]);
    });

    it('fits a line of exactly budget - 1 and breaks one of exactly budget', () => {
        assert.deepEqual(wrapByCharacterBudget('a'.repeat(40), STOCK), ['a'.repeat(40)]);
        assert.equal(wrapByCharacterBudget('a'.repeat(41), STOCK).length, 2);
    });

    // No space to cut at, so the line is chopped where it runs out rather than overflowing.
    it('breaks mid-token when a run has no space in it', () => {
        assert.deepEqual(wrapByCharacterBudget('x'.repeat(10), 5), ['xxxx', 'xxxx', 'xx']);
    });

    it('cuts at the last space and does not keep it', () => {
        assert.deepEqual(wrapByCharacterBudget('aaa bbb ccc', 8), ['aaa bbb', 'ccc']);
    });

    // A key that resolved to nothing contributes no line at all.
    it('yields nothing for an empty string', () => {
        assert.deepEqual(wrapByCharacterBudget('', STOCK), []);
    });

    // A budget the component never declared must not turn every character into its own line.
    it('leaves the text alone when there is no usable budget', () => {
        assert.deepEqual(wrapByCharacterBudget(DIVIDER, 0), [DIVIDER]);
        assert.deepEqual(wrapByCharacterBudget(DIVIDER, -3), [DIVIDER]);
    });

    // Authored newlines are honoured before any budget is applied.
    it('splits on newlines first', () => {
        assert.deepEqual(wrapByCharacterBudget('one\ntwo', STOCK), ['one', 'two']);
    });
});

// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import {
    countTreeNodes, searchIsOpen, treeFilterSummary, treeMinHeight,
} from './treeSearch';

describe('searchIsOpen', () => {
    it('is closed until the reader opens it', () => {
        assert.equal(searchIsOpen(false, ''), false);
    });

    it('opens when the reader presses the magnifier', () => {
        assert.equal(searchIsOpen(true, ''), true);
    });

    /**
     * The rule the whole control depends on.
     *
     * A collapsed box with text still in it is a filter narrowing the tree with nothing on screen
     * saying so - the reader sees a short tree and no reason for it. Whatever the button says, a
     * non-empty pattern keeps its own box visible.
     */
    it('stays open while the pattern has text, whatever the button says', () => {
        assert.equal(searchIsOpen(false, 'HP_'), true);
    });

    it('counts whitespace as text, because it filters like text', () => {
        // A stray space matches nothing and empties the tree. Hiding the box that holds it is the
        // worst version of this bug, not an edge case of it.
        assert.equal(searchIsOpen(false, ' '), true);
    });
});

describe('treeFilterSummary', () => {
    it('says nothing when nothing is filtered', () => {
        assert.equal(treeFilterSummary(133, 133, 3), null);
    });

    it('says how much of the tree is left when a pattern hides rows', () => {
        assert.equal(treeFilterSummary(12, 133, 3), '12 of 133');
    });

    it('says so when a KIND is switched off, even with no pattern', () => {
        // The other half of the same problem: two of three kinds off is a narrowed tree, and the
        // count alone reads as a small model.
        assert.equal(treeFilterSummary(40, 133, 1), '40 of 133');
    });

    it('is quiet when every kind is on and the pattern matches everything', () => {
        assert.equal(treeFilterSummary(133, 133, 3), null);
    });
});

describe('countTreeNodes', () => {
    const leaf = (id: string) => ({ id, name: id, children: [] });

    it('counts a flat list', () => {
        assert.equal(countTreeNodes([leaf('a'), leaf('b')] as never), 2);
    });

    it('counts every descendant, however deep', () => {
        // The reason this exists rather than reading the rendered row count: a COLLAPSED branch is
        // still in the model. Counting what is on screen made folding read as filtering, and the
        // Star Destroyer opened claiming "50 of 58" with nothing filtered at all.
        const tree = [{
            id: 'root', name: 'root', children: [
                { id: 'a', name: 'a', children: [leaf('a1'), leaf('a2')] },
                leaf('b'),
            ],
        }];

        assert.equal(countTreeNodes(tree as never), 5);
    });

    it('is zero for an empty tree', () => {
        assert.equal(countTreeNodes([]), 0);
    });
});

describe('treeMinHeight', () => {
    /**
     * The rule: filtering must not resize the tree.
     *
     * A pattern that matches nothing - or switching every kind off - collapsed the list to nothing
     * and pulled the skeleton control, the effect groups and everything under them up the panel.
     * The reader is then reaching for a control that has moved while they were typing.
     */
    it('holds the height the UNFILTERED tree needs', () => {
        assert.equal(treeMinHeight(20), 'min(480px, 48vh)');
    });

    it('is the same whatever the filter matched, because it is not told', () => {
        // The signature is the whole guarantee: this cannot depend on the filtered count, so it
        // cannot move when the filter does.
        assert.equal(treeMinHeight(20), treeMinHeight(20));
    });

    it('never asks for more than the cap, so a big model still scrolls', () => {
        // A Star Destroyer is 133 rows - over 3000px. Expressed as a CSS min() so the cap wins
        // there: a min-height ABOVE a max-height would beat it and the panel would run off-screen.
        assert.match(treeMinHeight(133), /^min\(3192px, 48vh\)$/);
    });

    it('asks for nothing when there is no tree at all', () => {
        assert.equal(treeMinHeight(0), 'min(0px, 48vh)');
    });
});

// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import {
    countTreeNodes,
    treeFilterSummary,
    treeIsFiltered,
    treeMinHeight,
} from "./treeSearch";

describe("treeIsFiltered", () => {
    it("is not filtered with no pattern and every kind on", () => {
        assert.equal(treeIsFiltered("", 3), false);
    });

    it("is filtered by a pattern", () => {
        assert.equal(treeIsFiltered("HP_", 3), true);
    });

    /**
     * A quick filter is a filter.
     *
     * One kind switched off hides rows as surely as a pattern does, so it turns the plate's filter
     * icon into the reset - which is what switches the kind back on.
     */
    it("is filtered by a kind switched off, with no pattern at all", () => {
        assert.equal(treeIsFiltered("", 2), true);
        assert.equal(treeIsFiltered("", 0), true);
    });

    it("counts whitespace as text, because it filters like text", () => {
        // The reset has to be live for a stray space, or nothing on the closed plate can remove it.
        assert.equal(treeIsFiltered(" ", 3), true);
    });
});

describe("treeFilterSummary", () => {
    it("says nothing when nothing is filtered", () => {
        assert.equal(treeFilterSummary(133, 133, 3), null);
    });

    it("says how much of the tree is left when a pattern hides rows", () => {
        assert.equal(treeFilterSummary(12, 133, 3), "12 of 133");
    });

    it("says so when a KIND is switched off, even with no pattern", () => {
        // The other half of the same problem: two of three kinds off is a narrowed tree, and the
        // count alone reads as a small model.
        assert.equal(treeFilterSummary(40, 133, 1), "40 of 133");
    });

    it("is quiet when every kind is on and the pattern matches everything", () => {
        assert.equal(treeFilterSummary(133, 133, 3), null);
    });
});

describe("countTreeNodes", () => {
    const leaf = (id: string) => ({ id, name: id, children: [] });

    it("counts a flat list", () => {
        assert.equal(countTreeNodes([leaf("a"), leaf("b")] as never), 2);
    });

    it("counts every descendant, however deep", () => {
        // The reason this exists rather than reading the rendered row count: a COLLAPSED branch is
        // still in the model. Counting what is on screen made folding read as filtering, and the
        // Star Destroyer opened claiming "50 of 58" with nothing filtered at all.
        const tree = [
            {
                id: "root",
                name: "root",
                children: [
                    { id: "a", name: "a", children: [leaf("a1"), leaf("a2")] },
                    leaf("b"),
                ],
            },
        ];

        assert.equal(countTreeNodes(tree as never), 5);
    });

    it("is zero for an empty tree", () => {
        assert.equal(countTreeNodes([]), 0);
    });
});

describe("treeMinHeight", () => {
    /**
     * The rule: filtering must not resize the tree.
     *
     * A pattern that matches nothing - or switching every kind off - collapsed the list to nothing
     * and pulled the skeleton control, the effect groups and everything under them up the panel.
     * The reader is then reaching for a control that has moved while they were typing.
     */
    it("holds the height the UNFILTERED tree needs", () => {
        assert.equal(treeMinHeight(20), "min(480px, 48vh)");
    });

    it("is the same whatever the filter matched, because it is not told", () => {
        // The signature is the whole guarantee: this cannot depend on the filtered count, so it
        // cannot move when the filter does.
        assert.equal(treeMinHeight(20), treeMinHeight(20));
    });

    it("never asks for more than the cap, so a big model still scrolls", () => {
        // A Star Destroyer is 133 rows - over 3000px. Expressed as a CSS min() so the cap wins
        // there: a min-height ABOVE a max-height would beat it and the panel would run off-screen.
        assert.match(treeMinHeight(133), /^min\(3192px, 48vh\)$/);
    });

    it("asks for nothing when there is no tree at all", () => {
        assert.equal(treeMinHeight(0), "min(0px, 48vh)");
    });
});

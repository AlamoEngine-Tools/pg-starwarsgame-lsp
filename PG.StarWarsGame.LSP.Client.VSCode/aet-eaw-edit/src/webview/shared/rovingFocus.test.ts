// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { menuStep, rowStep, treeStep } from "./rovingFocus";

describe("menuStep", () => {
    // Items 1 and 3 are disabled, as an action the entry does not offer.
    const enabled = [true, false, true, false, true];

    it("walks down and up over the enabled items only", () => {
        assert.equal(menuStep(enabled, 0, "ArrowDown"), 2);
        assert.equal(menuStep(enabled, 2, "ArrowUp"), 0);
    });

    it("wraps around at either end, as the editor's own menus", () => {
        assert.equal(menuStep(enabled, 4, "ArrowDown"), 0);
        assert.equal(menuStep(enabled, 0, "ArrowUp"), 4);
    });

    it("goes to the first and last enabled item on Home and End", () => {
        assert.equal(menuStep(enabled, 2, "Home"), 0);
        assert.equal(menuStep(enabled, 2, "End"), 4);
    });

    it("starts from the first enabled item when nothing has focus", () => {
        assert.equal(menuStep(enabled, -1, "ArrowDown"), 0);
    });

    it("ignores any other key and a menu with nothing enabled", () => {
        assert.equal(menuStep(enabled, 2, "a"), undefined);
        assert.equal(menuStep([false, false], 0, "ArrowDown"), undefined);
    });
});

describe("rowStep", () => {
    it("moves one row, and a page of rows, clamped to the table", () => {
        assert.equal(rowStep(10, 3, "ArrowDown", 4), 4);
        assert.equal(rowStep(10, 0, "ArrowUp", 4), 0);
        assert.equal(rowStep(10, 3, "PageDown", 4), 7);
        assert.equal(rowStep(10, 8, "PageDown", 4), 9);
        assert.equal(rowStep(10, 2, "PageUp", 4), 0);
    });

    it("goes to the first and last row on Home and End", () => {
        assert.equal(rowStep(10, 5, "Home", 4), 0);
        assert.equal(rowStep(10, 5, "End", 4), 9);
    });

    it("ignores any other key and an empty table", () => {
        assert.equal(rowStep(10, 5, "Enter", 4), undefined);
        assert.equal(rowStep(0, 0, "ArrowDown", 4), undefined);
    });
});

describe("treeStep", () => {
    // Game Objects (open) > Star_Destroyer (open) > Models (closed), Hardpoints (leaf); Models (closed).
    const rows = [
        { depth: 0, expandable: true, expanded: true },
        { depth: 1, expandable: true, expanded: true },
        { depth: 2, expandable: true, expanded: false },
        { depth: 2, expandable: false, expanded: false },
        { depth: 0, expandable: true, expanded: false },
    ];

    it("walks the visible rows as a table does", () => {
        assert.deepEqual(treeStep(rows, 1, "ArrowDown", 10), { to: 2 });
        assert.deepEqual(treeStep(rows, 0, "ArrowUp", 10), { to: 0 });
        assert.deepEqual(treeStep(rows, 2, "End", 10), { to: 4 });
        assert.deepEqual(treeStep(rows, 3, "Home", 10), { to: 0 });
    });

    it("expands a closed row on the right, then goes to its first child", () => {
        assert.deepEqual(treeStep(rows, 2, "ArrowRight", 10), { expand: true });
        assert.deepEqual(treeStep(rows, 1, "ArrowRight", 10), { to: 2 });
        assert.equal(treeStep(rows, 3, "ArrowRight", 10), undefined);
    });

    it("collapses an open row on the left, otherwise goes to its parent", () => {
        assert.deepEqual(treeStep(rows, 1, "ArrowLeft", 10), {
            collapse: true,
        });
        assert.deepEqual(treeStep(rows, 3, "ArrowLeft", 10), { to: 1 });
        assert.deepEqual(treeStep(rows, 2, "ArrowLeft", 10), { to: 1 });
        assert.equal(treeStep(rows, 4, "ArrowLeft", 10), undefined);
    });

    it("ignores any other key and an empty tree", () => {
        assert.equal(treeStep(rows, 0, "Enter", 10), undefined);
        assert.equal(treeStep([], 0, "ArrowDown", 10), undefined);
    });
});

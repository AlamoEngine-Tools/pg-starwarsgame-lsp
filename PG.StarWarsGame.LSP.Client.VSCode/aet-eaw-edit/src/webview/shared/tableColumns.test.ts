// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { resetPanelLayoutForTests } from "./panelLayout";
import {
    moveColumn,
    orderedKeys,
    readColumnLayout,
    writeColumnOrder,
    writeColumnWidth,
} from "./tableColumns";

const KEYS = ["name", "type", "kinds", "origin"];

describe("orderedKeys", () => {
    it("keeps the table's order when the reader has none", () => {
        assert.deepEqual(orderedKeys(KEYS, []), KEYS);
    });

    // A column the stored order does not name - one added since - goes where the table puts it.
    it("takes the reader's order, then any column it does not name", () => {
        assert.deepEqual(orderedKeys(KEYS, ["origin", "name"]), [
            "origin",
            "name",
            "type",
            "kinds",
        ]);
    });

    it("drops a stored column the table no longer has", () => {
        assert.deepEqual(orderedKeys(["name", "type"], ["gone", "type"]), [
            "type",
            "name",
        ]);
    });
});

describe("moveColumn", () => {
    it("puts a dragged column in the place of the one it is dropped on", () => {
        assert.deepEqual(moveColumn(KEYS, "origin", "type"), [
            "name",
            "origin",
            "type",
            "kinds",
        ]);
        assert.deepEqual(moveColumn(KEYS, "name", "kinds"), [
            "type",
            "kinds",
            "name",
            "origin",
        ]);
    });

    it("leaves the order as it is for a column dropped on itself", () => {
        assert.deepEqual(moveColumn(KEYS, "type", "type"), KEYS);
    });
});

describe("the column layout store", () => {
    it("keeps a list's widths and order, and nothing for another list", () => {
        resetPanelLayoutForTests();
        writeColumnWidth("models", "name", 320);
        writeColumnOrder("models", ["origin", "name", "type", "kinds"]);

        assert.deepEqual(readColumnLayout("models", KEYS), {
            order: ["origin", "name", "type", "kinds"],
            widths: { name: 320 },
        });
        assert.deepEqual(readColumnLayout("textures", KEYS), {
            order: KEYS,
            widths: {},
        });
    });
});

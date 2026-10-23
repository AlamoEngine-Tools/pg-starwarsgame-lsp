// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { nextSort } from "./tableSort";

describe("nextSort", () => {
    it("starts a column ascending", () => {
        assert.deepEqual(nextSort(null, "key"), {
            column: "key",
            direction: "asc",
        });
    });

    it("turns ascending into descending on the same column", () => {
        assert.deepEqual(nextSort({ column: "key", direction: "asc" }, "key"), {
            column: "key",
            direction: "desc",
        });
    });

    // A third click restores document order rather than cycling back to ascending: in a keyed file
    // that is the order on disk, and there would otherwise be no way back to it.
    it("clears the sort on the third click", () => {
        assert.equal(
            nextSort({ column: "key", direction: "desc" }, "key"),
            null,
        );
    });

    it("starts a different column ascending again", () => {
        assert.deepEqual(
            nextSort({ column: "key", direction: "desc" }, "GERMAN"),
            { column: "GERMAN", direction: "asc" },
        );
    });
});

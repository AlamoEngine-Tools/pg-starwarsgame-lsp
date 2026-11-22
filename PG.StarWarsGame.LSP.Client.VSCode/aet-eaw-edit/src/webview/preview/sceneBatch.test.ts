// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { asOneChange, type Batching } from "./sceneBatch";

/** A layer that writes down when its batch opens and when it ends. */
function layer(name: string, log: string[]): Batching {
    return {
        batch: (work) => {
            log.push(`${name} held`);
            try {
                work();
            } finally {
                log.push(`${name} settled`);
            }
        },
    };
}

describe("asOneChange", () => {
    // The depth range is measured by what is drawn, and the rows are what decide that.
    it("settles the rows before the depth range", () => {
        const log: string[] = [];

        asOneChange(layer("rows", log), layer("depth range", log), () => {
            log.push("work");
        });

        assert.deepEqual(log, [
            "depth range held",
            "rows held",
            "work",
            "rows settled",
            "depth range settled",
        ]);
    });

    it("settles both when the work fails", () => {
        const log: string[] = [];

        assert.throws(() =>
            asOneChange(layer("rows", log), layer("depth range", log), () => {
                throw new Error("a step failed");
            }),
        );

        assert.deepEqual(log.slice(-2), [
            "rows settled",
            "depth range settled",
        ]);
    });
});

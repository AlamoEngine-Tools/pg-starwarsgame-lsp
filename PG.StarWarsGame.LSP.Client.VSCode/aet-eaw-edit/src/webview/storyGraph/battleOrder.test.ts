// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { battleOrderEdges, boxNodeSize } from "./battleOrder";

const stub = (id: string, rank: number, count = 3) => ({
    id,
    battleOrder: { rank, count },
});
const plain = (id: string) => ({ id });
const edge = (fromId: string, toId: string) => ({ fromId, toId });

describe("battleOrderEdges", () => {
    it("joins each battle's stand-in to the one played after it", () => {
        // In the order the server listed the nodes, which is not the order of play.
        const order = battleOrderEdges(
            [stub("c", 2), stub("a", 0), plain("x"), stub("b", 1)],
            [],
        );

        assert.deepEqual(order, [
            { source: "a", target: "b" },
            { source: "b", target: "c" },
        ]);
    });

    it("has nothing to say about fewer than two battles", () => {
        assert.deepEqual(battleOrderEdges([plain("x")], []), []);
        assert.deepEqual(
            battleOrderEdges([stub("a", 0, 1), plain("x")], []),
            [],
        );
    });

    it("joins across a battle the filter left out", () => {
        assert.deepEqual(battleOrderEdges([stub("a", 0), stub("c", 2)], []), [
            { source: "a", target: "c" },
        ]);
    });

    // A layered layout cannot put b left of a when the story itself leads from b to a: it would
    // break the loop by turning one of the story's own edges round.
    it("leaves a pair out when the story already leads the other way", () => {
        const order = battleOrderEdges(
            [stub("a", 0), stub("b", 1), plain("x")],
            [edge("b", "x"), edge("x", "a")],
        );

        assert.deepEqual(order, []);
    });

    it("goes on with the next pair after one it left out", () => {
        const order = battleOrderEdges(
            [stub("a", 0), stub("b", 1), stub("c", 2)],
            [edge("b", "a")],
        );

        assert.deepEqual(order, [{ source: "b", target: "c" }]);
    });

    it("closes no loop through a pair it added itself", () => {
        // c leads to a in the story; a-b is added, and b-c would then run c, a, b, c.
        const order = battleOrderEdges(
            [stub("a", 0), stub("b", 1), stub("c", 2)],
            [edge("c", "a")],
        );

        assert.deepEqual(order, [{ source: "a", target: "b" }]);
    });
});

describe("boxNodeSize", () => {
    it("is one line high for a box that only carries its label", () => {
        assert.deepEqual(boxNodeSize({ label: "Short" }), { w: 140, h: 40 });
    });

    it("grows with the label up to thirty characters", () => {
        assert.equal(boxNodeSize({ label: "x".repeat(24) }).w, 24 * 6.5 + 32);
        assert.equal(
            boxNodeSize({ label: "x".repeat(60) }).w,
            boxNodeSize({ label: "x".repeat(30) }).w,
        );
    });

    it("makes room for the second line a battle's stand-in carries", () => {
        const size = boxNodeSize({
            label: "Story_Plots_M2_Land",
            battleOrder: { rank: 0, count: 9 },
        });

        assert.equal(size.h, 54);
    });
});

// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { disabledFlicker, engineBrightness } from "./engineGlow";

describe("engineBrightness", () => {
    it("is a fifth at rest and full at full speed, straight between", () => {
        assert.equal(engineBrightness(0, 1.5), 0.2);
        assert.equal(engineBrightness(1, 1.5), 1);
        assert.ok(Math.abs(engineBrightness(0.5, 1.5) - 0.6) < 1e-9);
        assert.ok(Math.abs(engineBrightness(0.25, 1.5) - 0.4) < 1e-9);
    });

    it("stays within nothing and full whatever the speed", () => {
        assert.equal(engineBrightness(3, 1.5), 1);
        assert.equal(engineBrightness(-2, 1.5), 0);
    });

    // Measured in the game: the brightness is worked out only for a Max_Speed above 0.
    it("is dark for a type that cannot move", () => {
        assert.equal(engineBrightness(1, 0), 0);
        assert.equal(engineBrightness(1, -1), 0);
    });

    it("takes a type that writes no Max_Speed as one that can move", () => {
        assert.equal(engineBrightness(1, null), 1);
        assert.equal(engineBrightness(0, null), 0.2);
    });
});

describe("disabledFlicker", () => {
    const frames = Array.from({ length: 90 }, (_, frame) =>
        disabledFlicker(frame / 30 + 0.001),
    );

    it("stays between nothing and a fifth", () => {
        assert.ok(frames.every((value) => value >= 0 && value <= 0.2));
    });

    it("is not steady", () => {
        assert.ok(new Set(frames).size > 60);
        assert.ok(Math.max(...frames) - Math.min(...frames) > 0.1);
    });

    it("holds one value through a game frame", () => {
        assert.equal(disabledFlicker(1.001), disabledFlicker(1.03));
        assert.notEqual(disabledFlicker(1.001), disabledFlicker(1.04));
    });

    it("is the same on every run", () => {
        assert.equal(disabledFlicker(2.5), disabledFlicker(2.5));
    });
});

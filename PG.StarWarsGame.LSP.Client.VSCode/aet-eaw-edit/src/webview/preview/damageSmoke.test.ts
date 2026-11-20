// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { DamageSmoke, smokeDue } from "./damageSmoke";

describe("smokeDue (#451)", () => {
    // Measured in the game: a hit adds the smoke when it takes the hull from above 75 % to 75 % or
    // below, or from above 50 % to 50 % or below, and the unit still lives.
    it("adds the smoke when a hit crosses 75 %", () => {
        assert.equal(smokeDue(80, 75, 100), true);
        assert.equal(smokeDue(76, 60, 100), true);
    });

    it("adds the smoke when a hit crosses 50 %", () => {
        assert.equal(smokeDue(60, 50, 100), true);
        assert.equal(smokeDue(51, 10, 100), true);
    });

    it("adds nothing for a hit that crosses neither", () => {
        assert.equal(smokeDue(100, 76, 100), false);
        assert.equal(smokeDue(75, 60, 100), false, "already at 75 %");
        assert.equal(smokeDue(50, 20, 100), false, "already at 50 %");
    });

    it("adds nothing for a repair, whichever line it crosses", () => {
        assert.equal(smokeDue(40, 90, 100), false);
    });

    it("adds nothing for the hit that kills", () => {
        assert.equal(smokeDue(80, 0, 100), false);
        assert.equal(smokeDue(80, -5, 100), false);
    });

    it("adds nothing for a unit with no hull to measure against", () => {
        assert.equal(smokeDue(0, 0, 0), false);
    });
});

describe("DamageSmoke (#451)", () => {
    it("gains the effect at 75 % and again at 50 %", () => {
        const smoke = new DamageSmoke("P_Smoke_Small02.alo");

        assert.equal(smoke.observe(100, 100), null, "the opening hull");
        const first = smoke.observe(70, 100);
        assert.equal(smoke.observe(60, 100), null);
        const second = smoke.observe(45, 100);

        assert.notEqual(first, null);
        assert.notEqual(second, null);
        assert.notEqual(first, second, "each smoke is a system of its own");
    });

    // Measured in the game: one hit through both lines adds ONE smoke - the two are one test, not
    // two adds.
    it("adds one smoke for a hit that crosses both lines", () => {
        const smoke = new DamageSmoke("P_Smoke_Small02.alo");
        smoke.observe(100, 100);

        assert.notEqual(smoke.observe(40, 100), null);
        assert.equal(smoke.observe(30, 100), null);
    });

    // Measured in the game: nothing in the damage path takes the smoke away again, and nothing
    // remembers that it was added - so a unit repaired above a line gains another when it falls
    // through it again.
    it("adds another smoke when a repaired unit falls through a line again", () => {
        const smoke = new DamageSmoke("P_Smoke_Small02.alo");
        smoke.observe(100, 100);
        const first = smoke.observe(70, 100);

        assert.equal(smoke.observe(100, 100), null, "a repair adds nothing");
        const again = smoke.observe(70, 100);

        assert.notEqual(again, null);
        assert.notEqual(again, first);
    });

    it("gains nothing for a type that names no smoke", () => {
        for (const asset of [null, undefined, ""]) {
            const smoke = new DamageSmoke(asset);
            smoke.observe(100, 100);

            assert.equal(smoke.observe(70, 100), null);
            assert.equal(smoke.observe(40, 100), null);
        }
    });

    it("never adds the smoke for the hull it first sees", () => {
        assert.equal(
            new DamageSmoke("P_Smoke_Small02.alo").observe(40, 100),
            null,
        );
    });

    // Measured in the game: the dying object is destroyed when it dies, and the wreck or the
    // spinning hull is a NEW object made from its type - so no smoke a hit added outlives the death.
    it("hands over every smoke it added, once, for the death to take away", () => {
        const smoke = new DamageSmoke("P_Smoke_Small02.alo");
        smoke.observe(100, 100);
        const first = smoke.observe(70, 100);
        const second = smoke.observe(45, 100);

        assert.deepEqual(smoke.drop(), [first, second]);
        assert.deepEqual(smoke.drop(), [], "dropped once");
    });

    it("keeps every smoke through a repair: a repair takes none away", () => {
        const smoke = new DamageSmoke("P_Smoke_Small02.alo");
        smoke.observe(100, 100);
        const first = smoke.observe(70, 100);
        smoke.observe(100, 100);

        assert.deepEqual(smoke.drop(), [first]);
    });

    it("starts again from the hull it sees after the death", () => {
        const smoke = new DamageSmoke("P_Smoke_Small02.alo");
        smoke.observe(100, 100);
        smoke.observe(70, 100);
        smoke.observe(0, 100);
        smoke.drop();

        assert.equal(smoke.observe(100, 100), null, "the revived hull");
        assert.notEqual(
            smoke.observe(70, 100),
            null,
            "a revived unit smokes again",
        );
        assert.equal(smoke.drop().length, 1);
    });
});

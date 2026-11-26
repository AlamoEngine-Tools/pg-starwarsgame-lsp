// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { anyFilterSet } from "./graphFilters";

const none = {
    nameFilter: "",
    branch: "",
    lifecycle: "",
    reachableFrom: "",
    plotState: "",
};

describe("anyFilterSet", () => {
    it("is false with nothing set", () => {
        assert.equal(anyFilterSet(none), false);
    });

    it("is true as soon as any one of them is set", () => {
        for (const filters of [
            { ...none, nameFilter: "destroy" },
            { ...none, branch: "Empire" },
            { ...none, lifecycle: "Armed" },
            { ...none, reachableFrom: "Event_01" },
            // A plot's registration state - Active_Plot or Suspended_Plot in the faction's
            // manifest - which is a different question from an event's lifecycle.
            { ...none, plotState: "Suspended" },
        ]) {
            assert.equal(anyFilterSet(filters), true, JSON.stringify(filters));
        }
    });

    /** The mode narrows nothing without a pattern, and the reset keeps it. */
    it("does not count the mode a pattern is read in", () => {
        assert.equal(anyFilterSet({ ...none, nameFilterMode: "regex" }), false);
    });

    it("treats an absent field as unset, since the protocol makes every one optional", () => {
        assert.equal(anyFilterSet({}), false);
    });
});

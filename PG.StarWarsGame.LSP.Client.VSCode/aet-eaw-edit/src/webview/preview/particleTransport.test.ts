// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { particleStep } from "./particleTransport";

describe("particleStep", () => {
    it("pauses a running system and plays it on from the same frame", () => {
        assert.deepEqual(particleStep("playing", "pause"), {
            run: "paused",
            viewport: null,
        });
        assert.deepEqual(particleStep("paused", "play"), {
            run: "playing",
            viewport: null,
        });
    });

    it("stops from either state, and lets a paused one run on so the air clears", () => {
        assert.deepEqual(particleStep("playing", "stop"), {
            run: "stopped",
            viewport: "stop",
        });
        assert.deepEqual(particleStep("paused", "stop"), {
            run: "stopped",
            viewport: "stop",
        });
    });

    it("tells the viewport nothing for a stop that is already standing", () => {
        assert.deepEqual(particleStep("stopped", "stop"), {
            run: "stopped",
            viewport: null,
        });
    });

    /** A CD player's Play after Stop starts from the top; for a burst the top is the whole effect. */
    it("plays a stopped system from nothing", () => {
        assert.deepEqual(particleStep("stopped", "play"), {
            run: "playing",
            viewport: "restart",
        });
    });

    it("restarts from every state and plays", () => {
        for (const run of ["playing", "paused", "stopped"] as const) {
            assert.deepEqual(particleStep(run, "restart"), {
                run: "playing",
                viewport: "restart",
            });
        }
    });

    it("leaves a stopped system stopped on pause", () => {
        assert.deepEqual(particleStep("stopped", "pause"), {
            run: "stopped",
            viewport: null,
        });
    });
});

// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { particleNotices } from "./particleNotices";

describe("particleNotices", () => {
    it("turns each notice into a warning named after the system", () => {
        const problems = particleNotices("P_EXPLOSION_BIG00.ALO", {
            notices: [
                "Emitter 5 - chunk 0x7F not read by the game, skipped",
                "Emitter 5 - property 0x7E not read by the game, skipped",
            ],
        });

        assert.deepEqual(problems, [
            {
                severity: "warning",
                message:
                    "P_EXPLOSION_BIG00.ALO: Emitter 5 - chunk 0x7F not read by the game, skipped",
                hardpointId: null,
            },
            {
                severity: "warning",
                message:
                    "P_EXPLOSION_BIG00.ALO: Emitter 5 - property 0x7E not read by the game, skipped",
                hardpointId: null,
            },
        ]);
    });

    it("gives a shipped file, which carries no notice, nothing", () => {
        assert.deepEqual(particleNotices("p_smoke.alo", { notices: [] }), []);
        assert.deepEqual(particleNotices("p_smoke.alo", {}), []);
        assert.deepEqual(particleNotices("p_smoke.alo", { notices: null }), []);
    });
});

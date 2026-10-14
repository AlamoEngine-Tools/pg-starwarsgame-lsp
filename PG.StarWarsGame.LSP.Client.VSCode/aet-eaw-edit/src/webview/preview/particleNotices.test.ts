// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { particleNotices } from "./particleNotices";

describe("particleNotices", () => {
    it("keeps each problem's id and severity and names the system in front", () => {
        const problems = particleNotices("P_EXPLOSION_BIG00.ALO", {
            problems: [
                {
                    diagnosticId: "aetswg-014-0022",
                    severity: "warning",
                    message:
                        "Emitter 5 - chunk 0x7F not read by the game, skipped",
                },
                {
                    diagnosticId: "aetswg-014-0023",
                    severity: "info",
                    message:
                        "Emitter 5 - link group 3, Particle Editor data the game does not read",
                },
            ],
        });

        assert.deepEqual(problems, [
            {
                diagnosticId: "aetswg-014-0022",
                severity: "warning",
                message:
                    "P_EXPLOSION_BIG00.ALO: Emitter 5 - chunk 0x7F not read by the game, skipped",
                hardpointId: null,
            },
            {
                diagnosticId: "aetswg-014-0023",
                severity: "info",
                message:
                    "P_EXPLOSION_BIG00.ALO: Emitter 5 - link group 3, Particle Editor data the game does not read",
                hardpointId: null,
            },
        ]);
    });

    it("gives a shipped file, which carries no problem, nothing", () => {
        assert.deepEqual(particleNotices("p_smoke.alo", { problems: [] }), []);
        assert.deepEqual(particleNotices("p_smoke.alo", {}), []);
        assert.deepEqual(
            particleNotices("p_smoke.alo", { problems: null }),
            [],
        );
    });
});

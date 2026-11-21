// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { particleNotices, withParticleNotices } from "./particleNotices";

describe("particleNotices", () => {
    it("keeps each problem's id and severity and names the system in front", () => {
        const problems = particleNotices("P_EXPLOSION_BIG00.ALO", {
            problems: [
                {
                    diagnosticId: "aetswg-014-0022",
                    severity: "warning",
                    message:
                        "Emitter 5 - chunk 0x7F is not read by the game and has no effect",
                },
                {
                    diagnosticId: "aetswg-014-0023",
                    severity: "info",
                    message:
                        "Emitter 5 - the Particle Editor's link group is not read by the game and has no effect",
                },
            ],
        });

        assert.deepEqual(problems, [
            {
                diagnosticId: "aetswg-014-0022",
                severity: "warning",
                message:
                    "P_EXPLOSION_BIG00.ALO: Emitter 5 - chunk 0x7F is not read by the game and has no effect",
                hardpointId: null,
            },
            {
                diagnosticId: "aetswg-014-0023",
                severity: "info",
                message:
                    "P_EXPLOSION_BIG00.ALO: Emitter 5 - the Particle Editor's link group is not read by the game and has no effect",
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

    // #394: two damage-stage proxies name one file - p_rb_station_damage_ALT1 and _ALT2 - and the
    // server reads that one file for each. Named after the file it read, the notes are one note.
    it("names the file the server read rather than the name it was asked for", () => {
        const problems = particleNotices("p_rb_station_damage_ALT2", {
            file: "p_rb_station_damage.alo",
            problems: [
                {
                    diagnosticId: "aetswg-014-0023",
                    severity: "info",
                    message: "Emitters 0-2 - property 0x06",
                },
            ],
        });

        assert.equal(
            problems[0].message,
            "p_rb_station_damage.alo: Emitters 0-2 - property 0x06",
        );
    });
});

describe("withParticleNotices", () => {
    const note = (message: string) => ({
        diagnosticId: "aetswg-014-0023",
        severity: "info",
        message,
        hardpointId: null,
    });

    it("adds a file's notes once, however many proxies asked for the file", () => {
        const first = withParticleNotices(
            [{ severity: "warning", message: "Something else" }],
            [note("p_x.alo: Emitter 0 - property 0x06")],
        );
        const second = withParticleNotices(first, [
            note("p_x.alo: Emitter 0 - property 0x06"),
        ]);

        assert.equal(second, first);
        assert.equal(second.length, 2);
    });

    it("keeps a note of another file, and another id, beside it", () => {
        const merged = withParticleNotices(
            [note("p_x.alo: Emitter 0 - property 0x06")],
            [
                note("p_y.alo: Emitter 0 - property 0x06"),
                {
                    ...note("p_x.alo: Emitter 0 - property 0x06"),
                    diagnosticId: "aetswg-014-0022",
                },
            ],
        );

        assert.equal(merged.length, 3);
    });
});

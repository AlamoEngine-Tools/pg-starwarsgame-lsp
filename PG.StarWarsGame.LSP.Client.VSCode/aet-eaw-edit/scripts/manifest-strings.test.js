// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

const assert = require("node:assert/strict");
const { describe, it } = require("node:test");
const fs = require("node:fs");
const path = require("node:path");

const { userFacingStrings, literals, unresolved } = require("./manifest-strings");

const CLIENT = path.join(__dirname, "..");

describe("manifest strings", () => {
    it("finds the strings a reader sees and leaves ids and values alone", () => {
        const manifest = {
            name: "aet-eaw-edit",
            displayName: "Shown",
            contributes: {
                commands: [{ command: "x.run", title: "Run" }],
                configuration: [
                    {
                        title: "General",
                        properties: {
                            "x.mode": {
                                type: "string",
                                enum: ["a", "b"],
                                markdownDescription: "Mode",
                                enumItemLabels: ["A", "B"],
                            },
                        },
                    },
                ],
            },
        };

        const found = userFacingStrings(manifest).map((s) => s.value);

        assert.deepEqual(found.sort(), ["A", "B", "General", "Mode", "Run", "Shown"]);
    });

    it("reports a literal and accepts a key", () => {
        const manifest = {
            contributes: {
                commands: [
                    { command: "x.run", title: "Run" },
                    { command: "x.stop", title: "%command.x.stop.title%" },
                ],
            },
        };

        assert.deepEqual(
            literals(manifest).map((s) => s.value),
            ["Run"],
        );
    });

    it("reports a key the catalogue does not carry", () => {
        const manifest = {
            contributes: { commands: [{ command: "x.run", title: "%command.x.run.title%" }] },
        };

        assert.deepEqual(unresolved(manifest, {}), ["command.x.run.title"]);
        assert.deepEqual(unresolved(manifest, { "command.x.run.title": "Run" }), []);
    });

    /** Every string a reader sees in the extension's manifest comes from `package.nls.json`. */
    it("holds no literal in the shipped manifest, and every key resolves", () => {
        const manifest = JSON.parse(fs.readFileSync(path.join(CLIENT, "package.json"), "utf8"));
        const nlsPath = path.join(CLIENT, "package.nls.json");
        const nls = fs.existsSync(nlsPath) ? JSON.parse(fs.readFileSync(nlsPath, "utf8")) : {};

        assert.deepEqual(literals(manifest).map((s) => s.path), []);
        assert.deepEqual(unresolved(manifest, nls), []);
    });
});

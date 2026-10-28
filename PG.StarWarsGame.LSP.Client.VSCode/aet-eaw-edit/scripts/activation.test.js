// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The extension activates on every folder that starts a server (#295).
//
// A mod's folder without a project file starts a server and is offered one (modLayout.ts), but the
// extension only ever activated on a .pgproj or an opened XML or Lua file: such a folder sat with no
// server and no offer until a file of it was opened.

const assert = require("node:assert/strict");
const {describe, it} = require("node:test");
const fs = require("node:fs");
const path = require("node:path");

const manifest = JSON.parse(fs.readFileSync(path.join(__dirname, "..", "package.json"), "utf8"));
const events = manifest.activationEvents ?? [];

describe("activation", () => {
    it("activates on a project file", () => {
        assert.ok(events.includes("workspaceContains:**/*.pgproj"));
    });

    it("activates on a mod's own description", () => {
        assert.ok(events.includes("workspaceContains:modinfo.json"));
    });

    it("activates on a mod's Data folder, in the spellings mods use", () => {
        assert.ok(events.includes("workspaceContains:{Data,data,DATA}/**"));
    });

    it("activates on a window opened at Data itself", () => {
        const atData = events.find((e) => e.startsWith("workspaceContains:{XML,"));
        assert.ok(atData, "no activation for a window opened at Data");
        for (const folder of ["XML", "Scripts", "Art", "Audio", "Text"]) {
            assert.ok(atData.includes(folder), `${folder} missing from ${atData}`);
        }
    });
});

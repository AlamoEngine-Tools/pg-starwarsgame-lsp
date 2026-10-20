// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

const assert = require("node:assert/strict");
const {describe, it} = require("node:test");

const {resxEntries, manifestEntries, problemsIn} = require("./catalogue-texts");

describe("catalogue texts", () => {
    it("reads a resx value, unescaped, and marks title keys as labels", () => {
        const xml =
            '<root><data name="FixTitle_Apply" xml:space="preserve"><value>Apply &lt;{0}&gt;.</value></data>' +
            '<data name="Thing_Missing" xml:space="preserve"><value>It is missing.</value></data></root>';
        assert.deepEqual(resxEntries(xml), [
            {source: "ServerMessages.resx", key: "FixTitle_Apply", text: "Apply <{0}>.", label: true},
            {source: "ServerMessages.resx", key: "Thing_Missing", text: "It is missing.", label: false},
        ]);
    });

    it("marks a command title as a label and a setting description as prose", () => {
        const manifest = {
            contributes: {
                commands: [{command: "a.b", title: "%cmd.title%"}],
                configuration: {properties: {"a.c": {description: "%cfg.description%"}}},
            },
        };
        const nls = {"cmd.title": "Rebuild", "cfg.description": "Rebuilds it."};
        assert.deepEqual(manifestEntries(manifest, nls), [
            {source: "package.nls.json", key: "cmd.title", text: "Rebuild", label: true},
            {source: "package.nls.json", key: "cfg.description", text: "Rebuilds it.", label: false},
        ]);
    });

    it("reports each broken rule with its source and key", () => {
        const found = problemsIn([
            {source: "s", key: "k", text: "Open your file.", label: false},
            {source: "s", key: "ok", text: "Fine.", label: false},
        ]);
        assert.deepEqual(found, ["s k: second-person - 'your' addresses the reader"]);
    });
});

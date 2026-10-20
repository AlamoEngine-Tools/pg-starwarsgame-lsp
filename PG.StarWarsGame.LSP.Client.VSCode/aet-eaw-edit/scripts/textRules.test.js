// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

const assert = require("node:assert/strict");
const {describe, it} = require("node:test");

const {textProblems} = require("./textRules");

const rules = (text, options) => textProblems(text, options).map((p) => p.rule);

describe("text rules", () => {
    it("passes a plain sentence", () => {
        assert.deepEqual(rules("The file '{0}' could not be read."), []);
    });

    it("finds a character outside ASCII", () => {
        assert.deepEqual(rules("Loading…"), ["ascii"]);
    });

    it("finds the reader addressed in second person", () => {
        assert.deepEqual(rules("Open the folder that holds your project."), ["second-person"]);
        assert.deepEqual(rules("If you want to start over, delete it."), ["second-person"]);
    });

    it("leaves second person inside code and placeholders alone", () => {
        assert.deepEqual(rules("Set `yourSetting` to {0}."), []);
    });

    it("finds a lower-case sentence after a colon", () => {
        assert.deepEqual(rules("Cannot convert: no target folder was given."), ["capital-after-colon"]);
    });

    it("accepts a capital, a value or code after a colon", () => {
        assert.deepEqual(rules("Cannot convert: The folder is missing."), []);
        assert.deepEqual(rules("Preview: {0}"), []);
        assert.deepEqual(rules("Set it: 'aet-eaw-edit.lsp.locale' decides."), []);
        assert.deepEqual(rules("Enable: `aet-eaw-edit.features.story`"), []);
        assert.deepEqual(rules("**Format:** float in `[0.0, 1.0]`"), []);
    });

    it("finds a full stop on a one-clause label", () => {
        assert.deepEqual(rules("Clear all filters.", {label: true}), ["label-stop"]);
    });

    it("keeps the stops of a label that runs to two sentences", () => {
        assert.deepEqual(rules("Clears the filters. The list shows everything again.", {label: true}), []);
    });

    it("leaves an ellipsis and a message's full stop alone", () => {
        assert.deepEqual(rules("Loading...", {label: true}), []);
        assert.deepEqual(rules("The file is empty."), []);
    });

    it("finds a count of the game's own data", () => {
        assert.deepEqual(rules("On the base game that is 18,281 files."), ["corpus-count"]);
        assert.deepEqual(rules("Declared by 1153 objects in the game."), ["corpus-count"]);
    });

    it("leaves versions, hex values and code numbers alone", () => {
        assert.deepEqual(rules("Shader Model 2.0, e.g. 0x0200 or version 0.2.0."), []);
        assert.deepEqual(rules("Four integers `0`-`255`."), []);
    });

    it("leaves limits, ports and links alone", () => {
        assert.deepEqual(rules("A member of 255 bytes or longer crashes the game."), []);
        assert.deepEqual(rules("The first free port from 1234 upward."), []);
        assert.deepEqual(rules("Expected [0, 360] against a 600-line reference."), []);
        assert.deepEqual(rules("Used by the [Text Editor](https://example.org/mods/1859)."), []);
    });
});

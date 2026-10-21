// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import {
    kindChoices,
    originChoices,
    originLabel,
    pageLabel,
    reasonLabel,
    relationLabel,
} from "./modBrowserModel";

/** The Mod Browser's words for the server's keys (#270). */
describe("pageLabel", () => {
    it("names the game objects' list and keeps every other XML type's own name", () => {
        assert.equal(pageLabel("xml:GameObjectType"), "Game Objects");
        assert.equal(pageLabel("xml:SFXEvent"), "SFXEvent");
        assert.equal(pageLabel("particleSystems"), "Particle Systems");
        assert.equal(pageLabel("luaScripts"), "Lua Scripts");
    });

    it("keeps a key it does not know as written", () => {
        assert.equal(pageLabel("somethingNew"), "somethingNew");
    });
});

describe("relationLabel", () => {
    it("names each relation", () => {
        assert.equal(relationLabel("referencedBy"), "Referenced by");
        assert.equal(relationLabel("sfxEvents"), "SFX Events");
        assert.equal(relationLabel("usedBy"), "Used by");
    });
});

describe("originLabel", () => {
    const layers = [
        { rank: 0, name: "Vanilla" },
        { rank: 1, name: "Core" },
        { rank: 2, name: "Mod" },
    ];

    it("names the root project this mod, a dependency by its name, and the game", () => {
        assert.equal(originLabel("layer:2", "Mod", layers), "This mod");
        assert.equal(originLabel("layer:1", "Core", layers), "Core");
        assert.equal(originLabel("game", null, layers), "Game");
        assert.equal(originLabel("missing", null, layers), "Not found");
    });

    it("names a layer by the outline's list when the row carries no name", () => {
        assert.equal(originLabel("layer:1", null, layers), "Core");
    });
});

describe("reasonLabel", () => {
    it("says why a relation lists nothing", () => {
        assert.equal(reasonLabel("none"), "None");
        assert.equal(reasonLabel("notInModFiles"), "Not in the mod's files");
        assert.equal(reasonLabel(null), "");
    });
});

describe("originChoices", () => {
    it("offers every layer, the root project first, then the game", () => {
        assert.deepEqual(
            originChoices([
                { rank: 0, name: "Core" },
                { rank: 1, name: "Mod" },
            ]),
            ["layer:1", "layer:0", "game"],
        );
    });
});

describe("kindChoices", () => {
    it("offers the kinds the loaded rows are, once each, in name order", () => {
        assert.deepEqual(
            kindChoices([
                { kinds: ["SpaceUnit", "Capital"] },
                { kinds: ["SpaceUnit"] },
                { kinds: [] },
            ]),
            ["Capital", "SpaceUnit"],
        );
    });
});

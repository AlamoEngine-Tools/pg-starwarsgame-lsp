// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import {
    kindChoices,
    locationLabel,
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

describe("locationLabel", () => {
    it("names the file and the one-based line", () => {
        assert.equal(
            locationLabel({
                uri: "file:///c:/mod/data/xml/Units_Space_Empire.xml",
                line: 211,
            }),
            "Units_Space_Empire.xml:212",
        );
    });

    it("decodes an escaped file name", () => {
        assert.equal(
            locationLabel({ uri: "file:///c:/mod/My%20Units.xml", line: 0 }),
            "My Units.xml:1",
        );
    });
});

describe("kindChoices", () => {
    const kinds = [
        { kind: "Hero", count: 0 },
        { kind: "SpaceUnit", count: 412 },
    ];

    it("offers every kind the server counted, with its count", () => {
        assert.deepEqual(
            kindChoices(kinds, []).map((c) => [c.kind, c.label]),
            [
                ["Hero", "Hero (0)"],
                ["SpaceUnit", "SpaceUnit (412)"],
            ],
        );
    });

    it("disables a kind the list holds none of, unless it is picked", () => {
        assert.deepEqual(
            kindChoices(kinds, []).map((c) => c.disabled),
            [true, false],
        );
        assert.deepEqual(
            kindChoices(kinds, ["Hero"]).map((c) => c.disabled),
            [false, false],
        );
    });
});

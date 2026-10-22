// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import {
    actionChoices,
    kindChoices,
    locationLabel,
    menuChoices,
    originChoices,
    originLabel,
    pageLabel,
    previewLayout,
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
        assert.equal(relationLabel("sfxEvents"), "SFX Events");
    });

    it("calls what names an entry its references, as the code lens does, for an object and an asset alike", () => {
        assert.equal(relationLabel("referencedBy"), "References");
        assert.equal(relationLabel("usedBy"), "References");
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

describe("actionChoices", () => {
    const summary = (groups: ReturnType<typeof actionChoices>) =>
        groups.map((g) =>
            g.map((c) => [c.action, c.label, c.disabled ? c.reason : null]),
        );

    it("puts open and go to definition first, the entry's actions next, copying its name last", () => {
        assert.deepEqual(
            summary(
                actionChoices(true, [
                    { action: "definition", reason: null },
                    { action: "previewUnit", reason: null },
                    { action: "encyclopedia", reason: "noEncyclopedia" },
                    { action: "effectiveObject", reason: "notAVariant" },
                ]),
            ),
            [
                [
                    ["open", "Open", null],
                    ["definition", "Go to definition", null],
                ],
                [
                    ["previewUnit", "Preview unit", null],
                    [
                        "encyclopedia",
                        "Show encyclopedia card",
                        "No encyclopedia text",
                    ],
                    [
                        "effectiveObject",
                        "Show effective object",
                        "Not a variant",
                    ],
                ],
                [["copyName", "Copy name", null]],
            ],
        );
    });

    it("names the keys that do the same", () => {
        const [first] = actionChoices(true, []);
        assert.deepEqual(
            first.map((c) => c.keys),
            ["Enter", "F12"],
        );
    });

    it("waits for the server before going to a definition", () => {
        assert.deepEqual(summary(actionChoices(true, undefined))[0][1], [
            "definition",
            "Go to definition",
            "Loading...",
        ]);
    });

    it("offers only copying the name of a row that is no entry of its own", () => {
        assert.deepEqual(summary(actionChoices(false, undefined)), [
            [
                ["open", "Open", "Not an entry of its own"],
                ["definition", "Go to definition", "Not an entry of its own"],
            ],
            [["copyName", "Copy name", null]],
        ]);
    });

    it("says why there is no definition to go to", () => {
        const reasonOf = (reason: string) =>
            actionChoices(true, [{ action: "definition", reason }])[0][1]
                .reason;
        assert.equal(reasonOf("shipped"), "Only the game defines it");
        assert.equal(reasonOf("binary"), "Not a text file");
        assert.equal(reasonOf("schema"), "Defined by the schema");
        assert.equal(reasonOf("off"), "Turned off in the settings");
    });
});

describe("menuChoices", () => {
    it("lists only what can be done, leaving no empty group behind", () => {
        const menu = menuChoices(
            actionChoices(true, [
                { action: "definition", reason: "binary" },
                { action: "previewModel", reason: null },
            ]),
        );

        assert.deepEqual(
            menu.map((g) => g.map((c) => c.action)),
            [["open"], ["previewModel"], ["copyName"]],
        );
    });

    it("offers only the name of a row that is no entry of its own", () => {
        assert.deepEqual(
            menuChoices(actionChoices(false, undefined)).map((g) =>
                g.map((c) => c.action),
            ),
            [["copyName"]],
        );
    });
});

/**
 * The preview's grid, after Scout's tile grid: a column is dropped at a time, never below one. Unlike
 * Scout's, the grid uses the full width - its columns stretch - so a wide panel takes more of them.
 */
describe("previewLayout", () => {
    const at = (width: number) => {
        const layout = previewLayout(width);
        return [layout.columns, layout.actionsBeside];
    };

    it("keeps the actions beside the grid while two columns fit beside them", () => {
        assert.deepEqual(at(1082), [4, true]);
        assert.deepEqual(at(1081), [3, true]);
        assert.deepEqual(at(866), [3, true]);
        assert.deepEqual(at(865), [2, true]);
        assert.deepEqual(at(650), [2, true]);
    });

    it("takes every column that fits the full width, with no upper limit", () => {
        assert.deepEqual(at(1298), [5, true]);
        assert.deepEqual(at(1920), [7, true]);
    });

    it("moves the actions above the grid when they no longer fit beside two columns", () => {
        assert.deepEqual(at(649), [2, false]);
        assert.deepEqual(at(452), [2, false]);
        assert.deepEqual(at(451), [1, false]);
    });

    it("never drops below one column, however narrow", () => {
        assert.deepEqual(at(100), [1, false]);
    });
});

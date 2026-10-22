// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import {
    actionChoices,
    factLabel,
    fileSize,
    kindChoices,
    listColumns,
    problemMessage,
    locationLabel,
    menuChoices,
    originChoices,
    originLabel,
    pageIcon,
    pageLabel,
    previewLayout,
    noteLabel,
    propertyLabel,
    propertyValue,
    reasonLabel,
    relationIcon,
    relationLabel,
    rowTypeLabel,
} from "./modBrowserModel";

/** The Schema outline's words and pictures (#270). */
describe("the Schema outline", () => {
    it("names its lists and relations", () => {
        assert.equal(pageLabel("schemaTypes"), "Types");
        assert.equal(pageLabel("schemaTags"), "Tags");
        assert.equal(pageLabel("behaviours"), "Behaviours");
        assert.equal(relationLabel("tags"), "Tags");
        assert.equal(relationLabel("objects"), "Game objects");
    });

    it("pictures a type, a tag and a behaviour", () => {
        assert.equal(pageIcon("schemaTypes"), "schemaType");
        assert.equal(pageIcon("schemaTags"), "tag");
        assert.equal(pageIcon("behaviours"), "behaviour");
        assert.equal(relationIcon("objects"), "gameObject");
    });

    it("names a tag's properties and its notes' kinds", () => {
        assert.equal(propertyLabel("valueType"), "Value type");
        assert.equal(propertyLabel("references"), "References");
        assert.equal(propertyLabel("repeats"), "Repeats");
        assert.equal(noteLabel("BuggedInEngine"), "Bugged in the engine");
        assert.equal(noteLabel("Since"), "Since");
    });

    // Whether an enum is built in or read from the game's files, said wherever its type is.
    it("says what kind of enum an enum is", () => {
        const enumRow = (type: string) => ({
            id: "DamageType",
            name: "DamageType",
            type,
            kinds: [],
            origin: "game",
            overrides: [],
            page: "enums",
        });
        assert.equal(rowTypeLabel(enumRow("fixed")), "Built in");
        assert.equal(rowTypeLabel(enumRow("dynamic")), "Dynamic");
        assert.equal(rowTypeLabel(enumRow("gameConstants")), "Game constants");
        assert.equal(
            rowTypeLabel({
                ...enumRow("GameObjectType"),
                page: "xml:GameObjectType",
            }),
            "GameObjectType",
        );
        assert.equal(propertyLabel("kind"), "Kind");
        assert.equal(
            propertyValue({ key: "kind", value: "dynamic" }),
            "Dynamic",
        );
        assert.equal(propertyValue({ key: "repeats", value: "" }), "Yes");
        assert.equal(
            propertyValue({ key: "valueType", value: "Float" }),
            "Float",
        );
    });

    it("names and pictures an enum's values", () => {
        assert.equal(relationLabel("values"), "Values");
        assert.equal(relationIcon("values"), "enumValue");
        assert.equal(pageIcon("enumValues"), "enumValue");
    });

    // An enum's values list keys and values as fields of their own; no value behaves like anything.
    it("gives an enum's values list a Value column", () => {
        assert.deepEqual(listColumns(undefined), [
            "name",
            "type",
            "kinds",
            "origin",
        ]);
        assert.deepEqual(listColumns("models"), [
            "name",
            "type",
            "kinds",
            "origin",
        ]);
        assert.deepEqual(listColumns("values"), [
            "name",
            "value",
            "type",
            "origin",
        ]);
    });
});

/** The rail's picture of each list and entry, by what it is (#270). */
describe("pageIcon", () => {
    it("pictures an entry by its type", () => {
        assert.equal(pageIcon("xml:GameObjectType"), "gameObject");
        assert.equal(pageIcon("xml:SFXEvent"), "gameObject");
        assert.equal(pageIcon("models"), "model");
        assert.equal(pageIcon("particleSystems"), "effects");
        assert.equal(pageIcon("animations"), "animation");
        assert.equal(pageIcon("textures"), "texture");
        assert.equal(pageIcon("audio"), "audio");
        assert.equal(pageIcon("luaScripts"), "script");
        assert.equal(pageIcon("enums"), "enumeration");
    });

    // The Scripts outline's lists are the folders under Data/Scripts, each of scripts.
    it("pictures a folder of scripts as scripts", () => {
        assert.equal(pageIcon("scripts:GameObject"), "script");
    });
});

describe("relationIcon", () => {
    it("pictures a relation by what it holds", () => {
        assert.equal(relationIcon("models"), "model");
        assert.equal(relationIcon("model"), "model");
        assert.equal(relationIcon("sfxEvents"), "audio");
        assert.equal(relationIcon("samples"), "audio");
        assert.equal(relationIcon("speechEvents"), "speech");
        assert.equal(relationIcon("hardpoints"), "target");
        assert.equal(relationIcon("luaScript"), "script");
        assert.equal(relationIcon("requires"), "script");
        assert.equal(relationIcon("usedBy"), "references");
        assert.equal(relationIcon("referencedBy"), "references");
        assert.equal(relationIcon("definitions"), "definition");
    });

    // An object's own tags name the rest: a relation named after its tag.
    it("pictures any other relation as the tag naming it", () => {
        assert.equal(relationIcon("Projectile_Types"), "tag");
    });
});

/** The Mod Browser's words for the server's keys (#270). */
describe("pageLabel", () => {
    it("names a folder of scripts by the folder", () => {
        assert.equal(pageLabel("scripts:GameObject"), "GameObject");
        assert.equal(pageLabel("scripts:Other"), "Other");
    });

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

    it("says why an entry whose type has no preview does not open", () => {
        assert.deepEqual(
            summary(
                actionChoices(
                    true,
                    [{ action: "definition", reason: "binary" }],
                    { action: "open", reason: "noPreview" },
                ),
            )[0][0],
            ["open", "Open", "No preview for this type"],
        );
    });

    it("says why a planet offers no encyclopedia card", () => {
        const [, offered] = actionChoices(true, [
            { action: "definition", reason: null },
            { action: "encyclopedia", reason: "planet" },
        ]);
        assert.equal(offered[0].reason, "Not for a planet");
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
 * The preview's grid is fixed - the adapter's columns and rows, each tile in its own cells - and only
 * stretches over the tab. What the width decides is where the actions go and how wide a cell is.
 */
describe("previewLayout", () => {
    it("keeps the actions beside the grid while the whole grid fits beside them at its least cell", () => {
        // Six least cells and five gaps are 800; padding, the gap and the action column add 228.
        assert.equal(previewLayout(1028, 6).actionsBeside, true);
        assert.equal(previewLayout(1027, 6).actionsBeside, false);
    });

    it("stretches the cells over what is left of the width", () => {
        // 1228 less the padding, the gap and the action column leaves 1000 for six cells and five gaps.
        assert.equal(previewLayout(1228, 6).cellWidth, (1000 - 5 * 16) / 6);
        // Above the grid, the actions leave it the full width less the padding.
        assert.equal(previewLayout(900, 6).cellWidth, (900 - 32 - 5 * 16) / 6);
    });

    it("never shrinks a cell below its least width; the grid scrolls instead", () => {
        assert.equal(previewLayout(300, 6).cellWidth, 120);
    });
});

describe("factLabel", () => {
    it("names what a model's facts count", () => {
        assert.equal(factLabel("bones"), "Bones");
        assert.equal(factLabel("textures"), "Textures");
        assert.equal(factLabel("particleSystems"), "Particle Systems");
        assert.equal(factLabel("animations"), "Animations");
    });
});

describe("problemMessage", () => {
    it("words a problem the server found by its reason, naming what no file holds", () => {
        assert.equal(
            problemMessage({
                name: "sd_missing.dds",
                reason: "missingTexture",
            }),
            "No file holds the texture - sd_missing.dds",
        );
        assert.equal(
            problemMessage({
                name: "p_missing.alo",
                reason: "missingParticleSystem",
            }),
            "No file holds the particle system - p_missing.alo",
        );
    });

    it("leaves a diagnostic's message as it is", () => {
        assert.equal(
            problemMessage({ name: "Max_Speed is out of range" }),
            "Max_Speed is out of range",
        );
    });
});

describe("the reveal action", () => {
    const reveal = (reason: string | null) =>
        actionChoices(true, [
            { action: "definition", reason: "binary" },
            { action: "reveal", reason },
        ])[1][0];

    it("shows the file in the file manager", () => {
        assert.equal(reveal(null).label, "Reveal in File Explorer");
        assert.equal(reveal(null).disabled, false);
    });

    it("says why there is no file to show", () => {
        assert.equal(reveal("packed").reason, "Packed in the game's archives");
        assert.equal(reveal("notFound").reason, "No file on disk");
    });
});

describe("fileSize", () => {
    it("reads a size in the unit it is closest to", () => {
        assert.equal(fileSize(512), "512 B");
        assert.equal(fileSize(1536), "1.5 KB");
        assert.equal(fileSize(5 * 1024 * 1024), "5 MB");
    });
});

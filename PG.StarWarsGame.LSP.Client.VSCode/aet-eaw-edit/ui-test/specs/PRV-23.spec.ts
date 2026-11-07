// PRV-23: a game object's page - its relations, its tiles, its actions and its context menu. The project ships its
// own Spaceunitsfrigates.xml, so the game never opens the shipped one: the frigate is defined once (PRV-35 holds
// the game's packed row).
import { browser, expect } from "@wdio/globals";
import { EAW, serverReady } from "../protocol.js";
import { inNewestWebview } from "./storyGraph.js";
import {
    actions,
    contextMenu,
    entryHead,
    openOutline,
    openRow,
    panels,
    rail,
    railClick,
    railExpand,
    rows,
    search,
    selectRow,
    tableReady,
} from "./modBrowser.js";

const RELATIONS = [
    "Models",
    "Particle Systems",
    "Particles",
    "SFX Events",
    "Speech Events",
    "Hardpoints",
    "Other references",
    "Textures",
    "Lua Script",
    "References",
    "Overrides",
];

describe("PRV-23", () => {
    before(async () => {
        await serverReady();
    });

    it("shows the frigate's relations, page, actions and menu", async () => {
        await openOutline("Game Data");
        await inNewestWebview(async () => {
            await tableReady();
            await railClick("Game Objects");
            await tableReady();
            await search("Nebulon_B_Frigate");
            const row = (await rows()).find((r) => r.cells[0] === "Nebulon_B_Frigate")!;
            expect(row.cells[3]).toBe(EAW);
            expect(row.tags).toEqual([]);

            await selectRow("Nebulon_B_Frigate");
            await railExpand("Nebulon_B_Frigate");
            const nodes = await rail();
            const at = nodes.findIndex((n) => n.label === "Nebulon_B_Frigate");
            const relations = nodes.slice(at + 1, at + 1 + RELATIONS.length);
            expect(relations.map((n) => n.label)).toEqual(RELATIONS);
            for (const relation of relations) {
                expect(relation.count).toMatch(/^[\d,]+$/);
                if (relation.label === "Overrides") {
                    expect(relation).toMatchObject({ count: "0", inert: true, title: "Defined once" });
                } else if (relation.count === "0") {
                    expect(relation).toMatchObject({ inert: true, title: "None" });
                }
            }

            const menu = await contextMenu("Nebulon_B_Frigate");
            expect(menu.every((m) => !m.disabled)).toBe(true);
            expect(menu.map((m) => m.label)).not.toContain("Show effective object");
            expect(menu.map((m) => m.label)).toEqual(
                expect.arrayContaining(["Go to definition", "Preview unit", "Show encyclopedia card", "Copy name"]),
            );

            await openRow("Nebulon_B_Frigate");
            const tiles = await actions();
            expect(tiles.map((t) => t.label)).toEqual([
                "Go to definition",
                "Preview unit",
                "Show encyclopedia card",
                "Show effective object",
                "Copy name",
            ]);
            expect(tiles[3]).toMatchObject({ disabled: true, title: "Not a variant" });
            expect(tiles.filter((t) => t.disabled).length).toBe(1);

            const head = await entryHead();
            expect(head.title).toBe("Nebulon_B_Frigate");
            expect(head.rows["Defined in"]).toBe("Spaceunitsfrigates.xml:4");
            expect(await panels()).toEqual(["Header", "Problems", "Encyclopedia", "References"]);
            // The card draws after the page: its faction switch is waited for, not read at once.
            await (await browser.$('section.panel[aria-label="Encyclopedia"] [aria-label="Faction frame"]')).waitForExist({
                timeout: 30_000,
                timeoutMsg: "The encyclopedia tile has no faction switch",
            });
        });
    });
});

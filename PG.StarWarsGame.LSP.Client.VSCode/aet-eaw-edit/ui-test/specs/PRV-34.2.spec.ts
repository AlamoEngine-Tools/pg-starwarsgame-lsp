// PRV-34, second part: a game-only object, on PRV-35's fixture - a mod whose object file list names the
// game's files without replacing them.
import { browser, expect } from "@wdio/globals";
import { serverReady } from "../protocol.js";
import { inNewestWebview } from "./storyGraph.js";
import { actions, openOutline, openRow, railChildren, railClick, railExpand, rows, search, selectRow, tableReady } from "./modBrowser.js";
import { TAG_RELATIONS } from "./PRV-34.relations.js";

const GAME_ONLY = "Corellian_Corvette";

describe("PRV-34", () => {
    before(async () => {
        await serverReady();
    });

    it("tells a game-only object", async () => {
        await openOutline("Game Data");
        await inNewestWebview(async () => {
            await tableReady();
            await railClick("Game Objects");
            await tableReady();
            await search(GAME_ONLY);
            const game = (await rows()).find((r) => r.cells[0] === GAME_ONLY)!;
            expect(game.cells[3]).toBe("Game");
            expect(game.tags).toEqual([]);
            await selectRow(GAME_ONLY);
            await railExpand(GAME_ONLY, 1);
            const relations = await railChildren(GAME_ONLY);
            for (const relation of relations.filter((r) => TAG_RELATIONS.includes(r.label)))
                expect(relation).toMatchObject({ inert: true, title: "Not in the mod's files" });
            expect(relations.find((r) => r.label === "Overrides")).toMatchObject({ inert: true, title: "Defined once" });
            await openRow(GAME_ONLY);
            expect((await actions()).find((a) => a.label === "Go to definition")).toMatchObject({
                disabled: true,
                title: "Only the game defines it",
            });
            await browser.pause(200);
        });
    });
});

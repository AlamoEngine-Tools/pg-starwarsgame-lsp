// PRV-34 (mod-bound): the Mod Browser in a mod - an object the mod defines once. The object is local.json's.
// The game-only half runs in PRV-34.2 on PRV-35's fixture: the mod replaces every object file of the game it
// lists, so it has no game-only object.
import { browser, expect } from "@wdio/globals";
import { localValues } from "../paths.js";
import { serverReady } from "../protocol.js";
import { inNewestWebview } from "./storyGraph.js";
import { openOutline, railChildren, railClick, railExpand, rows, search, selectRow, tableReady } from "./modBrowser.js";
import { TAG_RELATIONS } from "./PRV-34.relations.js";

const mod = localValues<{ modOnly: string; leafName: string }>("PRV-34");

describe("PRV-34", () => {
    before(async () => {
        await serverReady();
    });

    it("tells a mod object defined once", async () => {
        await openOutline("Game Data");
        await inNewestWebview(async () => {
            await tableReady();
            await railClick("Game Objects");
            await tableReady();
            await search(mod.modOnly);
            expect((await rows()).find((r) => r.cells[0] === mod.modOnly)?.cells[3]).toBe(mod.leafName);
            await selectRow(mod.modOnly);
            await railExpand(mod.modOnly, 1);
            const own = await railChildren(mod.modOnly);
            expect(own.find((r) => r.label === "Overrides")).toMatchObject({ inert: true, title: "Defined once" });
            expect(own.filter((r) => TAG_RELATIONS.includes(r.label)).every((r) => /^[\d,]+$/.test(r.count))).toBe(true);
            await browser.pause(200);
        });
    });
});

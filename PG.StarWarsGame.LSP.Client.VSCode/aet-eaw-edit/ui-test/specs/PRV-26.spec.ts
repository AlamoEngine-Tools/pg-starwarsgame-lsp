// PRV-26: the Scripts outline lists the script folders; a script's relations and page.
import { expect } from "@wdio/globals";
import { serverReady } from "../protocol.js";
import { inNewestWebview } from "./storyGraph.js";
import {
    entryHead,
    openOutline,
    openRow,
    panels,
    rail,
    railChildren,
    railClick,
    railExpand,
    search,
    selectRow,
    tableReady,
} from "./modBrowser.js";

const FOLDERS = ["GameObject", "Library", "Story", "AI", "Evaluators", "FreeStore", "Interventions", "Miscellaneous", "Other"];

describe("PRV-26", () => {
    before(async () => {
        await serverReady();
    });

    it("lists the script folders and a script's relations and page", async () => {
        await openOutline("Scripts");
        await inNewestWebview(async () => {
            await tableReady();
            const lists = (await rail()).filter((n) => n.depth === 0 && !n.label.endsWith(" more"));
            // In the order the folders are named; an empty one is disabled, not left out.
            expect(lists.map((n) => n.label)).toEqual(FOLDERS.filter((f) => lists.some((n) => n.label === f)));
            expect(lists.map((n) => n.label)).toEqual(expect.arrayContaining(["GameObject", "Library", "Story"]));
            for (const empty of lists.filter((n) => n.count === "0")) {
                expect(empty).toMatchObject({ inert: true, title: "None" });
            }

            await railClick("Library");
            await tableReady();
            await search("Pgbase");
            await selectRow("Pgbase.lua");
            await railExpand("Pgbase.lua", 1);
            expect((await railChildren("Pgbase.lua")).map((n) => n.label)).toEqual(["Loaded by", "Requires"]);

            await openRow("Pgbase.lua");
            const head = await entryHead();
            expect(Object.keys(head.rows)).toEqual(expect.arrayContaining(["Size", "Lines", "Modified", "Created"]));
            expect(await panels()).toEqual(expect.arrayContaining(["Header", "Loaded by", "Requires"]));
        });
    });
});

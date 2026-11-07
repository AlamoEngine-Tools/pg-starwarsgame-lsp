// PRV-21: the search form, the column filters and the server's sort. Dragging columns and the kept layout across
// a reload stay a hand check.
import { browser, expect } from "@wdio/globals";
import { serverReady } from "../protocol.js";
import { inNewestWebview } from "./storyGraph.js";
import { foot, kindTags, openOutline, railClick, rows, search, tableReady } from "./modBrowser.js";

/** The header filter button of a column. */
const headerFilter = (column: string): string =>
    `//th[.//span[contains(@class,'th-label') and normalize-space()='${column}']]//button[contains(@class,'th-filter')]`;

describe("PRV-21", () => {
    before(async () => {
        await serverReady();
    });

    it("searches, filters and sorts the game objects", async () => {
        await openOutline("Game Data");
        await inNewestWebview(async () => {
            await tableReady();
            await railClick("Game Objects");
            await tableReady();

            await search("Nebulon");
            const found = (await rows()).map((r) => r.cells[0]);
            expect(found).toContain("Nebulon_B_Frigate");
            expect(found.every((n) => /nebulon/i.test(n))).toBe(true);

            await search("");
            const tags = await kindTags();
            expect(tags.every((t) => /\(\d+\)$/.test(t.label))).toBe(true);
            for (const t of tags.filter((t) => t.label.endsWith("(0)"))) {
                expect(t).toMatchObject({ disabled: true, title: "None in this list" });
            }

            await search("", ["Planet"]);
            const planets = await rows();
            expect(planets.map((r) => r.cells[0])).toEqual(expect.arrayContaining(["Coruscant", "Corulag"]));
            expect(planets.every((r) => r.cells[2].split(", ").includes("Planet"))).toBe(true);
            expect(await (await browser.$(headerFilter("Behaves like"))).getAttribute("aria-pressed")).toBe("true");

            const type = await browser.$(headerFilter("Type"));
            expect(await type.isEnabled()).toBe(false);
            expect(await type.getAttribute("title")).toBe("No filter for the type");

            // The whole list, the rows not yet loaded included: the last name of all is first descending.
            await search("");
            const total = Number(/of ([\d,]+)/.exec((await foot()).text)![1].replace(/,/g, ""));
            const name = await browser.$("//th[.//span[contains(@class,'th-label') and normalize-space()='Name']]//span[contains(@class,'th-label')]");
            await name.click();
            await browser.pause(1_500);
            const ascending = (await rows()).map((r) => r.cells[0]);
            await name.click();
            await browser.pause(1_500);
            const descending = (await rows()).map((r) => r.cells[0]);
            expect(descending[0]).not.toBe(ascending[0]);
            expect(descending.slice(0, 3)).not.toEqual(expect.arrayContaining(ascending.slice(0, 3)));
            expect((await foot()).text).toBe(`Showing 200 of ${total.toLocaleString("en-US")}`);
            const lower = (s: string): string => s.toLowerCase();
            expect(lower(descending[0]) >= lower(ascending[ascending.length - 1])).toBe(true);
        });
    });
});

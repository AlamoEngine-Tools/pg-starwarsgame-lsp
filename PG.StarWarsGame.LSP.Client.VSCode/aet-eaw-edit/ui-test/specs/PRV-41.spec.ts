// PRV-41 (#382): sorting the table by name ascending gives the order the list opened with. Both are the plain
// character order, case folded, where a letter sorts before an underscore - 2M_Repulsor_Tank before 2_Warpod.
// The sorted column ran through a word-aware comparison that let the underscore slide, so the two orders
// disagreed on exactly that pair in the report.
import { browser, expect } from "@wdio/globals";
import { serverReady } from "../protocol.js";
import { inNewestWebview } from "./storyGraph.js";
import { openOutline, railClick, rows, tableReady } from "./modBrowser.js";

const nameHeader =
    "//th[.//span[contains(@class,'th-label') and normalize-space()='Name']]//span[contains(@class,'th-label')]";

/** The plain character order, case folded - what the server opens a list in. */
function ordinal(a: string, b: string): number {
    const x = a.toUpperCase();
    const y = b.toUpperCase();
    return x < y ? -1 : x > y ? 1 : 0;
}

describe("PRV-41", () => {
    before(async () => {
        await serverReady();
    });

    it("sorts by name into the order the list opened with", async () => {
        await openOutline("Game Data");
        await inNewestWebview(async () => {
            await tableReady();
            await railClick("Game Objects");
            await tableReady();
            const opened = (await rows()).map((r) => r.cells[0]);
            expect(opened).toEqual([...opened].sort(ordinal));
            // The pair the order hinges on sits on the vanilla game's first page: a name with an
            // underscore where another has a letter.
            expect(opened.some((n) => n.includes("_"))).toBe(true);

            const name = await browser.$(nameHeader);
            await name.click();
            await browser.pause(1_500);
            const ascending = (await rows()).map((r) => r.cells[0]);
            expect(ascending).toEqual(opened);

            await name.click();
            await browser.pause(1_500);
            const descending = (await rows()).map((r) => r.cells[0]);
            expect(descending).toEqual([...descending].sort(ordinal).reverse());
        });
    });
});

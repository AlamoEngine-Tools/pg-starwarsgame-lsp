// SET-18: a credits file opens as an ordered grid with its line kinds as tiles; the crawl opens beside it
// and full screen. How the crawl scrolls is a picture and stays a hand check.
import { browser, expect } from "@wdio/globals";
import { serverReady, tabLabels } from "../protocol.js";
import { inNewestWebview } from "./storyGraph.js";
import { columns, contentTiles, openTextFile, sortable } from "./locGrid.js";

describe("SET-18", () => {
    before(async () => {
        await serverReady();
    });

    it("opens the credits as an ordered grid with the line kinds as tiles", async () => {
        await openTextFile("Credits files", "creditstext_english.dat");
        await inNewestWebview(async () => {
            expect(await columns()).toEqual(["Format", "ENGLISH"]);
            expect(await sortable()).toBe(false);
            expect(await contentTiles()).toEqual([
                { label: "Label", badge: "HEADER" },
                { label: "Name", badge: "CENTER" },
                { label: "Blank line", badge: "[TBL]" },
            ]);
        });
    });

    it("opens the crawl beside the grid and full screen", async () => {
        const before = (await tabLabels()).length;
        await browser.executeWorkbench((vscode) => vscode.commands.executeCommand("aet-eaw-edit.lsp.previewCreditsCrawl"));
        await browser.waitUntil(async () => (await tabLabels()).length > before, {
            timeout: 30_000,
            timeoutMsg: "No crawl opened beside the grid",
        });
        const groups: number = await browser.executeWorkbench((vscode) => vscode.window.tabGroups.all.length);
        expect(groups).toBeGreaterThan(1);

        await browser.executeWorkbench((vscode) =>
            vscode.commands.executeCommand("aet-eaw-edit.lsp.previewCreditsCrawlFullScreen"),
        );
        // Full screen is the crawl played over the grid itself, in the grid's own panel.
        const workbench = await browser.getWorkbench();
        let overGrid = false;
        await browser.waitUntil(
            async () => {
                for (const view of await workbench.getAllWebviews()) {
                    await view.open();
                    try {
                        overGrid ||= await browser.execute(
                            () => document.querySelector(".head-row") !== null && document.querySelector(".starfield") !== null,
                        );
                    } finally {
                        await view.close();
                    }
                }
                return overGrid;
            },
            { timeout: 30_000, timeoutMsg: "No crawl over the credits grid" },
        );
    });
});

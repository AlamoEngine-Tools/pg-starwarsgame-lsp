// SET-18: a credits file opens as an ordered grid with its line kinds as tiles; the crawl opens beside it
// and full screen. How the crawl scrolls is a picture and stays a hand check. The file is a compiled DAT,
// which the grid shows and does not edit: its line kinds and its formats are disabled and say how its
// texts are edited (#339). SET-35 has the line kinds of a credits file that is edited.
import { browser, expect } from "@wdio/globals";
import { serverReady, tabLabels } from "../protocol.js";
import { inNewestWebview } from "./storyGraph.js";
import { columns, contentTiles, openTextFile, sortable } from "./locGrid.js";

const howToEdit = "Convert to CSV or XML to edit";

describe("SET-18", () => {
    before(async () => {
        await serverReady();
    });

    it("opens the credits as an ordered grid with the line kinds as tiles", async () => {
        await openTextFile("Credits files", "creditstext_english.dat");
        await inNewestWebview(async () => {
            expect(await columns()).toEqual(["Format", "ENGLISH"]);
            expect(await sortable()).toBe(false);
            // A disabled tile says why in place of its badge, so each line kind names the way to edit.
            expect(await contentTiles()).toEqual([
                { label: "Add language", badge: howToEdit },
                { label: "Label", badge: howToEdit },
                { label: "Name", badge: howToEdit },
                { label: "Blank line", badge: howToEdit },
            ]);
            const formats: { disabled: boolean; title: string }[] = await browser.execute(() =>
                Array.from(document.querySelectorAll<HTMLSelectElement>(".rows .data-row select.directive")).map((s) => ({
                    disabled: s.disabled,
                    title: s.title,
                })),
            );
            expect(formats.length).toBeGreaterThan(0);
            expect(formats.every((f) => f.disabled && f.title === howToEdit)).toBe(true);
            // The dock's header says it once in full, where a tile has room for half of it.
            expect(await browser.execute(() => (document.querySelector(".read-only-note")?.textContent ?? "").trim())).toBe(
                howToEdit,
            );
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

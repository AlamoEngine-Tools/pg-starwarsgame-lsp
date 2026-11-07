// PRV-20: the Mod Browser view's four outlines open one panel; its table pages 200 rows at a time.
import fs from "node:fs";
import path from "node:path";
import { browser, expect } from "@wdio/globals";
import { client } from "../paths.js";
import { serverReady, tabLabels, viewItems } from "../protocol.js";
import { inNewestWebview } from "./storyGraph.js";
import { columns, foot, openOutline, rail, railClick, rows, tableReady } from "./modBrowser.js";

const browserTabs = async (): Promise<string[]> => (await tabLabels()).filter((t) => t.startsWith("Mod Browser"));

describe("PRV-20", () => {
    before(async () => {
        await serverReady();
    });

    it("lists four outlines and declares no feature flag", async () => {
        expect(await viewItems("Mod Browser")).toEqual(["Game Data", "Assets", "Scripts", "Schema"]);
        const manifest = JSON.parse(fs.readFileSync(path.join(client, "package.json"), "utf8"));
        const keys = manifest.contributes.configuration.flatMap((c: any) => Object.keys(c.properties ?? {}));
        expect(keys).not.toContain("aet-eaw-edit.features.tools.modBrowser");
    });

    it("opens one panel on Game Data and pages the game objects", async () => {
        await openOutline("Game Data");
        await browser.waitUntil(async () => (await browserTabs()).length === 1, { timeout: 30_000 });
        expect(await browserTabs()).toEqual(["Mod Browser"]);

        await inNewestWebview(async () => {
            await tableReady();
            await railClick("Game Objects");
            await tableReady();
            expect(await columns()).toEqual(["Name", "Type", "Behaves like", "Origin"]);

            const lists = (await rail()).filter((n) => n.depth === 0 && !n.label.endsWith(" more"));
            expect(lists.length).toBeGreaterThan(5);
            expect(lists.every((n) => n.icon)).toBe(true);

            let shown = await foot();
            const total = Number(/of ([\d,]+)/.exec(shown.text)![1].replace(/,/g, ""));
            expect(shown.text).toBe(`Showing 200 of ${total.toLocaleString("en-US")}`);
            let count = 200;
            while (!shown.more.disabled) {
                await (await browser.$("button=Load more")).click();
                count = Math.min(count + 200, total);
                await browser.waitUntil(async () => (await rows()).length === count, {
                    timeout: 30_000,
                    timeoutMsg: `Load more did not reach ${count} rows`,
                });
                shown = await foot();
            }
            expect(shown.more.title).toBe("Everything is loaded");
            const names = (await rows()).map((r) => r.cells[0]);
            expect(names.length).toBe(total);
            expect(new Set(names.map((n) => n.toUpperCase())).size).toBe(names.length);
        });
    });

    it("reuses the panel for another outline", async () => {
        for (const outline of ["Assets", "Scripts", "Schema"] as const) {
            await openOutline(outline);
            await browser.pause(1_000);
            expect(await browserTabs()).toEqual(["Mod Browser"]);
        }
    });
});

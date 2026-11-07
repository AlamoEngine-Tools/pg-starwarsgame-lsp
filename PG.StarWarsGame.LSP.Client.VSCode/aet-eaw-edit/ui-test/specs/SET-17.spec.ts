// SET-17: the localisation editor on a text file - its columns, the row filter, the language columns, the
// Inherited toggle, and Validate writing nothing.
import fs from "node:fs";
import path from "node:path";
import { browser, expect } from "@wdio/globals";
import { foc, serverReady, tabLabels } from "../protocol.js";
import { inNewestWebview } from "./storyGraph.js";
import { columnChoices, columns, filterRows, footer, inherited, openTextFile, rowTexts, toggleInherited } from "./locGrid.js";

const file = path.join(foc, "Data", "Text", "mastertextfile_english.dat");

describe("SET-17", () => {
    before(async () => {
        await serverReady();
    });

    it("filters, shows the languages and validates without writing", async () => {
        const written = fs.statSync(file).mtimeMs;
        await openTextFile("Text files", "mastertextfile_english.dat");
        await browser.waitUntil(async () => (await tabLabels()).includes("Loc: mastertextfile_english.dat"), {
            timeout: 60_000,
        });
        await inNewestWebview(async () => {
            expect(await columns()).toEqual(["Key", "ENGLISH"]);

            await filterRows("TEXT_UNIT_TIE_FIGHTER");
            // The project's file is the game's own, so every row is inherited and hidden until the toggle.
            expect(await inherited()).toEqual({
                title: "Show entries whose value is identical to the layer below this file",
                checked: false,
            });
            await toggleInherited();
            let shown: string[] = [];
            await browser.waitUntil(async () => (shown = await rowTexts()).length > 0, {
                timeout: 10_000,
                timeoutMsg: `No row shown - ${await footer()}`,
            });
            expect(shown.every((r) => r.toUpperCase().includes("TEXT_UNIT_TIE_FIGHTER"))).toBe(true);
            expect(shown.filter((r) => r.startsWith("TEXT_UNIT_TIE_FIGHTER | ")).length).toBe(1);

            // The languages this file set has: the workspace ships the English file alone.
            expect(await columnChoices()).toEqual(["ENGLISH"]);

            await browser.execute(() => document.querySelector<HTMLButtonElement>("button.validate-btn")?.click());
            await browser.pause(5_000);
        });
        expect(fs.statSync(file).mtimeMs).toBe(written);
    });
});

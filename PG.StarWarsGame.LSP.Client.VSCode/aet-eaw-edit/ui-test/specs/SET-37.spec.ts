// SET-37: Discard, beside Save in the page toolbar, drops what is staged and leaves the file as it was
// (#222). The question it asks first cannot be answered here - the editor shows no modal dialog to a
// test - so this window has the setting behind "Discard & Don't Ask Again" turned off, and Discard acts
// at once.
import fs from "node:fs";
import { browser, expect } from "@wdio/globals";
import { serverReady } from "../protocol.js";
import { inNewestWebview } from "./storyGraph.js";
import { editCell, openTextFile, rowTexts, saveTitle } from "./locGrid.js";

const credits = "C:\\temp\\aet-test-SET-discard\\Data\\Text\\CreditsText.csv";
const nothingStaged = "Save - nothing is staged";
const nothingToDiscard = "Discard - nothing is staged";

/** The Discard control's tooltip, which is its reason while it cannot be pressed. */
async function discardTitle(): Promise<string> {
    return (await (await browser.$("button.toolbar-discard")).getAttribute("title")) ?? "";
}

describe("SET-37", () => {
    let original = "";

    before(async () => {
        original = fs.readFileSync(credits, "utf8");
        await serverReady();
    });

    it("stands beside Save, disabled with the reason while nothing is staged", async () => {
        await openTextFile("Credits files", "CreditsText.csv");
        await inNewestWebview(async () => {
            await (await browser.$("button.toolbar-discard")).waitForExist({ timeout: 120_000 });
            expect(await saveTitle()).toBe(nothingStaged);
            expect(await discardTitle()).toBe(nothingToDiscard);
            expect(await (await browser.$("button.toolbar-discard")).isEnabled()).toBe(false);
            // Save first, Discard right after it, on the toolbar's left end.
            const order: string[] = await browser.execute(() =>
                Array.from(document.querySelectorAll(".page-toolbar-start > button")).map((b) =>
                    b.classList.contains("toolbar-save") ? "save" : b.classList.contains("toolbar-discard") ? "discard" : "other",
                ),
            );
            expect(order.slice(0, 2)).toEqual(["save", "discard"]);
        });
    });

    it("drops the staged edit, shows the saved text again and writes nothing", async () => {
        await inNewestWebview(async () => {
            const before = await rowTexts();
            await editCell("HEADER", "Lead Writer");
            await browser.waitUntil(async () => (await saveTitle()) !== nothingStaged, {
                timeout: 15_000,
                timeoutMsg: "The edit was not staged",
            });
            expect(await discardTitle()).toBe("Discard - drop all staged changes");
            expect((await rowTexts()).some((row) => row.includes("Lead Writer"))).toBe(true);

            await (await browser.$("button.toolbar-discard")).click();

            await browser.waitUntil(async () => (await saveTitle()) === nothingStaged, {
                timeout: 30_000,
                timeoutMsg: "Discard left the edit staged",
            });
            expect(await discardTitle()).toBe(nothingToDiscard);
            expect(await rowTexts()).toEqual(before);
        });
        expect(fs.readFileSync(credits, "utf8")).toBe(original);
    });
});

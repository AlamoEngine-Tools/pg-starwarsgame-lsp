// SET-10: the encyclopedia's screen is the preview's Screen field; the old setting is unknown; the Mod
// Browser's card is drawn for the same screen. Kept across a window reload stays a hand check (VS Code keeps
// no state under the UI test).
import { browser, expect } from "@wdio/globals";
import { command, editUserSettingsJson, serverReady, unknownSettings } from "../protocol.js";
import { closeAll, inNewestWebview } from "./storyGraph.js";
import { openOutline, openRow, railClick, search, tableReady } from "./modBrowser.js";

/**
 * How large the card's text is drawn relative to the card: the widest line's ink - its text's own box, not
 * the line's - over the width of the smallest box holding every line, the card wherever the panel puts it.
 * A smaller screen draws the same card with larger glyphs. Run inside a webview.
 */
async function cardScale(root: string): Promise<number> {
    await browser.pause(1_500);
    return browser.execute(
        (sel: string) => {
            const lines = Array.from(document.querySelector(sel)!.querySelectorAll("div")).filter(
                (d) => d.children.length === 0 && (d.textContent ?? "").trim().length > 0,
            );
            let card: Element = lines[0];
            while (card.parentElement && !lines.every((l) => card.contains(l))) card = card.parentElement;
            const ink = (d: Element): number => {
                const range = document.createRange();
                range.selectNodeContents(d);
                return range.getBoundingClientRect().width;
            };
            return Math.max(...lines.map(ink)) / card.getBoundingClientRect().width;
        },
        root,
    );
}

describe("SET-10", () => {
    before(async () => {
        await serverReady();
        editUserSettingsJson({ "aet-eaw-edit.encyclopedia.screenWidth": 800 });
    });

    it("marks the old key unknown", () => {
        expect(unknownSettings()).toContain("aet-eaw-edit.encyclopedia.screenWidth");
    });

    let small = 0;

    it("redraws the card for 800x600", async () => {
        await command("aet-eaw-edit.lsp.showEncyclopedia", "Corellian_Corvette");
        await inNewestWebview(async () => {
            const field = await browser.$('input[aria-label="Screen"]');
            await field.waitForExist({ timeout: 120_000 });
            expect(await field.getValue()).toBe("1920x1080");
            const wide = await cardScale(".preview-stage");
            await browser.execute(() => document.querySelector<HTMLInputElement>('input[aria-label="Screen"]')!.focus());
            await (await browser.$('[role="listbox"]')).waitForExist({ timeout: 5_000 });
            await browser.execute(() =>
                Array.from(document.querySelectorAll('[role="option"]'))
                    .find((o) => (o.textContent ?? "").trim() === "800x600")
                    ?.dispatchEvent(new MouseEvent("mousedown", { bubbles: true, cancelable: true })),
            );
            await browser.waitUntil(async () => (await field.getValue()) === "800x600", { timeout: 5_000 });
            small = await cardScale(".preview-stage");
            expect(small).toBeGreaterThan(wide);
        });
        await closeAll();
    });

    it("draws the Mod Browser's card for the same screen", async () => {
        await openOutline("Game Data");
        await inNewestWebview(async () => {
            await tableReady();
            await railClick("Game Objects");
            await tableReady();
            await search("Corellian_Corvette");
            await openRow("Corellian_Corvette");
            const card = 'section.panel[aria-label="Encyclopedia"] .card';
            await (await browser.$(card)).waitForExist({ timeout: 60_000 });
            expect(Math.abs((await cardScale(card)) - small) / small).toBeLessThan(0.1);
        });
    });

    after(() => {
        editUserSettingsJson({ "aet-eaw-edit.encyclopedia.screenWidth": null });
    });
});

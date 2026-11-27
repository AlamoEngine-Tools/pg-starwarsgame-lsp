// PRV-82: the model tree's search plate (#222). Closed, it is one row on the tree's lower right corner: a
// magnifier, the three kinds and a filter icon. The field opens beside the plate and over the stage, with
// the three ways of reading a pattern inside it. Closing the field leaves the pattern in force. The filter
// icon is dead while nothing is set and is the reset once a pattern or a switched-off kind narrows the tree.
// Before, closing the field cleared the pattern, a pattern forced the field open inside the dock, and
// nothing put a switched-off kind back but pressing it again.
import { browser, expect } from "@wdio/globals";
import { clearNotifications, serverReady } from "../protocol.js";
import { model, openInPreview, press } from "./preview.js";
import { inNewestWebview } from "./storyGraph.js";

const PLATE = ".tree-pane .search-plate";
const FIELD = `${PLATE} .search-plate-field`;
const OPEN = "Filter the tree by name";
const NOTHING_SET = "Clear all filters - no filters are set";
const WILDCARD = "Wildcard: * matches any text, ? matches one character";
const SPOTS = ["Spot01.Target", "Spot02.Target", "Spot03.Target", "Spot04.Target", "Spot05.Target"];

interface Plate {
    field: boolean;
    icons: string[];
    /** Which of the three kinds are switched on, left to right. */
    kinds: boolean[];
    magnifierLeft: number;
    reset: { title: string; live: boolean };
}

async function plate(): Promise<Plate> {
    return browser.execute((selector: string) => {
        const el = document.querySelector<HTMLElement>(selector)!;
        const buttons = Array.from(el.querySelectorAll<HTMLButtonElement>(".search-plate-icons button"));
        const reset = el.querySelector<HTMLButtonElement>(".search-plate-reset")!;
        return {
            field: el.querySelector(".search-plate-field") !== null,
            icons: buttons.map((b) => b.getAttribute("title") ?? ""),
            kinds: buttons.slice(1, -1).map((b) => b.getAttribute("aria-pressed") === "true"),
            magnifierLeft: Math.round(el.querySelector(".search-plate-toggle")!.getBoundingClientRect().left),
            reset: { title: reset.getAttribute("title") ?? "", live: !reset.disabled },
        };
    }, PLATE);
}

/** The field's box against the dock and the plate, and the modes inside it. */
async function field(): Promise<{ value: string; modes: number; leftOfDock: number; gapToPlate: number } | null> {
    return browser.execute((selector: string) => {
        const el = document.querySelector<HTMLElement>(selector)!;
        const input = el.querySelector<HTMLInputElement>(".search-plate-field");
        const box = el.querySelector<HTMLElement>(".search-plate-box");
        const dock = el.closest(".right-dock");
        if (!input || !box || !dock) return null;
        const at = box.getBoundingClientRect();
        return {
            value: input.value,
            modes: box.querySelectorAll('[role="radio"]').length,
            leftOfDock: dock.getBoundingClientRect().left - at.left,
            gapToPlate: el.getBoundingClientRect().left - at.right,
        };
    }, PLATE);
}

const names = (): Promise<string[]> =>
    browser.execute(() => Array.from(document.querySelectorAll(".bone-tree .tree-name")).map((n) => (n.textContent ?? "").trim()));

async function type(text: string): Promise<void> {
    const input = await browser.$(FIELD);
    await input.waitForExist({ timeout: 30_000 });
    await input.click();
    await browser.keys(["Control", "a"]);
    await browser.keys("Backspace");
    if (text !== "") await input.addValue(text);
    await browser.pause(400);
}

describe("PRV-82", () => {
    let whole: string[] = [];

    before(async () => {
        await serverReady();
        await clearNotifications();
        await openInPreview(model("Eb_icc.alo"));
        await inNewestWebview(async () => {
            await (await browser.$(`${PLATE} button[title="${OPEN}"]`)).waitForClickable({ timeout: 120_000 });
            await browser.waitUntil(async () => (await names()).length > 0, { timeout: 60_000, timeoutMsg: "The tree stayed empty" });
            whole = await names();
        });
    });

    it("is a magnifier, three kinds and a dead filter icon while closed", async () => {
        await inNewestWebview(async () => {
            const closed = await plate();
            expect(closed.field).toBe(false);
            expect(closed.icons.length).toBe(5);
            expect(closed.icons[0]).toBe(OPEN);
            expect(closed.icons[4]).toBe(NOTHING_SET);
            expect(closed.kinds).toEqual([true, true, true]);
            expect(closed.reset.live).toBe(false);
        });
    });

    it("counts a switched-off kind as a filter, and puts it back from the reset", async () => {
        await inNewestWebview(async () => {
            const bones = (await plate()).icons[1];
            await press(`${PLATE} button[title="${bones}"]`);
            await browser.pause(400);
            const narrowed = await plate();
            expect(narrowed.kinds).toEqual([false, true, true]);
            expect(narrowed.reset).toEqual({ title: "Clear all filters", live: true });

            await press(`${PLATE} .search-plate-reset`);
            await browser.pause(400);
            const back = await plate();
            expect(back.kinds).toEqual([true, true, true]);
            expect(back.reset.live).toBe(false);
            expect(await names()).toEqual(whole);
        });
    });

    it("opens its field beside the plate, over the stage, and keeps the pattern when the field closes", async () => {
        await inNewestWebview(async () => {
            const before = await plate();
            await press(`${PLATE} button[title="${OPEN}"]`);
            const at = await field();
            expect(at?.modes).toBe(3);
            expect(at?.leftOfDock).toBeGreaterThan(40);
            expect(Math.abs(at?.gapToPlate ?? NaN)).toBeLessThanOrEqual(1.5);
            expect((await plate()).magnifierLeft).toBe(before.magnifierLeft);

            await type("Target");
            const found = await names();
            expect(SPOTS.every((spot) => found.includes(spot))).toBe(true);
            expect(found.length).toBeLessThan(whole.length);

            // Escape closes the field and nothing else.
            await (await browser.$(FIELD)).click();
            await browser.keys("Escape");
            await browser.pause(400);
            const closed = await plate();
            expect(closed.field).toBe(false);
            expect(await names()).toEqual(found);
            expect(closed.reset).toEqual({ title: "Clear all filters - Target", live: true });

            // The magnifier brings the same pattern back.
            await press(`${PLATE} button[title="${OPEN}"]`);
            expect((await field())?.value).toBe("Target");
        });
    });

    it("reads the pattern with wildcards when that mode is chosen, and clears it from the reset", async () => {
        await inNewestWebview(async () => {
            await type("Spot0?.Target");
            // Plain text: no bone has a question mark in its name.
            expect(await names()).toEqual([]);

            await press(`${PLATE} .search-plate-box [role="radio"][title="${WILDCARD}"]`);
            await browser.pause(400);
            const found = await names();
            expect(SPOTS.every((spot) => found.includes(spot))).toBe(true);
            expect(found.includes("Camera01.Target")).toBe(false);

            await press(`${PLATE} .search-plate-reset`);
            await browser.pause(400);
            expect(await names()).toEqual(whole);
            expect((await plate()).reset.live).toBe(false);
        });
    });
});

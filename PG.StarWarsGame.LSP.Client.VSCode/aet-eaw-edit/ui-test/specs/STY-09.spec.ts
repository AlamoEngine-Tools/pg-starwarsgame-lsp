// STY-09: the story graph's palette is searched from a plate at the foot of the dock, in the model tree's
// shape (#222): a magnifier, the two kinds - event types and rewards - and a filter icon. The field opens to
// the left of icons that do not move, beside the plate and over the canvas, with the three ways of reading a
// pattern inside it. A narrowed group says so in its title, "1 of 64"; a kind switched off takes its group
// away. Closing the field - Escape, or the magnifier - leaves the pattern in force; the filter icon is the
// reset, live as soon as a pattern or a switched-off kind narrows the palette. Before, the palette had a text
// field across its top, a count that did not say it was narrowed, and a search that closing cleared.
import { browser, expect } from "@wdio/globals";
import { serverReady } from "../protocol.js";
import { graphLoaded, inNewestWebview, openStoryGraph } from "./storyGraph.js";

const PLATE = ".palette-scroll .search-plate";
const FIELD = `${PLATE} .search-plate-field`;
const WILDCARD = "Wildcard: * matches any text, ? matches one character";
const REGEX = "Regular expression (case-insensitive)";
const TEXT = "Plain text search";
const NOTHING_SET = "Clear all filters - no filters are set";

interface Plate {
    classes: string[];
    field: boolean;
    /** The tooltips of the plate's icons, left to right. */
    icons: string[];
    /** The left edge of the magnifier, which must not move when the field opens. */
    magnifierLeft: number;
    /** Whether the plate stands inside the dock's visible content, at its foot. */
    atFoot: boolean;
    /** The filter icon: its tooltip, and whether it can be pressed. */
    reset: { title: string; live: boolean };
}

async function plate(): Promise<Plate> {
    return browser.execute((selector: string) => {
        const el = document.querySelector<HTMLElement>(selector)!;
        const box = el.getBoundingClientRect();
        // The nearest ancestor that scrolls: the plate sticks to its bottom edge.
        let scroller: HTMLElement | null = el.parentElement;
        while (scroller && !/(auto|scroll)/.test(getComputedStyle(scroller).overflowY)) scroller = scroller.parentElement;
        const view = scroller?.getBoundingClientRect();
        const reset = el.querySelector<HTMLButtonElement>(".search-plate-reset")!;
        return {
            classes: Array.from(el.classList),
            field: el.querySelector(".search-plate-field") !== null,
            icons: Array.from(el.querySelectorAll(".search-plate-icons button")).map((b) => b.getAttribute("title") ?? ""),
            magnifierLeft: Math.round(el.querySelector(".search-plate-toggle")!.getBoundingClientRect().left),
            atFoot: view !== undefined && box.bottom <= view.bottom + 1 && view.bottom - box.bottom < 16,
            reset: { title: reset.getAttribute("title") ?? "", live: !reset.disabled },
        };
    }, PLATE);
}

interface Field {
    value: string;
    invalid: boolean;
    /** The tooltips of the pattern modes inside the field, and the one that is chosen. */
    modes: string[];
    chosen: string;
    /** The field's box against the dock and the plate: it opens beside the plate, over the canvas. */
    leftOfDock: number;
    gapToPlate: number;
    sameRow: number;
}

async function field(): Promise<Field | null> {
    return browser.execute((selector: string) => {
        const el = document.querySelector<HTMLElement>(selector)!;
        const input = el.querySelector<HTMLInputElement>(".search-plate-field");
        const box = el.querySelector<HTMLElement>(".search-plate-box");
        const dock = el.closest(".right-dock");
        if (!input || !box || !dock) return null;
        const radios = Array.from(box.querySelectorAll('[role="radio"]'));
        const at = box.getBoundingClientRect();
        const plateAt = el.getBoundingClientRect();
        return {
            value: input.value,
            invalid: input.getAttribute("aria-invalid") === "true",
            modes: radios.map((r) => r.getAttribute("title") ?? ""),
            chosen: radios.find((r) => r.getAttribute("aria-checked") === "true")?.getAttribute("title") ?? "",
            leftOfDock: dock.getBoundingClientRect().left - at.left,
            gapToPlate: plateAt.left - at.right,
            sameRow: Math.abs(at.top - plateAt.top) + Math.abs(at.bottom - plateAt.bottom),
        };
    }, PLATE);
}

/** The palette's groups as titled: name and count. */
async function groups(): Promise<Record<string, string>> {
    return browser.execute(() => {
        const out: Record<string, string> = {};
        for (const section of Array.from(document.querySelectorAll(".palette-scroll .dock-section"))) {
            const name = (section.querySelector(".dock-section-name")?.textContent ?? "").trim();
            if (name) out[name] = (section.querySelector(".section-count")?.textContent ?? "").trim();
        }
        return out;
    });
}

async function pressIn(selector: string): Promise<void> {
    const control = await browser.$(selector);
    await control.waitForExist({ timeout: 30_000 });
    await browser.execute((el: HTMLElement) => el.click(), control as unknown as HTMLElement);
    await browser.pause(300);
}

const pressIcon = (title: string): Promise<void> => pressIn(`${PLATE} .search-plate-icons button[title="${title}"]`);
const pressMode = (title: string): Promise<void> => pressIn(`${PLATE} .search-plate-box [role="radio"][title="${title}"]`);
const pressReset = (): Promise<void> => pressIn(`${PLATE} .search-plate-reset`);

/** Replaces the pattern by keys, as a reader does: setting a value on the field does not reach React. */
async function type(text: string): Promise<void> {
    const input = await browser.$(FIELD);
    await input.waitForExist({ timeout: 30_000 });
    await input.click();
    await browser.keys(["Control", "a"]);
    await browser.keys("Backspace");
    if (text !== "") await input.addValue(text);
    await browser.pause(300);
}

const ALL = { "Event types": "64", Rewards: "141" };

describe("STY-09", () => {
    before(async () => {
        await serverReady();
        await openStoryGraph("Sandbox_Alderaans_Demise_Rebel", "Rebel");
        await inNewestWebview(async () => {
            await graphLoaded();
            await (await browser.$('button.rotary-pos[title="Edit"]')).click();
            await (await browser.$(".palette-tile")).waitForExist({ timeout: 30_000 });
        });
    });

    it("is a magnifier, the two kinds and a dead filter icon at the foot of the palette, with no field while closed", async () => {
        await inNewestWebview(async () => {
            const closed = await plate();
            expect(closed.field).toBe(false);
            expect(closed.classes).not.toContain("open");
            expect(closed.icons).toEqual(["Search the node types", "Show event types", "Show rewards", NOTHING_SET]);
            expect(closed.reset.live).toBe(false);
            expect(closed.atFoot).toBe(true);
            expect(await groups()).toEqual(ALL);
        });
    });

    it("opens its field beside the plate and over the canvas, with the three modes in it, and no icon moves", async () => {
        await inNewestWebview(async () => {
            const before = await plate();
            await pressIcon("Search the node types");
            const open = await plate();
            expect(open.field).toBe(true);
            expect(open.classes).toContain("open");
            expect(open.magnifierLeft).toBe(before.magnifierLeft);
            expect(open.icons[0]).toBe("Close the search");

            const at = await field();
            expect(at?.modes).toEqual([TEXT, WILDCARD, REGEX]);
            expect(at?.chosen).toBe(TEXT);
            // The field's box reaches out of the dock, over the canvas, and meets the plate's left edge
            // on the plate's own row.
            expect(at?.leftOfDock).toBeGreaterThan(40);
            expect(Math.abs(at?.gapToPlate ?? NaN)).toBeLessThanOrEqual(1.5);
            expect(at?.sameRow).toBeLessThanOrEqual(2);

            await type("elapsed");
            // One event by either name; no reward answers, and a group with nothing in it is not drawn.
            expect(await groups()).toEqual({ "Event types": "1 of 64" });
            // One tile is far less than the dock is tall. The plate stays at the dock's foot all the
            // same: a plate that follows the last tile up moves under the hand that is typing in it.
            const narrowed = await plate();
            expect(narrowed.atFoot).toBe(true);
            expect(narrowed.magnifierLeft).toBe(before.magnifierLeft);
        });
    });

    it("keeps the pattern in force when the field closes, and resets from the filter icon", async () => {
        await inNewestWebview(async () => {
            // Escape closes the field. The palette stays narrowed, and the filter icon names what by.
            await (await browser.$(FIELD)).click();
            await browser.keys("Escape");
            await browser.pause(300);
            let closed = await plate();
            expect(closed.field).toBe(false);
            expect(closed.classes).not.toContain("open");
            expect(await groups()).toEqual({ "Event types": "1 of 64" });
            expect(closed.reset).toEqual({ title: "Clear all filters - elapsed", live: true });

            // The magnifier opens the same pattern again, and closes it again without clearing it.
            await pressIcon("Search the node types");
            expect((await field())?.value).toBe("elapsed");
            await pressIcon("Close the search");
            closed = await plate();
            expect(closed.field).toBe(false);
            expect(await groups()).toEqual({ "Event types": "1 of 64" });

            // The reset is what clears it.
            await pressReset();
            expect(await groups()).toEqual(ALL);
            expect((await plate()).reset).toEqual({ title: NOTHING_SET, live: false });
        });
    });

    it("reads the pattern as plain text, with wildcards or as a regular expression, by the mode in the field", async () => {
        await inNewestWebview(async () => {
            await pressIcon("Search the node types");
            await type("time*elapsed");
            // Plain text: no type has an asterisk in either name.
            expect(await groups()).toEqual({});

            await pressMode(WILDCARD);
            expect((await field())?.chosen).toBe(WILDCARD);
            expect(await groups()).toEqual({ "Event types": "1 of 64" });

            await pressMode(REGEX);
            await type("^story_elapsed$");
            expect(await groups()).toEqual({ "Event types": "1 of 64" });

            // A regular expression that does not compile finds nothing, and the field says it is the
            // pattern rather than the palette.
            await type("(");
            expect(await groups()).toEqual({});
            expect((await field())?.invalid).toBe(true);

            await pressMode(TEXT);
            await pressReset();
            expect((await field())?.invalid).toBe(false);
            expect(await groups()).toEqual(ALL);
            await pressIcon("Close the search");
        });
    });

    it("takes a kind's group away while that kind is switched off, which the reset puts back", async () => {
        await inNewestWebview(async () => {
            await pressIcon("Show rewards");
            expect(await groups()).toEqual({ "Event types": "64" });
            // A kind switched off is a filter: the icon is the reset, with no pattern to name.
            expect((await plate()).reset).toEqual({ title: "Clear all filters", live: true });
            await pressIcon("Show event types");
            expect(await groups()).toEqual({});

            await pressReset();
            expect(await groups()).toEqual(ALL);
            expect((await plate()).reset.live).toBe(false);
        });
    });
});

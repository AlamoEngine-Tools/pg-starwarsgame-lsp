// SET-38: the localisation editor's search is the search plate, on the grid's lower right corner (#222).
// Closed, it is a magnifier, the Inherited toggle and a filter icon; the dock keeps no search and no foot.
// Open, the field holds the three pattern modes, and the scope - which column the pattern is read in - is
// a pill on a line above it, listing every column at full length. The filter icon is the reset for the
// pattern, the scope and the toggle alike, and a regular expression that does not compile says why above
// the field. Before, the search was a block at the foot of the dock with a strip of mode buttons, a select
// and a checkbox.
import { browser, expect } from "@wdio/globals";
import { serverReady, tabLabels } from "../protocol.js";
import { inNewestWebview } from "./storyGraph.js";
import { openTextFile, rowTexts } from "./locGrid.js";

const PLATE = ".grid-column .search-plate";
const FIELD = `${PLATE} .search-plate-field`;
const OPEN = "Search the entries";
const INHERITED = "Show entries whose value is identical to the layer below this file";
const NOTHING_SET = "Clear all filters - no filters are set";
const WILDCARD = "Wildcard: * matches any text, ? matches one character";
const REGEX = "Regular expression (case-insensitive)";
const TEXT = "Plain text search";

interface Plate {
    field: boolean;
    icons: string[];
    inherited: boolean;
    magnifierLeft: number;
    /** How far the plate's corner is from the grid's own lower right corner. */
    fromRight: number;
    fromBottom: number;
    reset: { title: string; live: boolean };
}

async function plate(): Promise<Plate | null> {
    return browser.execute((selector: string) => {
        const el = document.querySelector<HTMLElement>(selector);
        const grid = document.querySelector(".grid-area");
        if (!el || !grid) return null;
        const box = el.getBoundingClientRect();
        const area = grid.getBoundingClientRect();
        const buttons = Array.from(el.querySelectorAll<HTMLButtonElement>(".search-plate-icons button"));
        const reset = el.querySelector<HTMLButtonElement>(".search-plate-reset")!;
        return {
            field: el.querySelector(".search-plate-field") !== null,
            icons: buttons.map((b) => b.getAttribute("title") ?? ""),
            inherited: buttons[1]?.getAttribute("aria-pressed") === "true",
            magnifierLeft: Math.round(el.querySelector(".search-plate-toggle")!.getBoundingClientRect().left),
            fromRight: area.right - box.right,
            fromBottom: area.bottom - box.bottom,
            reset: { title: reset.getAttribute("title") ?? "", live: !reset.disabled },
        };
    }, PLATE);
}

interface Open {
    value: string;
    marks: string[];
    /** The scope pill: what it shows, whether it is marked as set, and every row of its list. */
    scope: { shown: string; set: boolean; rows: string[] };
    /** The pill's line ends above the field's top edge. */
    pillsAboveField: boolean;
    /**
     * Why the pattern does not compile, as the line above the field says it. Not named `error`: a
     * result carrying that key is read by WebDriver as a failed command.
     */
    reason: string;
}

async function open(): Promise<Open | null> {
    return browser.execute((selector: string) => {
        const el = document.querySelector<HTMLElement>(selector);
        const input = el?.querySelector<HTMLInputElement>(".search-plate-field");
        const pill = el?.querySelector<HTMLElement>(".search-plate-pills .pill-select");
        if (!el || !input || !pill) return null;
        return {
            value: input.value,
            marks: Array.from(el.querySelectorAll('.search-plate-box [role="radio"]')).map((r) => (r.textContent ?? "").trim()),
            scope: {
                shown: (pill.querySelector(".pill-value")?.textContent ?? "").trim(),
                set: pill.classList.contains("set"),
                rows: Array.from(pill.querySelectorAll("option")).map((o) => (o.textContent ?? "").trim()),
            },
            pillsAboveField: pill.getBoundingClientRect().bottom <= input.getBoundingClientRect().top + 1,
            reason: (el.querySelector(".search-plate-error")?.textContent ?? "").trim(),
        };
    }, PLATE);
}

async function pressIn(selector: string): Promise<void> {
    const control = await browser.$(selector);
    await control.waitForExist({ timeout: 30_000 });
    await browser.execute((el: HTMLElement) => el.click(), control as unknown as HTMLElement);
    await browser.pause(500);
}

const pressIcon = (title: string): Promise<void> => pressIn(`${PLATE} .search-plate-icons button[title="${title}"]`);
const pressMode = (title: string): Promise<void> => pressIn(`${PLATE} .search-plate-box [role="radio"][title="${title}"]`);
const pressReset = (): Promise<void> => pressIn(`${PLATE} .search-plate-reset`);

/** Replaces the pattern by keys, as a reader does. */
async function type(text: string): Promise<void> {
    const input = await browser.$(FIELD);
    await input.waitForExist({ timeout: 30_000 });
    await input.click();
    await browser.keys(["Control", "a"]);
    await browser.keys("Backspace");
    if (text !== "") await input.addValue(text);
    await browser.pause(1_000);
}

/** Picks a row of the scope pill's list by what the row says, as the platform's list does. */
async function pickScope(row: string): Promise<void> {
    await browser.execute(
        (selector: string, text: string) => {
            const select = document.querySelector<HTMLSelectElement>(`${selector} .search-plate-pills select`)!;
            const option = Array.from(select.options).find((o) => (o.textContent ?? "").trim() === text)!;
            Object.getOwnPropertyDescriptor(HTMLSelectElement.prototype, "value")!.set!.call(select, option.value);
            select.dispatchEvent(new Event("change", { bubbles: true }));
        },
        PLATE,
        row,
    );
    await browser.pause(1_000);
}

describe("SET-38", () => {
    before(async () => {
        await serverReady();
        await openTextFile("Text files", "mastertextfile_english.dat");
        await browser.waitUntil(async () => (await tabLabels()).includes("Loc: mastertextfile_english.dat"), {
            timeout: 60_000,
        });
        // The Inherited count arrives once the file is compared with the game's, and widens its button;
        // the icons are measured after that, so a moved magnifier means the plate moved it.
        await inNewestWebview(async () => {
            await (await browser.$(`${PLATE} .search-plate-count`)).waitForExist({ timeout: 120_000 });
        });
    });

    it("stands on the grid's lower right corner, one row while closed, and leaves the dock without a search", async () => {
        await inNewestWebview(async () => {
            await (await browser.$(".grid-area")).waitForExist({ timeout: 60_000 });
            const closed = await plate();
            expect(closed).not.toBeNull();
            expect(closed?.field).toBe(false);
            expect(closed?.icons).toEqual([OPEN, INHERITED, NOTHING_SET]);
            expect(closed?.inherited).toBe(false);
            expect(closed?.reset.live).toBe(false);
            // On the corner, clear of the grid's scroll bars.
            expect(closed?.fromRight).toBeGreaterThanOrEqual(0);
            expect(closed?.fromRight).toBeLessThan(40);
            expect(closed?.fromBottom).toBeGreaterThanOrEqual(0);
            expect(closed?.fromBottom).toBeLessThan(40);

            const dock: { search: boolean; foot: boolean } = await browser.execute(() => ({
                search: document.querySelector(".right-dock .dock-search, .right-dock input.filter") !== null,
                foot: document.querySelector(".right-dock .dock-overview") !== null,
            }));
            expect(dock).toEqual({ search: false, foot: false });
        });
    });

    it("opens with the scope as a pill above the field, the modes inside it, and no icon moved", async () => {
        await inNewestWebview(async () => {
            const before = await plate();
            await pressIcon(OPEN);
            const at = await open();
            expect(at?.marks).toEqual(["ab", "*?", ".*"]);
            expect(at?.scope).toEqual({ shown: "All fields", set: false, rows: ["All fields", "Key only", "ENGLISH"] });
            expect(at?.pillsAboveField).toBe(true);
            expect((await plate())?.magnifierLeft).toBe(before?.magnifierLeft);
        });
    });

    it("narrows by pattern, mode and scope, and puts all of them back from the reset", async () => {
        await inNewestWebview(async () => {
            // The project's file is the game's own, so every row is inherited and hidden until the toggle.
            await pressIcon(INHERITED);
            expect((await plate())?.inherited).toBe(true);

            await type("TEXT_UNIT_TIE_*");
            // Plain text: no key has an asterisk in it.
            expect(await rowTexts()).toEqual([]);

            await pressMode(WILDCARD);
            const found = await rowTexts();
            expect(found.length).toBeGreaterThan(1);
            expect(found.every((r) => r.toUpperCase().includes("TEXT_UNIT_TIE_"))).toBe(true);

            // Read in the key alone, a pattern that only a text answers finds nothing.
            await type("fighter");
            const anywhere = (await rowTexts()).length;
            expect(anywhere).toBeGreaterThan(0);
            await pickScope("Key only");
            const at = await open();
            expect(at?.scope.shown).toBe("Key only");
            expect(at?.scope.set).toBe(true);
            const inKeys = await rowTexts();
            expect(inKeys.length).toBeGreaterThan(0);
            expect(inKeys.every((r) => r.split(" | ")[0].toUpperCase().includes("FIGHTER"))).toBe(true);

            expect((await plate())?.reset).toEqual({ title: "Clear all filters - fighter", live: true });
            await pressReset();
            const cleared = await open();
            expect(cleared?.value).toBe("");
            expect(cleared?.scope).toMatchObject({ shown: "All fields", set: false });
            const back = await plate();
            expect(back?.inherited).toBe(false);
            expect(back?.reset).toEqual({ title: NOTHING_SET, live: false });
        });
    });

    it("says above the field why a regular expression does not compile, and keeps the pattern when closed", async () => {
        await inNewestWebview(async () => {
            await pressMode(REGEX);
            await type("(");
            const broken = await open();
            expect(broken?.reason).toMatch(/^Invalid regular expression/);

            await type("^TEXT_UNIT");
            expect((await open())?.reason).toBe("");

            // Escape closes the field and nothing else.
            await (await browser.$(FIELD)).click();
            await browser.keys("Escape");
            await browser.pause(500);
            const closed = await plate();
            expect(closed?.field).toBe(false);
            expect(closed?.reset).toEqual({ title: "Clear all filters - ^TEXT_UNIT", live: true });

            await pressIcon(OPEN);
            expect((await open())?.value).toBe("^TEXT_UNIT");
            await pressMode(TEXT);
            await pressReset();
            await pressIcon("Close the search");
        });
    });
});

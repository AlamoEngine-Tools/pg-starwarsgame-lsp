// STY-12: the story simulation's trace is searched from the search plate on the trace panel's lower right
// corner (#222), as every list is: a magnifier and the filter icon while closed, the field with the three
// ways of reading a pattern while open. Plain text keeps its tick shorthand - t0 is tick 0 - and wildcards
// and regular expressions read a row with its tick first. Closing the field keeps the pattern; the filter
// icon clears it. The copy button stands in the panel's bar. Before, the trace had a plain field and the
// copy button in a strip across its top.
import { browser, expect } from "@wdio/globals";
import { serverReady } from "../protocol.js";
import { graphLoaded, inNewestWebview, openStoryGraph } from "./storyGraph.js";

const PANEL = ".sim-trace";
const PLATE = `${PANEL} .search-plate`;
const FIELD = `${PLATE} .search-plate-field`;
const OPEN = "Filter the trace";
const NOTHING_SET = "Clear all filters - no filters are set";
const TEXT = "Plain text search";
const WILDCARD = "Wildcard: * matches any text, ? matches one character";
const REGEX = "Regular expression (case-insensitive)";

interface Row {
    tick: string;
    text: string;
}

/** The trace's rows as drawn: the tick, and everything the row says. */
async function rows(): Promise<Row[]> {
    return browser.execute((selector: string) =>
        Array.from(document.querySelectorAll(`${selector} .sim-trace-row:not(.muted)`)).map((r) => ({
            tick: (r.querySelector(".sim-trace-tick")?.textContent ?? "").trim(),
            text: (r.textContent ?? "").trim(),
        })),
    PANEL);
}

interface Plate {
    field: boolean;
    icons: string[];
    reset: { title: string; live: boolean };
    /** Against the panel: on its lower right corner. */
    fromRight: number;
    fromBottom: number;
    /** The old strip across the top is gone, and the copy button stands in the bar. */
    strip: boolean;
    copyInBar: boolean;
}

async function plate(): Promise<Plate | null> {
    return browser.execute((panel: string, selector: string) => {
        const frame = document.querySelector(panel);
        const el = document.querySelector<HTMLElement>(selector);
        if (!frame || !el) return null;
        const box = el.getBoundingClientRect();
        const area = frame.getBoundingClientRect();
        const reset = el.querySelector<HTMLButtonElement>(".search-plate-reset")!;
        return {
            field: el.querySelector(".search-plate-field") !== null,
            icons: Array.from(el.querySelectorAll(".search-plate-icons button")).map((b) => b.getAttribute("title") ?? ""),
            reset: { title: reset.getAttribute("title") ?? "", live: !reset.disabled },
            fromRight: area.right - box.right,
            fromBottom: area.bottom - box.bottom,
            strip: frame.querySelector(".sim-trace-tools") !== null,
            copyInBar: frame.querySelector('.panel-bar button[title="Copy"]') !== null,
        };
    }, PANEL, PLATE);
}

async function pressIn(selector: string): Promise<void> {
    const control = await browser.$(selector);
    await control.waitForExist({ timeout: 30_000 });
    await browser.execute((el: HTMLElement) => el.click(), control as unknown as HTMLElement);
    await browser.pause(400);
}

const pressMode = (title: string): Promise<void> => pressIn(`${PLATE} .search-plate-box [role="radio"][title="${title}"]`);

async function type(text: string): Promise<void> {
    const input = await browser.$(FIELD);
    await input.waitForExist({ timeout: 30_000 });
    await input.click();
    await browser.keys(["Control", "a"]);
    await browser.keys("Backspace");
    if (text !== "") await input.addValue(text);
    await browser.pause(400);
}

describe("STY-12", () => {
    let all: Row[] = [];

    before(async () => {
        await serverReady();
        await openStoryGraph("Sandbox_Alderaans_Demise_Rebel", "Rebel");
        await inNewestWebview(async () => {
            await graphLoaded();
            await (await browser.$('button.rotary-pos[title="Simulation"]')).click();
            await (await browser.$(".sim-transport")).waitForExist({ timeout: 60_000 });
            // The trace opens with the simulation. This campaign loads its story at tick 0 and then
            // waits on the reader's decisions, so every row is at tick 0 - which is enough to tell a
            // tick that matches from one that does not.
            await browser.waitUntil(async () => (all = await rows()).length > 3, {
                timeout: 30_000,
                timeoutMsg: "The trace holds too few rows to search",
            });
            // A simulation waiting on a decision opens it as a dialog; the reader closes it to get at
            // the trace, and so does this.
            await browser.pause(1_000);
            for (let i = 0; i < 3; i++) {
                const open: boolean = await browser.execute(() => document.querySelector(".modal-backdrop") !== null);
                if (!open) break;
                await browser.keys("Escape");
                await browser.pause(600);
            }
            const covered: boolean = await browser.execute(() => document.querySelector(".modal-backdrop") !== null);
            if (covered) throw new Error("A dialog stays open over the trace");
        });
    });

    it("stands on the trace's lower right corner, with the copy button in the panel's bar", async () => {
        await inNewestWebview(async () => {
            const closed = await plate();
            expect(closed?.field).toBe(false);
            expect(closed?.icons).toEqual([OPEN, NOTHING_SET]);
            expect(closed?.reset.live).toBe(false);
            expect(closed?.fromRight).toBeGreaterThanOrEqual(0);
            expect(closed?.fromRight).toBeLessThan(32);
            expect(closed?.fromBottom).toBeGreaterThanOrEqual(0);
            expect(closed?.fromBottom).toBeLessThan(24);
            expect(closed?.strip).toBe(false);
            expect(closed?.copyInBar).toBe(true);
        });
    });

    it("keeps the tick shorthand in plain text, and reads wildcards and regular expressions with the tick first", async () => {
        await inNewestWebview(async () => {
            await pressIn(`${PLATE} button[title="${OPEN}"]`);
            const tick = all[0].tick;
            const later = `t${Number(tick.slice(1)) + 1}`;

            await type(tick);
            const byTick = await rows();
            expect(byTick.length).toBe(all.filter((r) => r.tick === tick).length);
            expect(byTick.every((r) => r.tick === tick)).toBe(true);
            // The shorthand asks for that tick exactly.
            await type(later);
            expect((await rows()).length).toBe(all.filter((r) => r.tick === later).length);

            const name = all.map((r) => r.text.slice(r.tick.length).trim()).find((t) => t.length >= 5)!;
            const fragment = name.slice(0, 4);
            const pattern = `${fragment[0]}?${fragment.slice(2)}*`;
            await type(pattern);
            // Plain text: no row has a question mark in it.
            expect(await rows()).toEqual([]);

            await pressMode(WILDCARD);
            const wild = await rows();
            expect(wild.length).toBeGreaterThan(0);
            const escape = (s: string): string => s.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
            const re = new RegExp(`${escape(fragment[0])}.${escape(fragment.slice(2))}`, "i");
            expect(wild.every((r) => re.test(r.text))).toBe(true);

            await pressMode(REGEX);
            await type(`^${tick} `);
            expect((await rows()).map((r) => r.tick)).toEqual(byTick.map((r) => r.tick));
            await type(`^${later} `);
            expect((await rows()).length).toBe(all.filter((r) => r.tick === later).length);

            await type("(");
            expect(await rows()).toEqual([]);
        });
    });

    it("keeps the pattern when the field closes, and clears it from the filter icon", async () => {
        await inNewestWebview(async () => {
            await type(all[0].tick);
            await (await browser.$(FIELD)).click();
            await browser.keys("Escape");
            await browser.pause(400);
            const closed = await plate();
            expect(closed?.field).toBe(false);
            expect(closed?.reset).toEqual({ title: `Clear all filters - ${all[0].tick}`, live: true });

            await pressIn(`${PLATE} .search-plate-reset`);
            expect((await rows()).length).toBe(all.length);
            expect((await plate())?.reset.live).toBe(false);
            await pressIn(`${PLATE} button[title="${OPEN}"]`);
            await pressMode(TEXT);
            await pressIn(`${PLATE} .search-plate-toggle`);
        });
    });
});

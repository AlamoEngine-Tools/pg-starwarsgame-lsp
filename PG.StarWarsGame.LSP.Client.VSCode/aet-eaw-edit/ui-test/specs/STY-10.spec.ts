// STY-10: the story graph's event filter is the search plate, on the canvas's lower right corner beside the
// colour key (#222). Closed it is a magnifier and the filter icon; open, the field holds the three ways of
// reading a pattern, and the branch, the lifecycle and the plot state are pills on the line above it. The
// pattern is matched on the server, in the mode the field shows: wildcards and regular expressions find the
// same events there as they would in the webview. The filter icon is the reset for all four, and the mode
// stays. The dock's foot keeps no filter. Before, the filter was a field and three selects in the dock's
// foot, read as plain text only, with a clear button among the view tools.
import { browser, expect } from "@wdio/globals";
import { serverReady } from "../protocol.js";
import { graphLoaded, inNewestWebview, openStoryGraph } from "./storyGraph.js";

const PLATE = ".canvas-area .search-plate";
const FIELD = `${PLATE} .search-plate-field`;
const OPEN = "Filter event names";
const NOTHING_SET = "Clear all filters - no filters are set";
const TEXT = "Plain text search";
const WILDCARD = "Wildcard: * matches any text, ? matches one character";
const REGEX = "Regular expression (case-insensitive)";

interface Plate {
    field: boolean;
    icons: string[];
    reset: { title: string; live: boolean };
    /** Against the canvas and the colour key: on the bottom edge, in the key's row, to its left. */
    fromBottom: number;
    leftOfKey: boolean;
    keyRow: number;
}

async function plate(): Promise<Plate | null> {
    return browser.execute((selector: string) => {
        const el = document.querySelector<HTMLElement>(selector);
        const canvas = document.querySelector(".canvas-area");
        const key = document.querySelector(".key-corner");
        if (!el || !canvas || !key) return null;
        const box = el.getBoundingClientRect();
        const keyBox = key.getBoundingClientRect();
        const reset = el.querySelector<HTMLButtonElement>(".search-plate-reset")!;
        return {
            field: el.querySelector(".search-plate-field") !== null,
            icons: Array.from(el.querySelectorAll(".search-plate-icons button")).map((b) => b.getAttribute("title") ?? ""),
            reset: { title: reset.getAttribute("title") ?? "", live: !reset.disabled },
            fromBottom: canvas.getBoundingClientRect().bottom - box.bottom,
            leftOfKey: box.right <= keyBox.left,
            keyRow: Math.abs(box.bottom - keyBox.bottom),
        };
    }, PLATE);
}

interface Open {
    marks: string[];
    chosen: string;
    /** The pills on the line above the field: what each shows, and whether it is set. */
    pills: { shown: string; set: boolean }[];
    pillsAboveField: boolean;
    /** Why the pattern does not compile. Not named `error`: WebDriver reads that key as a failure. */
    reason: string;
}

async function open(): Promise<Open | null> {
    return browser.execute((selector: string) => {
        const el = document.querySelector<HTMLElement>(selector);
        const input = el?.querySelector<HTMLInputElement>(".search-plate-field");
        const pills = Array.from(el?.querySelectorAll<HTMLElement>(".search-plate-pills .pill-select") ?? []);
        if (!el || !input || pills.length === 0) return null;
        const radios = Array.from(el.querySelectorAll('.search-plate-box [role="radio"]'));
        return {
            marks: radios.map((r) => (r.textContent ?? "").trim()),
            chosen: radios.find((r) => r.getAttribute("aria-checked") === "true")?.getAttribute("title") ?? "",
            pills: pills.map((p) => ({
                shown: (p.querySelector(".pill-value")?.textContent ?? "").trim(),
                set: p.classList.contains("set"),
            })),
            pillsAboveField: pills.every((p) => p.getBoundingClientRect().bottom <= input.getBoundingClientRect().top + 1),
            reason: (el.querySelector(".search-plate-error")?.textContent ?? "").trim(),
        };
    }, PLATE);
}

/**
 * Listens for the graphs the host hands the page. A graph of a whole campaign is drawn as an overview
 * with no node elements until the reader zooms in, so the events the server returned are read from
 * the message that carries them rather than from the page. Run inside the panel.
 */
async function listen(): Promise<void> {
    await browser.execute(() => {
        const seen = window as unknown as { aetGraphs?: string[][] };
        if (seen.aetGraphs !== undefined) return;
        seen.aetGraphs = [];
        window.addEventListener("message", (event: MessageEvent) => {
            const data = event.data as { type?: string; nodes?: { kind?: string; label?: string }[] };
            if (data?.type === "graph" && Array.isArray(data.nodes)) {
                seen.aetGraphs!.push(data.nodes.filter((n) => n.kind === "Event").map((n) => n.label ?? ""));
            }
        });
    });
}

/** The event labels of the last graph the host handed the page, sorted. */
async function events(): Promise<string[] | null> {
    return browser.execute(() => {
        const graphs = (window as unknown as { aetGraphs?: string[][] }).aetGraphs ?? [];
        return graphs.length === 0 ? null : [...graphs[graphs.length - 1]].sort();
    });
}

/** Waits until the last graph the host handed the page holds exactly these events. */
async function shows(expected: string[], what: string): Promise<void> {
    const want = [...expected].sort();
    let seen = null as string[] | null;
    await browser.waitUntil(
        async () => {
            seen = await events();
            return JSON.stringify(seen) === JSON.stringify(want);
        },
        { timeout: 30_000, timeoutMsg: `${what}: the graph holds ${seen?.length ?? "no"} events, expected ${want.length}` },
    );
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

/** Picks a row of a pill's list, as the platform's list does. Returns what the row says. */
async function pickPill(index: number, row: number): Promise<string> {
    const said = await browser.execute(
        (selector: string, at: number, option: number) => {
            const select = document.querySelectorAll<HTMLSelectElement>(`${selector} .search-plate-pills select`)[at];
            const picked = select.options[option];
            Object.getOwnPropertyDescriptor(HTMLSelectElement.prototype, "value")!.set!.call(select, picked.value);
            select.dispatchEvent(new Event("change", { bubbles: true }));
            return (picked.textContent ?? "").trim();
        },
        PLATE,
        index,
        row,
    );
    await browser.pause(400);
    return said;
}

/** The same reading of a wildcard the plate gives it: only * and ? are special. */
function wildcard(pattern: string): RegExp {
    const escaped = pattern
        .replace(/[.+^${}()|[\]\\]/g, "\\$&")
        .replace(/\*/g, ".*")
        .replace(/\?/g, ".");
    return new RegExp(escaped, "i");
}

describe("STY-10", () => {
    let all: string[] = [];

    before(async () => {
        await serverReady();
        await openStoryGraph("Sandbox_Alderaans_Demise_Rebel", "Rebel");
        await inNewestWebview(async () => {
            await graphLoaded();
            // The whole campaign, asked for again with the listener in place: a lifecycle picked and put
            // back is two requests, the second of them unfiltered. The lifecycle rather than the branch,
            // because it lists the same five values in every campaign and this one has no branches.
            await listen();
            await pressIn(`${PLATE} button[title="${OPEN}"]`);
            await pickPill(1, 1);
            await pickPill(1, 0);
            await browser.waitUntil(
                async () => {
                    const graphs: number = await browser.execute(
                        () => ((window as unknown as { aetGraphs?: string[][] }).aetGraphs ?? []).length,
                    );
                    const last = await events();
                    return graphs >= 2 && last !== null && last.length > 3 && (all = last).length > 3;
                },
                { timeout: 30_000, timeoutMsg: "No unfiltered graph arrived" },
            );
            await pressIn(`${PLATE} .search-plate-toggle`);
        });
    });

    it("stands on the canvas's lower right corner beside the colour key, and leaves the dock's foot without a filter", async () => {
        await inNewestWebview(async () => {
            const closed = await plate();
            expect(closed?.field).toBe(false);
            expect(closed?.icons).toEqual([OPEN, NOTHING_SET]);
            expect(closed?.reset.live).toBe(false);
            expect(closed?.fromBottom).toBeGreaterThanOrEqual(0);
            expect(closed?.fromBottom).toBeLessThan(24);
            expect(closed?.leftOfKey).toBe(true);
            expect(closed?.keyRow).toBeLessThanOrEqual(2);

            const foot: { search: boolean; selects: boolean; clear: boolean } = await browser.execute(() => ({
                search: document.querySelector(".right-dock .dock-search") !== null,
                selects: document.querySelector(".right-dock .filters-below") !== null,
                clear: document.querySelector('.right-dock button[title^="Clear all filters"]') !== null,
            }));
            expect(foot).toEqual({ search: false, selects: false, clear: false });
        });
    });

    it("opens with the branch, the lifecycle and the plot state as pills above the field, and the modes inside it", async () => {
        await inNewestWebview(async () => {
            await pressIn(`${PLATE} button[title="${OPEN}"]`);
            const at = await open();
            expect(at?.marks).toEqual(["ab", "*?", ".*"]);
            expect(at?.chosen).toBe(TEXT);
            expect(at?.pills).toEqual([
                { shown: "All branches", set: false },
                { shown: "Any lifecycle", set: false },
                { shown: "Any plot state", set: false },
            ]);
            expect(at?.pillsAboveField).toBe(true);
        });
    });

    it("matches the pattern on the server in the mode the field shows", async () => {
        await inNewestWebview(async () => {
            const sample = all.find((label) => label.length >= 6) ?? all[0];
            const pattern = `${sample.slice(0, 3)}*${sample.slice(-2)}`;

            // Plain text: no event has an asterisk in its name.
            await type(pattern);
            await shows([], "plain text");

            await pressMode(WILDCARD);
            await shows(
                all.filter((label) => wildcard(pattern).test(label)),
                `wildcard ${pattern}`,
            );

            await pressMode(REGEX);
            const anchored = `^${sample.slice(0, 4).replace(/[.*+?^${}()|[\]\\]/g, "\\$&")}`;
            await type(anchored);
            await shows(
                all.filter((label) => new RegExp(anchored, "i").test(label)),
                `regex ${anchored}`,
            );

            // A regular expression that does not compile finds nothing, and the line above says why.
            await type("(");
            await shows([], "regex (");
            expect((await open())?.reason).toMatch(/^Invalid regular expression/);
        });
    });

    it("resets the pattern and the pills from the filter icon, and keeps the mode", async () => {
        await inNewestWebview(async () => {
            await type(all[0].slice(0, 3));
            const lifecycle = await pickPill(1, 1);
            const at = await open();
            expect(at?.pills[1]).toEqual({ shown: lifecycle, set: true });

            // Escape closes the field and nothing else.
            await (await browser.$(FIELD)).click();
            await browser.keys("Escape");
            await browser.pause(400);
            const closed = await plate();
            expect(closed?.field).toBe(false);
            expect(closed?.reset).toEqual({ title: `Clear all filters - ${all[0].slice(0, 3)}`, live: true });

            await pressIn(`${PLATE} .search-plate-reset`);
            await shows(all, "after the reset");
            expect((await plate())?.reset.live).toBe(false);

            await pressIn(`${PLATE} button[title="${OPEN}"]`);
            const back = await open();
            expect(back?.pills.every((p) => !p.set)).toBe(true);
            expect(back?.chosen).toBe(REGEX);
            await pressMode(TEXT);
            await pressIn(`${PLATE} .search-plate-toggle`);
        });
    });
});

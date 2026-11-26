// PRV-81: the flyout a hardpoint card's info pill opens in the model preview stands beside the dock with a
// tail that points at the pill, and a second press closes it (#222). It is the same flyout the story graph's
// palette opens for a type's documentation. Before, it stood beside the dock with nothing that joined it to
// its pill. An ability's info pill opens the same flyout; this test does not press that one. The hardpoint
// list's search is the shared search plate, on the list's lower right corner: it filters the cards, its field
// opens over the stage, Escape closes the field without clearing it, and the filter icon is the reset.
import { browser, expect } from "@wdio/globals";
import { command, serverReady } from "../protocol.js";
import { press } from "./preview.js";
import { inNewestWebview } from "./storyGraph.js";

const GAP = 8;
const SLACK = 1.5;

interface Placed {
    title: string;
    rows: number;
    /** The flyout's right edge against the left edge of the dock or list the pill is in. */
    gap: number;
    tailOffPill: number;
    tailOffEdge: number;
}

/** The shown details flyouts against the pill that opened each. Run inside the preview. */
async function placed(pill: string): Promise<Placed | null> {
    return browser.execute((selector: string) => {
        const button = document.querySelector<HTMLElement>(selector);
        const box = Array.from(document.querySelectorAll<HTMLElement>(".details-flyout")).find(
            (f) => getComputedStyle(f).visibility !== "hidden",
        );
        const dock = document.querySelector(".right-dock");
        if (!button || !box || !dock) return null;
        const flyout = box.getBoundingClientRect();
        const at = button.getBoundingClientRect();
        const tail = box.querySelector(".details-tail")?.getBoundingClientRect();
        return {
            title: (box.querySelector(".details-title")?.textContent ?? "").trim(),
            rows: box.querySelectorAll(".details-body dd").length,
            gap: dock.getBoundingClientRect().left - flyout.right,
            tailOffPill: tail ? (tail.top + tail.bottom) / 2 - (at.top + at.bottom) / 2 : NaN,
            tailOffEdge: tail ? (tail.left + tail.right) / 2 - flyout.right : NaN,
        };
    }, pill);
}

describe("PRV-81", () => {
    before(async () => {
        await serverReady();
        await command("aet-eaw-edit.lsp.previewAssembledUnit", "Nebulon_B_Frigate");
        await inNewestWebview(async () => {
            const position = await browser.$('button.rotary-pos[title^="Gameplay"]');
            await position.waitForClickable({ timeout: 120_000 });
            await position.click();
            await (await browser.$("[data-card] .card-info-btn")).waitForExist({ timeout: 60_000 });
        });
    });

    it("opens a hardpoint card's measurements beside the dock, with a tail at the card's pill", async () => {
        await inNewestWebview(async () => {
            const pill = "[data-card] .card-info-btn";
            await browser.execute((selector: string) => document.querySelector(selector)?.scrollIntoView({ block: "center" }), pill);
            await press(pill);
            await browser.pause(500);

            const open = await placed(pill);
            expect(open?.title).toMatch(/^HP_/i);
            expect(open?.rows).toBeGreaterThan(0);
            expect(Math.abs((open?.gap ?? NaN) - GAP)).toBeLessThanOrEqual(SLACK);
            expect(Math.abs(open?.tailOffPill ?? NaN)).toBeLessThanOrEqual(SLACK);
            expect(Math.abs(open?.tailOffEdge ?? NaN)).toBeLessThanOrEqual(SLACK);

            await press(pill);
            await browser.pause(300);
            expect(await placed(pill)).toBeNull();
        });
    });

    // The hardpoint list's search is the shared search plate, on the list's lower right corner: the list
    // has no pane of its own, so the plate is the last thing in its section and stays on the dock's
    // bottom edge while the cards are on screen.
    it("filters the hardpoint cards from a plate on the list's lower right, which Escape closes and the reset clears", async () => {
        await inNewestWebview(async () => {
            const PLATE = ".right-dock .dock-content .search-plate.sticky";
            const cards = async (): Promise<number> => browser.execute(() => document.querySelectorAll("[data-card]").length);
            const all = await cards();
            expect(all).toBeGreaterThan(1);

            // Where the plate stands, with the top of the list in view: below the first card, against
            // the right edge of the dock's content, and inside what the dock shows.
            const at = await browser.execute((selector: string) => {
                document.querySelector("[data-card]")?.scrollIntoView({ block: "start" });
                const plate = document.querySelector<HTMLElement>(selector);
                const view = document.querySelector(".right-dock .dock-content")?.getBoundingClientRect();
                const first = document.querySelector("[data-card]")?.getBoundingClientRect();
                if (!plate || !view || !first) return null;
                const box = plate.getBoundingClientRect();
                return {
                    belowFirstCard: box.top > first.top,
                    fromRight: view.right - box.right,
                    inView: box.top >= view.top - 1 && box.bottom <= view.bottom + 1,
                    icons: Array.from(plate.querySelectorAll(".search-plate-icons button")).map((b) => b.getAttribute("title") ?? ""),
                };
            }, PLATE);
            expect(at?.belowFirstCard).toBe(true);
            expect(at?.fromRight).toBeLessThan(32);
            expect(at?.inView).toBe(true);
            expect(at?.icons).toEqual(["Filter by type or name", "Clear all filters - no filters are set"]);

            await press(`${PLATE} button[title="Filter by type or name"]`);
            const field = await browser.$(`${PLATE} .search-plate-field`);
            await field.waitForExist({ timeout: 10_000 });
            // The field opens beside the plate, out of the dock and over the stage.
            const reach: number = await browser.execute((selector: string) => {
                const box = document.querySelector(`${selector} .search-plate-box`)!.getBoundingClientRect();
                return document.querySelector(".right-dock")!.getBoundingClientRect().left - box.left;
            }, PLATE);
            expect(reach).toBeGreaterThan(40);

            await field.click();
            await field.addValue("engine");
            await browser.pause(400);
            const narrowed = await cards();
            expect(narrowed).toBeGreaterThan(0);
            expect(narrowed).toBeLessThan(all);

            // Escape closes the field; the list stays narrowed and the filter icon is the reset.
            await browser.keys("Escape");
            await browser.pause(400);
            expect(await (await browser.$(`${PLATE} .search-plate-field`)).isExisting()).toBe(false);
            expect(await cards()).toBe(narrowed);
            const reset = await browser.$(`${PLATE} .search-plate-reset`);
            expect(await reset.getAttribute("title")).toBe("Clear all filters - engine");

            await press(`${PLATE} .search-plate-reset`);
            await browser.pause(400);
            expect(await cards()).toBe(all);
        });
    });
});

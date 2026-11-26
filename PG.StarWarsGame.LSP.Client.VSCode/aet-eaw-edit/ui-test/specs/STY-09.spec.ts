// STY-09: the story graph's palette is searched from a plate at the foot of the dock, in the model tree's
// shape (#222): a magnifier and the two kinds - event types and rewards - whose field opens to the left of
// icons that do not move. A narrowed group says so in its title, "1 of 64"; a kind switched off takes its
// group away; Escape and the magnifier clear the pattern and close the field. Before, the palette had a
// text field across its top and a count that did not say it was narrowed.
import { browser, expect } from "@wdio/globals";
import { serverReady } from "../protocol.js";
import { graphLoaded, inNewestWebview, openStoryGraph } from "./storyGraph.js";

const PLATE = ".palette-scroll .search-plate";

interface Plate {
    classes: string[];
    field: boolean;
    /** The tooltips of the plate's icons, left to right. */
    icons: string[];
    /** The left edge of the magnifier, which must not move when the field opens. */
    magnifierLeft: number;
    /** Whether the plate stands inside the dock's visible content, at its foot. */
    atFoot: boolean;
}

async function plate(): Promise<Plate> {
    return browser.execute((selector: string) => {
        const el = document.querySelector<HTMLElement>(selector)!;
        const box = el.getBoundingClientRect();
        // The nearest ancestor that scrolls: the plate sticks to its bottom edge.
        let scroller: HTMLElement | null = el.parentElement;
        while (scroller && !/(auto|scroll)/.test(getComputedStyle(scroller).overflowY)) scroller = scroller.parentElement;
        const view = scroller?.getBoundingClientRect();
        return {
            classes: Array.from(el.classList),
            field: el.querySelector(".search-plate-field") !== null,
            icons: Array.from(el.querySelectorAll(".search-plate-icons button")).map((b) => b.getAttribute("title") ?? ""),
            magnifierLeft: Math.round(el.querySelector(".search-plate-toggle")!.getBoundingClientRect().left),
            atFoot: view !== undefined && box.bottom <= view.bottom + 1 && view.bottom - box.bottom < 16,
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

async function pressIcon(title: string): Promise<void> {
    const icon = await browser.$(`${PLATE} button[title="${title}"]`);
    await icon.waitForExist({ timeout: 30_000 });
    await browser.execute((el: HTMLElement) => el.click(), icon as unknown as HTMLElement);
    await browser.pause(300);
}

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

    it("is a magnifier and the two kinds at the foot of the palette, with no field while closed", async () => {
        await inNewestWebview(async () => {
            const closed = await plate();
            expect(closed.field).toBe(false);
            expect(closed.classes).not.toContain("open");
            expect(closed.icons).toEqual(["Search the node types", "Show event types", "Show rewards"]);
            expect(closed.atFoot).toBe(true);
            expect(await groups()).toEqual({ "Event types": "64", Rewards: "141" });
        });
    });

    it("opens its field to the left of icons that do not move, and says how far a group is narrowed", async () => {
        await inNewestWebview(async () => {
            const before = await plate();
            await pressIcon("Search the node types");
            const open = await plate();
            expect(open.field).toBe(true);
            expect(open.classes).toContain("open");
            expect(open.magnifierLeft).toBe(before.magnifierLeft);
            expect(open.icons[0]).toBe("Close the filter");

            const field = await browser.$(`${PLATE} .search-plate-field`);
            await field.click();
            await field.addValue("elapsed");
            await browser.pause(300);
            // One event by either name; no reward answers, and a group with nothing in it is not drawn.
            expect(await groups()).toEqual({ "Event types": "1 of 64" });
            // One tile is far less than the dock is tall. The plate stays at the dock's foot all the
            // same: a plate that follows the last tile up moves under the hand that is typing in it.
            const narrowed = await plate();
            expect(narrowed.atFoot).toBe(true);
            expect(narrowed.magnifierLeft).toBe(before.magnifierLeft);
        });
    });

    it("clears the pattern and closes on Escape, and on the magnifier", async () => {
        await inNewestWebview(async () => {
            await (await browser.$(`${PLATE} .search-plate-field`)).click();
            await browser.keys("Escape");
            await browser.pause(300);
            expect((await plate()).field).toBe(false);
            expect(await groups()).toEqual({ "Event types": "64", Rewards: "141" });

            await pressIcon("Search the node types");
            await (await browser.$(`${PLATE} .search-plate-field`)).addValue("flag");
            await browser.pause(300);
            expect((await groups())["Event types"]).toMatch(/ of 64$/);
            await pressIcon("Close the filter");
            expect((await plate()).field).toBe(false);
            expect(await groups()).toEqual({ "Event types": "64", Rewards: "141" });
        });
    });

    it("takes a kind's group away while that kind is switched off", async () => {
        await inNewestWebview(async () => {
            await pressIcon("Show rewards");
            expect(await groups()).toEqual({ "Event types": "64" });
            await pressIcon("Show event types");
            expect(await groups()).toEqual({});
            await pressIcon("Show rewards");
            await pressIcon("Show event types");
            expect(await groups()).toEqual({ "Event types": "64", Rewards: "141" });
        });
    });
});

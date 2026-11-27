// STY-08: a type's tile in the story graph's palette carries an info pill that opens the type's documentation
// beside the dock (#222): its two names, what it does, the schema's notes and every parameter with what it
// holds. A tail on the flyout's edge points at the pill. The pill is also the tile's mark for a type the engine
// does not run. A second press, Escape and the close button close it; another pill moves it. Before, a tile
// had a name and a tooltip that said how to drag it.
import { browser, expect } from "@wdio/globals";
import { serverReady } from "../protocol.js";
import { graphLoaded, inNewestWebview, openStoryGraph } from "./storyGraph.js";

const GAP = 8;
const SLACK = 1.5;

const pillOf = (type: string): string => `.palette-tile[data-type="${type}"] .tile-info`;

interface Doc {
    title: string;
    subtitle: string;
    text: string;
    /** The flyout's right edge against the dock's left edge. */
    gap: number;
    /** How far the tail's middle is from the pill's middle, down the edge. */
    tailOffPill: number;
    /** How far the tail's middle is from the flyout's right edge. */
    tailOffEdge: number;
}

/** The open documentation flyout against a type's pill, or null while none is shown. Run inside the panel. */
async function doc(type: string): Promise<Doc | null> {
    return browser.execute((pillSelector: string) => {
        const box = document.querySelector<HTMLElement>(".details-flyout.type-doc");
        const pill = document.querySelector<HTMLElement>(pillSelector);
        if (!box || !pill || getComputedStyle(box).visibility === "hidden") return null;
        const flyout = box.getBoundingClientRect();
        const at = pill.getBoundingClientRect();
        const dock = pill.closest(".right-dock")!.getBoundingClientRect();
        const tail = box.querySelector(".details-tail")?.getBoundingClientRect();
        return {
            title: (box.querySelector(".details-title")?.textContent ?? "").trim(),
            subtitle: (box.querySelector(".details-subtitle")?.textContent ?? "").trim(),
            text: (box.querySelector(".details-body")?.textContent ?? "").replace(/\s+/g, " ").trim(),
            gap: dock.left - flyout.right,
            tailOffPill: tail ? (tail.top + tail.bottom) / 2 - (at.top + at.bottom) / 2 : NaN,
            tailOffEdge: tail ? (tail.left + tail.right) / 2 - flyout.right : NaN,
        };
    }, pillOf(type));
}

/** Brings a type's tile into view and presses its info pill. Run inside the panel. */
async function pressPill(type: string): Promise<void> {
    const pill = await browser.$(pillOf(type));
    await pill.waitForExist({ timeout: 30_000 });
    await browser.execute((el: HTMLElement) => {
        el.scrollIntoView({ block: "center" });
        el.click();
    }, pill as unknown as HTMLElement);
    await browser.pause(400);
}

async function pill(type: string): Promise<{ classes: string[]; title: string; expanded: string | null }> {
    return browser.execute((selector: string) => {
        const el = document.querySelector<HTMLElement>(selector);
        return {
            classes: Array.from(el?.classList ?? []),
            title: el?.getAttribute("title") ?? "",
            expanded: el?.getAttribute("aria-expanded") ?? null,
        };
    }, pillOf(type));
}

describe("STY-08", () => {
    before(async () => {
        await serverReady();
        await openStoryGraph("Sandbox_Alderaans_Demise_Rebel", "Rebel");
        await inNewestWebview(async () => {
            await graphLoaded();
            await (await browser.$('button.rotary-pos[title="Edit"]')).click();
            await (await browser.$(".palette-tile")).waitForExist({ timeout: 30_000 });
        });
    });

    it("opens a type's documentation beside the dock, with a tail at the tile's pill", async () => {
        await inNewestWebview(async () => {
            expect(await doc("STORY_ELAPSED")).toBeNull();
            await pressPill("STORY_ELAPSED");

            const open = await doc("STORY_ELAPSED");
            expect(open?.title).toBe("Time elapsed");
            expect(open?.subtitle).toBe("STORY_ELAPSED");
            expect(open?.text).toContain("Fires after the specified number of seconds have elapsed.");
            // Its one parameter: the position, the name, what it holds, that it is optional.
            expect(open?.text).toContain("Parameters1");
            expect(open?.text).toMatch(/1\s*Seconds\s*Float\s*Optional/);
            expect(Math.abs((open?.gap ?? NaN) - GAP)).toBeLessThanOrEqual(SLACK);
            expect(Math.abs(open?.tailOffPill ?? NaN)).toBeLessThanOrEqual(SLACK);
            expect(Math.abs(open?.tailOffEdge ?? NaN)).toBeLessThanOrEqual(SLACK);
            expect((await pill("STORY_ELAPSED")).expanded).toBe("true");
        });
    });

    it("moves to another type's pill, and closes on a second press and on Escape", async () => {
        await inNewestWebview(async () => {
            await pressPill("STORY_FLAG");
            const flag = await doc("STORY_FLAG");
            expect(flag?.title).toBe("Flag compared");
            expect(Math.abs(flag?.tailOffPill ?? NaN)).toBeLessThanOrEqual(SLACK);
            expect((await pill("STORY_ELAPSED")).expanded).toBe("false");

            await pressPill("STORY_FLAG");
            expect(await doc("STORY_FLAG")).toBeNull();

            await pressPill("STORY_FLAG");
            expect(await doc("STORY_FLAG")).not.toBeNull();
            await browser.keys("Escape");
            await browser.pause(300);
            expect(await doc("STORY_FLAG")).toBeNull();
        });
    });

    it("marks a type the engine does not run on its pill, and says so in the documentation", async () => {
        await inNewestWebview(async () => {
            expect((await pill("STORY_ELAPSED")).classes).toContain("sev-info");
            const bugged = await pill("STORY_CAPTURE_HERO");
            expect(bugged.classes).toContain("sev-error");
            expect(bugged.title).toMatch(/^Does not work: /);
            expect((await pill("STORY_GARRISON_UNIT")).classes).toContain("sev-warning");

            await pressPill("STORY_CAPTURE_HERO");
            expect((await doc("STORY_CAPTURE_HERO"))?.text).toContain("Does not work: Never used in vanilla.");
            await browser.keys("Escape");
        });
    });
});

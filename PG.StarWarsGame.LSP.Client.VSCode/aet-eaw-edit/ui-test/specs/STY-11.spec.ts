// STY-11: the story graph's dock foot holds view controls only (#222): the minimap, a stack of canvas tools
// at its right - zoom in, zoom out, fit, arrange, and the two lanes - and a zoom slider with the zoom in
// percent under both. The slider follows the canvas, however it was zoomed, and zooms it. Before, the foot
// held the event filter, a tool column at the minimap's left with no zoom, and three selects.
import { browser, expect } from "@wdio/globals";
import { serverReady } from "../protocol.js";
import { graphLoaded, inNewestWebview, openStoryGraph } from "./storyGraph.js";

const FOOT = ".right-dock .dock-overview";
const TOOLS = [
    "Zoom in",
    "Zoom out",
    "Fit graph to view",
    "Arrange - recompute the automatic layout",
    "Toggle thread lanes",
    "Toggle chapter lanes",
];

interface Foot {
    tools: string[];
    toolsRightOfMinimap: boolean;
    sliderBelow: boolean;
    readout: string;
}

async function foot(): Promise<Foot | null> {
    return browser.execute((selector: string) => {
        const el = document.querySelector(selector);
        const minimap = el?.querySelector(".minimap");
        const tools = el?.querySelector(".overview-tools");
        const slider = el?.querySelector(".zoom-slider");
        if (!el || !minimap || !tools || !slider) return null;
        const m = minimap.getBoundingClientRect();
        return {
            tools: Array.from(tools.querySelectorAll("button")).map((b) => b.getAttribute("title") ?? ""),
            toolsRightOfMinimap: tools.getBoundingClientRect().left >= m.right,
            sliderBelow: slider.getBoundingClientRect().top >= m.bottom,
            readout: (slider.querySelector(".zoom-readout")?.textContent ?? "").trim(),
        };
    }, FOOT);
}

/** The zoom the readout says, in percent. */
async function percent(): Promise<number> {
    const text = (await foot())?.readout ?? "";
    const match = /^(\d+)%$/.exec(text);
    if (!match) throw new Error(`The readout says '${text}'`);
    return Number(match[1]);
}

async function press(title: string): Promise<void> {
    await browser.execute(
        (selector: string, name: string) =>
            document.querySelector<HTMLButtonElement>(`${selector} button[title="${name}"]`)?.click(),
        FOOT,
        title,
    );
    await browser.pause(600);
}

/** Moves the slider as a reader's drag would: the value, then an input event. */
async function slide(to: number): Promise<void> {
    await browser.execute(
        (selector: string, value: number) => {
            const input = document.querySelector<HTMLInputElement>(`${selector} .zoom-slider input`)!;
            Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")!.set!.call(input, String(value));
            input.dispatchEvent(new Event("input", { bubbles: true }));
        },
        FOOT,
        to,
    );
    await browser.pause(600);
}

describe("STY-11", () => {
    before(async () => {
        await serverReady();
        await openStoryGraph("Sandbox_Alderaans_Demise_Rebel", "Rebel");
        await inNewestWebview(async () => {
            await graphLoaded();
            await browser.pause(1500);
        });
    });

    it("holds the minimap, the canvas tools at its right and a zoom slider under them", async () => {
        await inNewestWebview(async () => {
            const at = await foot();
            expect(at?.tools).toEqual(TOOLS);
            expect(at?.toolsRightOfMinimap).toBe(true);
            expect(at?.sliderBelow).toBe(true);
            expect(at?.readout).toMatch(/^\d+%$/);
        });
    });

    it("zooms in and out by a quarter from the tools, and the readout follows", async () => {
        await inNewestWebview(async () => {
            const start = await percent();
            await press("Zoom in");
            const zoomedIn = await percent();
            expect(Math.abs(zoomedIn - start * 1.25)).toBeLessThanOrEqual(1);
            await press("Zoom out");
            expect(Math.abs((await percent()) - start)).toBeLessThanOrEqual(1);
        });
    });

    it("zooms the canvas from the slider, and fit brings it back", async () => {
        await inNewestWebview(async () => {
            const fitted = await percent();
            // The track is a thousand steps from 1% to 200%, by doublings: the far end is 200%.
            await slide(1000);
            expect(await percent()).toBe(200);
            await press("Fit graph to view");
            expect(Math.abs((await percent()) - fitted)).toBeLessThanOrEqual(1);
        });
    });
});

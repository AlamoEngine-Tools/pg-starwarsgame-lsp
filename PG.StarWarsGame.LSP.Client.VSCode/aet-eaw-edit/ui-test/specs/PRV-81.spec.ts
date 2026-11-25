// PRV-81: the flyout a hardpoint card's info pill opens in the model preview stands beside the dock with a
// tail that points at the pill, and a second press closes it (#222). It is the same flyout the story graph's
// palette opens for a type's documentation. Before, it stood beside the dock with nothing that joined it to
// its pill. An ability's info pill opens the same flyout; this test does not press that one.
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
});

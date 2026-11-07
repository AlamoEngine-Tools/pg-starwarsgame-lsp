// PRV-07: a particle file offers the Renderer switch; Game with no shader sources says so; the mode is per
// user. The '<n> of <m>' count needs shader sources and stays a hand check.
import { browser, expect } from "@wdio/globals";
import { serverReady } from "../protocol.js";
import { model, openInPreview, openSceneSettings, press } from "./preview.js";
import { inNewestWebview } from "./storyGraph.js";

const NO_SOURCES =
    "No shader sources are reachable, so nothing can be translated. A game install ships compiled .fxo; the .fx sources are a separate Petroglyph download.";

/** The Renderer switch's positions and which one is active. Run inside the preview. */
async function renderer(): Promise<{ labels: string[]; active: string }> {
    return browser.execute(() => {
        const group = document.querySelector('[aria-label="Which renderer draws the model"]');
        const buttons = Array.from(group?.querySelectorAll("button") ?? []);
        return {
            labels: buttons.map((b) => (b.textContent ?? "").trim()),
            active: (buttons.find((b) => b.classList.contains("active"))?.textContent ?? "").trim(),
        };
    });
}

/** A position of the Renderer switch, by its label. */
const position = (label: string): string =>
    `//*[@aria-label='Which renderer draws the model']//button[normalize-space()='${label}']`;

async function pick(label: string): Promise<void> {
    await press(position(label));
    await browser.waitUntil(async () => (await renderer()).active === label, { timeout: 5_000 });
}

/** The notes the severity tag opens, or null when the tag is disabled. Run inside the preview. */
async function notes(): Promise<{ disabled: boolean; title: string; rows: string[] }> {
    const tag = await browser.$("button.validate-btn");
    const disabled = !(await tag.isEnabled());
    const title = (await tag.getAttribute("title")) ?? "";
    if (disabled) {
        return { disabled, title, rows: [] };
    }
    await press("button.validate-btn");
    await (await browser.$(".problem-list")).waitForExist({ timeout: 5_000 });
    const rows: string[] = await browser.execute(() =>
        Array.from(document.querySelectorAll(".problem-list .problem-msg")).map((m) => (m.textContent ?? "").trim()),
    );
    await press("button.validate-btn");
    return { disabled, title, rows };
}

describe("PRV-07", () => {
    before(async () => {
        await serverReady();
    });

    it("offers the Renderer on a particle file and notes Game without sources", async () => {
        await openInPreview(model("P_fire_med00.alo"));
        await inNewestWebview(async () => {
            await openSceneSettings();
            await browser.waitUntil(async () => (await renderer()).labels.length === 2, {
                timeout: 60_000,
                timeoutMsg: "No Renderer switch in Effects",
            });
            expect((await renderer()).labels).toEqual(["Default", "Game"]);
            expect(await (await browser.$("button=Set up shader sources")).isExisting()).toBe(true);

            await pick("Default");
            await browser.pause(500);
            const quiet = await notes();
            expect(quiet.rows).not.toContain(NO_SOURCES);
            if (quiet.disabled) {
                expect(quiet.title).toBe("Nothing to report about this model");
            }

            await pick("Game");
            await browser.waitUntil(async () => (await notes()).rows.includes(NO_SOURCES), {
                timeout: 10_000,
                timeoutMsg: "Game with no sources lists no shader note",
            });
            const game = await browser.$(position("Game"));
            expect(await game.getAttribute("title")).toBe(
                "The engine's stencil shadows, which need no sources. For its effect shaders as well, set the .fx sources up.",
            );
            await pick("Default");
            // The choice reaches the host as a message after the render; a tab switched to within
            // the same moment - faster than a reader can - drops it.
            await browser.pause(2_000);
        });
    });

    it("opens the next model in the mode the particle file was left in", async () => {
        await openInPreview(model("Ev_lambdashuttle.alo"));
        await inNewestWebview(async () => {
            await openSceneSettings();
            await browser.waitUntil(async () => (await renderer()).labels.length === 2, { timeout: 60_000 });
            expect((await renderer()).active).toBe("Default");
        });
    });
});

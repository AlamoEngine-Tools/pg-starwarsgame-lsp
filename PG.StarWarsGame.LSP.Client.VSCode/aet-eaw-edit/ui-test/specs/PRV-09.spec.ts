// PRV-09: with the shader sources set, the asteroid field is drawn through the engine's bump effect under the
// Game renderer and by the preview's own shader under Default; it stays visible and frozen, and no note says
// a shader failed. That each sprite faces the camera is the effect's billboard, a picture for the eye.
import { browser, expect } from "@wdio/globals";
import { serverReady } from "../protocol.js";
import { model, openInPreview, openSceneSettings } from "./preview.js";
import { emitterProbes, emittersWhen, pause } from "./particles.js";
import { inNewestWebview } from "./storyGraph.js";

async function renderer(id: "game" | "default"): Promise<void> {
    await browser.execute((wanted: string) => {
        const group = Array.from(document.querySelectorAll('[role="radiogroup"]')).find(
            (g) => g.getAttribute("aria-label") === "Which renderer draws the model",
        );
        const option = Array.from(group?.querySelectorAll<HTMLButtonElement>('[role="radio"]') ?? []).find((b) =>
            (b.textContent ?? b.title).trim().toLowerCase().startsWith(wanted),
        );
        option?.click();
    }, id);
}

async function notes(): Promise<string[]> {
    return browser.execute(() =>
        Array.from(document.querySelectorAll(".problem-list .problem-msg")).map((m) => (m.textContent ?? "").trim()),
    );
}

describe("PRV-09", () => {
    before(async () => {
        await serverReady();
    });

    it("draws the asteroid field through the engine's effect under Game, and the preview's shader under Default", async () => {
        await openInPreview(model("P_asteroid_field00.alo"));
        await inNewestWebview(async () => {
            await emittersWhen((e) => e.some((x) => x.live > 0), 120_000, "Nothing was emitted");
            await openSceneSettings();
            await renderer("game");
            const game = await emittersWhen((e) => e.length > 0 && e.every((x) => x.throughEffect), 60_000, "The engine effect never arrived");
            const before = JSON.stringify(game.map((e) => e.positionSum));
            await pause(5_000);
            const later = await emitterProbes();
            expect(later.every((e) => e.drawn > 0)).toBe(true);
            expect(JSON.stringify(later.map((e) => e.positionSum))).toBe(before);
            expect((await notes()).filter((n) => /compile|shader/i.test(n) && /fail|error/i.test(n))).toEqual([]);

            await renderer("default");
            await emittersWhen((e) => e.every((x) => !x.throughEffect), 30_000, "The preview's shader never came back");
            expect((await emitterProbes()).every((e) => e.drawn > 0)).toBe(true);
            await renderer("game");
        });
    });
});

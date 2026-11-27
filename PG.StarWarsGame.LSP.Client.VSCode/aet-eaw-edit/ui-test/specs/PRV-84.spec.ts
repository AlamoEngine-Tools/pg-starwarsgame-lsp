// PRV-84 (#294): the unit preview shows a land unit on the model each map environment gives it. The base
// game's Stormtrooper maps seven environments in its Land_Terrain_Model_Mapping, Arctic on the snow model.
// Before, the preview only ever assembled Land_Model_Name, and the models a unit wears on other terrain
// could not be previewed at all. A unit that maps none keeps the pick on screen, disabled, saying why.
import { browser, expect } from "@wdio/globals";
import { command, serverReady } from "../protocol.js";
import { closeAll, inNewestWebview } from "./storyGraph.js";

const PILL = ".pill-select.environment";

/** A scene the host sent, or the hull's geometry it sent after one, with the time since the pick. */
interface Seen {
    kind: "scene" | "hullGlb";
    at: number;
    hull?: string;
    environment?: string | null;
    /** For the geometry: whether it carried the model's bytes, or only said the held copy stands. */
    bytes?: boolean;
}

type Watched = Window & { __prv84?: { since: number; seen: Seen[] } };

/** Starts recording what the host sends the preview. Run inside the preview. */
async function watch(): Promise<void> {
    await browser.execute(() => {
        const w = window as Watched;
        if (w.__prv84 !== undefined) {
            return;
        }
        const log = { since: performance.now(), seen: [] as Seen[] };
        w.__prv84 = log;
        w.addEventListener("message", (event: MessageEvent) => {
            const message = event.data as {
                type?: string;
                partId?: string;
                data?: unknown;
                scene?: { parts: { origin: string; modelRef: string }[]; environment?: string | null };
            } | null;
            const at = Math.round(performance.now() - log.since);
            if (message?.type === "scene" && message.scene !== undefined) {
                log.seen.push({
                    kind: "scene",
                    at,
                    hull: message.scene.parts.find((part) => part.origin === "Hull")?.modelRef ?? "",
                    environment: message.scene.environment ?? null,
                });
            } else if (message?.type === "glb" && message.partId === "hull") {
                log.seen.push({ kind: "hullGlb", at, bytes: ArrayBuffer.isView(message.data) });
            }
        });
    });
}

/**
 * Picks an environment and waits for the scene it asks for and the hull's geometry after it. Returns what
 * arrived, timed from the pick. Run inside the preview.
 */
async function switchTo(environment: string): Promise<Seen[]> {
    await browser.execute(
        (selector: string, value: string) => {
            const log = (window as Watched).__prv84!;
            log.seen = [];
            log.since = performance.now();
            const select = document.querySelector<HTMLSelectElement>(`${selector} select`)!;
            Object.getOwnPropertyDescriptor(HTMLSelectElement.prototype, "value")!.set!.call(select, value);
            select.dispatchEvent(new Event("change", { bubbles: true }));
        },
        PILL,
        environment,
    );

    // The hull's geometry AFTER the new scene: a reply still on its way for the model shown before can
    // land in between, and is not the one asked for.
    await browser.waitUntil(
        async () =>
            (await browser.execute(() => {
                const seen = (window as Watched).__prv84!.seen;
                const scene = seen.findIndex((s) => s.kind === "scene");
                return scene >= 0 && seen.slice(scene + 1).some((s) => s.kind === "hullGlb");
            })) === true,
        { timeout: 120_000, timeoutMsg: `No hull geometry arrived after ${environment} was picked` },
    );
    return browser.execute(() => (window as Watched).__prv84!.seen);
}

/** What the environment pick shows and offers. Run inside the preview. */
async function pick(): Promise<{ shown: string; title: string; disabled: boolean; options: string[] }> {
    await (await browser.$(PILL)).waitForExist({ timeout: 120_000 });
    return browser.execute((selector: string) => {
        const pill = document.querySelector(selector)!;
        const select = pill.querySelector("select")!;
        return {
            shown: (pill.querySelector(".pill-value")?.textContent ?? "").trim(),
            title: pill.getAttribute("title") ?? "",
            disabled: select.disabled,
            options: [...select.options].map((option) => option.textContent ?? ""),
        };
    }, PILL);
}

describe("PRV-84", () => {
    before(async () => {
        await serverReady();
        await closeAll();
    });

    it("assembles the unit on the model the picked environment maps to", async () => {
        await command("aet-eaw-edit.lsp.previewAssembledUnit", "Stormtrooper");
        await inNewestWebview(async () => {
            const before = await pick();
            console.log("PRV-84 before: " + JSON.stringify(before));
            expect(before.shown).toBe("Default");
            expect(before.disabled).toBe(false);
            expect(before.options[0]).toBe("Default model");
            expect(before.options).toContain("Arctic - EI_TROOPER_Snow.ALO");

            await watch();

            // Two switches, each to a model not drawn yet: the snow model, then the sand one.
            const arctic = await switchTo("Arctic");
            console.log("PRV-84 timing Arctic: " + JSON.stringify(arctic));
            const scene = arctic.find((s) => s.kind === "scene");
            expect(scene?.hull).toBe("EI_TROOPER_Snow.ALO");
            expect(scene?.environment).toBe("Arctic");

            await browser.waitUntil(async () => (await pick()).shown === "Arctic", {
                timeout: 30_000,
                timeoutMsg: "The pill did not show the picked environment",
            });
            expect((await pick()).title).toBe("Map environment Arctic - EI_TROOPER_Snow.ALO");

            const volcanic = await switchTo("Volcanic");
            console.log("PRV-84 timing Volcanic: " + JSON.stringify(volcanic));
            expect(volcanic.find((s) => s.kind === "scene")?.hull).toBe("EI_TROOPER_Sand.ALO");
        });
    });

    it("keeps the pick on screen, disabled, for a unit that maps no environment", async () => {
        await closeAll();
        await command("aet-eaw-edit.lsp.previewAssembledUnit", "Nebulon_B_Frigate");
        await inNewestWebview(async () => {
            const state = await pick();
            console.log("PRV-84 no mapping: " + JSON.stringify(state));
            expect(state.disabled).toBe(true);
            expect(state.title).toBe("No Land_Terrain_Model_Mapping");
        });
    });
});

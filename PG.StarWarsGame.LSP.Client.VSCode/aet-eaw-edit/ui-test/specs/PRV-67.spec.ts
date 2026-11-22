// PRV-67: the wreck a hardpoint sheds is in hand once the unit is on screen. Its model was asked for at the
// moment the hardpoint was destroyed, so the piece appeared a beat after the hardpoint went. The fixture's
// hardpoint attaches no model and sheds a wreck of a model no part of the unit draws: destroying it is sent
// no model, and the wreck is in the scene.
import { browser, expect } from "@wdio/globals";
import { command, serverReady } from "../protocol.js";
import { press } from "./preview.js";
import { pause } from "./particles.js";
import { inNewestWebview } from "./storyGraph.js";

type Watched = Window & { __prv67?: { modelsSent: number } };

/** Counts the model answers that carry a model's bytes from now on. Run inside the preview. */
async function watch(): Promise<void> {
    await browser.execute(() => {
        const w = window as Watched;
        const seen = { modelsSent: 0 };
        w.__prv67 = seen;
        w.addEventListener("message", (event: MessageEvent) => {
            const message = event.data as { type?: string; data?: unknown } | null;
            if (message !== null && typeof message === "object" && message.type === "glb" && ArrayBuffer.isView(message.data)) {
                seen.modelsSent++;
            }
        });
    });
}

/** How many wrecks are in the scene. Run inside the preview. */
async function wrecks(): Promise<number> {
    return browser.execute(
        () =>
            (window as unknown as { aetDebugViewport: { destruction: { breakoffs: unknown[] } } }).aetDebugViewport
                .destruction.breakoffs.length,
    );
}

describe("PRV-67", () => {
    before(async () => {
        await serverReady();
    });

    it("is sent no model when a hardpoint sheds its wreck", async () => {
        await command("aet-eaw-edit.lsp.previewAssembledUnit", "Prv_Prefetch_Unit");
        await inNewestWebview(async () => {
            let quiet = 0;
            await browser.waitUntil(
                async () => {
                    const done: boolean = await browser.execute(() => {
                        const v = (window as unknown as { aetDebugViewport?: { parts: Map<string, unknown> } })
                            .aetDebugViewport;
                        return v !== undefined && v.parts.size > 0 && document.querySelector(".load-cover") === null;
                    });
                    quiet = done ? quiet + 1 : 0;
                    return quiet >= 3;
                },
                { timeout: 180_000, interval: 1000, timeoutMsg: "The unit did not finish loading" },
            );
            expect(await wrecks()).toBe(0);

            await press('button.rotary-pos[title^="Gameplay"]');
            await pause(2_000);
            await watch();
            await press('button[title="Destroy all hardpoints"]');

            let dropped = 0;
            for (let i = 0; i < 12; i++) {
                await pause(500);
                dropped = Math.max(dropped, await wrecks());
            }
            const seen = await browser.execute(() => (window as Watched).__prv67!);
            console.log("PRV-67: " + JSON.stringify({ dropped, ...seen }));

            expect(dropped).toBe(1);
            expect(seen.modelsSent).toBe(0);
        });
    });
});

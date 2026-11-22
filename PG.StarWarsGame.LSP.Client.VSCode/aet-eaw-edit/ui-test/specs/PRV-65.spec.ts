// PRV-65: a particle system copies to the graphics card what is alive, not the room it has. Every emitter's
// buffers were sized for the game's ceiling of 5,003 particles; copied whole each frame, a capital ship's
// spent explosions - thousands of emitters with nothing alive - drew at 9 frames a second. Read as bytes
// handed to the graphics card per frame, which says the same on any machine.
import { browser, expect } from "@wdio/globals";
import { serverReady } from "../protocol.js";
import { model, openInPreview } from "./preview.js";
import { emittersWhen, pause } from "./particles.js";
import { inNewestWebview } from "./storyGraph.js";

/** What one particle takes at most: four corners of position, colour and uv, as the engine's effects draw it. */
const BYTES_PER_PARTICLE = 4 * (3 + 4 + 2) * 4;

type Counted = Window & { __prv65?: { bytes: number; frames: number } };

/** Counts the bytes of every partial buffer copy, and the frames, from now on. Run inside the preview. */
async function count(): Promise<void> {
    await browser.execute(() => {
        const w = window as Counted;
        if (w.__prv65 !== undefined) {
            w.__prv65.bytes = 0;
            w.__prv65.frames = 0;
            return;
        }
        const seen = { bytes: 0, frames: 0 };
        w.__prv65 = seen;
        const copy = WebGL2RenderingContext.prototype.bufferSubData;
        WebGL2RenderingContext.prototype.bufferSubData = function (this: WebGL2RenderingContext, ...args: unknown[]) {
            const data = args[2] as ArrayBufferView | ArrayBuffer;
            const each = (data as { BYTES_PER_ELEMENT?: number }).BYTES_PER_ELEMENT ?? 1;
            seen.bytes += typeof args[4] === "number" && args[4] > 0 ? args[4] * each : data.byteLength;
            return (copy as (...a: unknown[]) => void).apply(this, args);
        } as typeof copy;
        const tick = (): void => {
            seen.frames++;
            requestAnimationFrame(tick);
        };
        requestAnimationFrame(tick);
    });
}

describe("PRV-65", () => {
    before(async () => {
        await serverReady();
    });

    it("copies the live particles to the graphics card, not every emitter's whole room", async () => {
        await openInPreview(model("P_smoke_med00.alo"));
        const seen = await inNewestWebview(async () => {
            await emittersWhen((e) => e.some((x) => x.live > 10), 120_000, "Nothing was emitted");
            await count();
            let live = 0;
            for (let i = 0; i < 8; i++) {
                await pause(250);
                const now = await emittersWhen(() => true);
                live = Math.max(live, now.reduce((sum, e) => sum + e.live, 0));
            }
            const counted = await browser.execute(() => (window as Counted).__prv65!);
            return { ...counted, live };
        });
        console.log("PRV-65: " + JSON.stringify(seen));

        expect(seen.frames).toBeGreaterThan(20);
        expect(seen.live).toBeGreaterThan(10);
        expect(seen.bytes / seen.frames).toBeLessThan(seen.live * BYTES_PER_PARTICLE * 1.5);
    });
});

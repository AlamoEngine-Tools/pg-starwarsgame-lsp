// PRV-69: a particle system holds room for the particles it has, not for the ceiling. Every emitter kept two
// vertex streams sized for the game's ceiling of 5,003 particles - 1.2 MB an emitter, alive or not - and a
// mod's capital ship with every hardpoint destroyed held 1.4 GB of them and spent most of a second handing
// them to the graphics card in one frame. Read as the bytes the emitters' buffers hold against what is alive.
import { browser, expect } from "@wdio/globals";
import { serverReady } from "../protocol.js";
import { model, openInPreview } from "./preview.js";
import { emittersWhen, pause } from "./particles.js";
import { inNewestWebview } from "./storyGraph.js";

/**
 * What one particle takes at most in a stream: four corners of position, colour, uv and normal, and the six
 * indices of its two triangles, as the engine's effects draw it.
 */
const BYTES_PER_PARTICLE = 4 * (3 + 4 + 2 + 3) * 4 + 6 * 4;

/**
 * The bytes every emitter's buffers hold, both streams, and how many emitters there are. Each buffer once: an
 * emitter keeps its attributes in one, and shares its quad with every other. Run inside the preview.
 */
async function room(): Promise<{ bytes: number; emitters: number }> {
    return browser.execute(() => {
        interface Stream {
            index: { array: ArrayBufferView } | null;
            attributes: Record<string, { array: ArrayBufferView }>;
        }
        const rig = (
            window as unknown as {
                aetDebugViewport: {
                    particles: {
                        particleSystems: Map<
                            string,
                            { instance: { emitters: { geometry?: Stream; engine?: { geometry?: Stream } | null }[] } }
                        >;
                    };
                };
            }
        ).aetDebugViewport.particles;
        const counted = new Set<ArrayBufferLike>();
        const held = (stream: Stream | undefined): number => {
            let bytes = 0;
            for (const view of [stream?.index, ...Object.values(stream?.attributes ?? {})]) {
                if (view !== null && view !== undefined && !counted.has(view.array.buffer)) {
                    counted.add(view.array.buffer);
                    bytes += view.array.byteLength;
                }
            }
            return bytes;
        };

        let bytes = 0;
        let emitters = 0;
        for (const entry of rig.particleSystems.values()) {
            for (const emitter of entry.instance.emitters) {
                emitters++;
                bytes += held(emitter.geometry) + held(emitter.engine?.geometry);
            }
        }
        return { bytes, emitters };
    });
}

describe("PRV-69", () => {
    before(async () => {
        await serverReady();
    });

    it("holds room for the particles in the air, not for the ceiling of every emitter", async () => {
        await openInPreview(model("P_smoke_med00.alo"));
        const seen = await inNewestWebview(async () => {
            await emittersWhen((e) => e.some((x) => x.live > 10), 120_000, "Nothing was emitted");
            let live = 0;
            for (let i = 0; i < 12; i++) {
                await pause(250);
                const now = await emittersWhen(() => true);
                live = Math.max(live, now.reduce((sum, e) => sum + e.live, 0));
            }
            return { ...(await room()), live };
        });
        console.log("PRV-69: " + JSON.stringify(seen));

        expect(seen.emitters).toBeGreaterThan(0);
        expect(seen.live).toBeGreaterThan(10);

        // Room is given ahead of need - what the emitter is expected to hold, then double when it holds
        // more - so it is bounded by a few times what is alive, never by the ceiling.
        expect(seen.bytes).toBeLessThan(seen.live * BYTES_PER_PARTICLE * 4);
    });
});

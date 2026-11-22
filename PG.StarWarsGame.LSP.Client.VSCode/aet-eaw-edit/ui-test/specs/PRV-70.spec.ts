// PRV-70: an emitter costs the graphics card one buffer, and nothing while it holds no particle. Each kept
// seven buffers of its own beside a quad and an index every emitter has alike, copied six of them every frame
// it held a particle, and went through the renderer every frame it held none. A mod's capital ship with every
// hardpoint destroyed made 43,920 buffers in the frame of the press and 16,167 copies a frame while it burnt,
// for 2,631 emitters holding a particle among 4,786. Read as graphics calls, which say the same on any machine.
import { browser, expect } from "@wdio/globals";
import { command, serverReady } from "../protocol.js";
import { press } from "./preview.js";
import { pause } from "./particles.js";
import { inNewestWebview } from "./storyGraph.js";

/** The graphics calls since the count was last taken, and the frames they were made in. */
interface Calls {
    frames: number;
    copies: number;
    created: number;
    binds: number;
}

type Counted = Window & { __prv70?: Calls };

/** Counts buffer copies, buffers made and vertex arrays bound from now on. Run inside the preview. */
async function count(): Promise<void> {
    await browser.execute(() => {
        const seen: Calls = { frames: 0, copies: 0, created: 0, binds: 0 };
        (window as Counted).__prv70 = seen;
        const proto = WebGL2RenderingContext.prototype as unknown as Record<string, (...args: unknown[]) => unknown>;
        const counted = (name: string, key: "copies" | "created" | "binds"): void => {
            const original = proto[name];
            proto[name] = function (this: unknown, ...args: unknown[]) {
                seen[key]++;
                return original.apply(this, args);
            };
        };
        counted("bufferSubData", "copies");
        counted("createBuffer", "created");
        counted("bindVertexArray", "binds");
        const tick = (): void => {
            seen.frames++;
            requestAnimationFrame(tick);
        };
        requestAnimationFrame(tick);
    });
}

/** What was counted since the last taking, which starts the count again. Run inside the preview. */
async function taken(): Promise<Calls> {
    return browser.execute(() => {
        const seen = (window as Counted).__prv70!;
        const copy = { ...seen };
        seen.frames = 0;
        seen.copies = 0;
        seen.created = 0;
        seen.binds = 0;
        return copy;
    });
}

/** The emitters in the scene, and how many of them hold a particle. Run inside the preview. */
async function emitters(): Promise<{ all: number; holding: number }> {
    return browser.execute(() => {
        const rig = (
            window as unknown as {
                aetDebugViewport: {
                    particles: {
                        particleSystems: Map<string, { instance: { emitters: { particles: unknown[] }[] } }>;
                    };
                };
            }
        ).aetDebugViewport.particles;
        let all = 0;
        let holding = 0;
        for (const entry of rig.particleSystems.values()) {
            for (const emitter of entry.instance.emitters) {
                all++;
                if (emitter.particles.length > 0) {
                    holding++;
                }
            }
        }
        return { all, holding };
    });
}

describe("PRV-70", () => {
    before(async () => {
        await serverReady();
    });

    it("makes one buffer for an emitter, copies one a frame, and leaves an empty emitter alone", async () => {
        await command("aet-eaw-edit.lsp.previewAssembledUnit", "Star_Destroyer");
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
                { timeout: 180_000, interval: 1000, timeoutMsg: "The Star Destroyer did not finish loading" },
            );

            await press('button.rotary-pos[title^="Gameplay"]');
            await pause(2_000);

            // What the unit costs a frame before anything burns: its own meshes and effects.
            await count();
            await pause(1_000);
            const idle = await taken();
            const before = await emitters();

            await press('button[title="Destroy all hardpoints"]');
            await pause(400);
            const pressed = await taken();
            const added = (await emitters()).all - before.all;

            // A stretch of the burning, with the emitters counted at both ends of it.
            const from = await emitters();
            await pause(600);
            const burning = await taken();
            const to = await emitters();
            const holding = Math.max(from.holding, to.holding);

            const perFrame = {
                idleCopies: idle.copies / Math.max(1, idle.frames),
                idleBinds: idle.binds / Math.max(1, idle.frames),
                copies: burning.copies / Math.max(1, burning.frames),
                binds: burning.binds / Math.max(1, burning.frames),
            };
            console.log(
                "PRV-70: " +
                    JSON.stringify({ added, created: pressed.created, all: to.all, holding, frames: burning.frames, perFrame }),
            );

            expect(added).toBeGreaterThan(20);
            expect(holding).toBeGreaterThan(10);
            expect(burning.frames).toBeGreaterThan(5);

            // One buffer for each emitter put in the scene. The wrecks dropped at the same press bring the
            // buffers of their own meshes, which this count cannot tell apart, so the bound is well under
            // the nine an emitter each had rather than at the one it has.
            expect(pressed.created).toBeLessThan(added * 4);
            // One copy a frame for each emitter that holds a particle, over what the unit copied before.
            expect(perFrame.copies).toBeLessThan(perFrame.idleCopies + holding * 1.5);
            // And one vertex array bound for each of those: an emitter holding nothing is not drawn.
            expect(perFrame.binds).toBeLessThan(perFrame.idleBinds + holding * 1.5);
        });
    });
});

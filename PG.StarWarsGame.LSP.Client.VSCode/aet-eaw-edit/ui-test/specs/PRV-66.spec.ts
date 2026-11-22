// PRV-66: every effect a unit can start is in hand once the unit is on screen. A hardpoint's death explosion
// was asked for at the moment the hardpoint was destroyed - once per hardpoint, 145 times on a mod's capital
// ship - and its textures only after that, so the explosion arrived seconds after the press. The Star
// Destroyer's hardpoints all name one explosion and a wreck that trails fire: destroying every one of them
// asks the server for no particle system, no texture and no effect, and is sent no wreck's model: each was
// sent with the unit. And none of it runs before it is started. Destroying every hardpoint kills the unit,
// which hides its hull: the explosions play all the same and leave when they have burnt out, where they
// were held on the hidden hull, unplayed, until the unit was repaired.
import { browser, expect } from "@wdio/globals";
import { command, serverReady } from "../protocol.js";
import { press } from "./preview.js";
import { pause } from "./particles.js";
import { inNewestWebview } from "./storyGraph.js";

/** The answers the preview was sent since the watch started, by kind. */
interface Answers {
    systems: number;
    textures: number;
    shaders: number;
    /** Model answers that carried the model's bytes. One for a model already held carries none. */
    models: number;
}

type Watched = Window & { __prv66?: Answers };

/** Counts the answers the preview is sent from now on. Run inside the preview. */
async function watch(): Promise<void> {
    await browser.execute(() => {
        const w = window as Watched;
        const seen: Answers = { systems: 0, textures: 0, shaders: 0, models: 0 };
        w.__prv66 = seen;
        w.addEventListener("message", (event: MessageEvent) => {
            const message = event.data as { type?: string; data?: unknown } | null;
            if (message === null || typeof message !== "object") {
                return;
            }
            if (message.type === "particleSystem") {
                seen.systems++;
            } else if (message.type === "texture") {
                seen.textures++;
            } else if (message.type === "shader") {
                seen.shaders++;
            } else if (message.type === "glb" && ArrayBuffer.isView(message.data)) {
                seen.models++;
            }
        });
    });
}

/** The effects in the scene that something started rather than the model carrying them. Run inside the preview. */
async function startedEffects(): Promise<string[]> {
    return browser.execute(() => {
        const rig = (
            window as unknown as {
                aetDebugViewport: { particles: { particleSystems: Map<string, { origin: string }> } };
            }
        ).aetDebugViewport.particles;
        return [...rig.particleSystems].filter(([, entry]) => entry.origin === "gameplay").map(([id]) => id);
    });
}

/** The explosions in the scene and the particles they have in the air. Run inside the preview. */
async function explosionsNow(): Promise<{ count: number; live: number }> {
    return browser.execute(() => {
        const rig = (
            window as unknown as {
                aetDebugViewport: {
                    particles: {
                        particleSystems: Map<
                            string,
                            { instance: { emitters: { particles: unknown[] }[] } }
                        >;
                    };
                };
            }
        ).aetDebugViewport.particles;
        let count = 0;
        let live = 0;
        for (const [id, entry] of rig.particleSystems) {
            if (id.startsWith("explosion:")) {
                count++;
                live += entry.instance.emitters.reduce((sum, e) => sum + e.particles.length, 0);
            }
        }
        return { count, live };
    });
}

/** How many wrecks are in the scene. Run inside the preview. */
async function wrecks(): Promise<number> {
    return browser.execute(() => {
        const destruction = (
            window as unknown as { aetDebugViewport: { destruction: { breakoffs: unknown[] } } }
        ).aetDebugViewport.destruction;
        return destruction.breakoffs.length;
    });
}

/** Waits until the unit is on screen: its parts loaded and the cover gone, three readings running. */
async function onScreen(): Promise<void> {
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
}

describe("PRV-66", () => {
    before(async () => {
        await serverReady();
    });

    it("starts none of a unit's effects when the unit opens", async () => {
        await command("aet-eaw-edit.lsp.previewAssembledUnit", "Star_Destroyer");
        await inNewestWebview(async () => {
            await onScreen();
            await pause(3_000);
            const started = await startedEffects();
            console.log("PRV-66 on open: " + JSON.stringify(started));

            expect(started).toEqual([]);
        });
    });

    it("asks the server for nothing when every hardpoint is destroyed", async () => {
        await inNewestWebview(async () => {
            await press('button.rotary-pos[title^="Gameplay"]');
            await pause(2_000);
            await watch();
            await press('button[title="Destroy all hardpoints"]');

            // The explosions are in the scene the moment they are started: counted over the first seconds,
            // since each leaves once it has burnt out.
            let explosions = 0;
            let dropped = 0;
            let live = 0;
            for (let i = 0; i < 24; i++) {
                await pause(500);
                const started = await startedEffects();
                explosions = Math.max(explosions, started.filter((id) => id.startsWith("explosion:")).length);
                dropped = Math.max(dropped, await wrecks());
                live = Math.max(live, (await explosionsNow()).live);
            }
            const answers = await browser.execute(() => (window as Watched).__prv66!);
            // The hardpoints' explosions, not a wreck's own: those go off when the wreck burns out, later.
            const left = (await startedEffects()).filter((id) => id.startsWith("explosion:HP_")).length;
            console.log("PRV-66 destroyed: " + JSON.stringify({ explosions, live, left, dropped, ...answers }));

            expect(explosions).toBeGreaterThan(0);
            expect(live).toBeGreaterThan(0);
            expect(left).toBe(0);
            expect(dropped).toBeGreaterThan(0);
            expect(answers.systems).toBe(0);
            expect(answers.textures).toBe(0);
            expect(answers.shaders).toBe(0);
            expect(answers.models).toBe(0);
        });
    });
});

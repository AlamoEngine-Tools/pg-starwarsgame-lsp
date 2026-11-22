// PRV-68: destroying every hardpoint is one change to the scene. Each explosion put in the scene re-ran the
// visibility chain over every row and measured the model twice, and the rules pass after it re-ran the chain
// once per effect of the unit - on a mod's capital ship 4.0 of the 5.9 seconds the press froze the preview
// for. The Star Destroyer's hardpoints all blow up at one press: the chain and the measuring run a few times
// for the press and for each wreck that arrives, not once per explosion and per effect.
import { browser, expect } from "@wdio/globals";
import { command, serverReady } from "../protocol.js";
import { press } from "./preview.js";
import { pause } from "./particles.js";
import { inNewestWebview } from "./storyGraph.js";

/** How often the whole-scene work has run since the watch started. */
interface Runs {
    rows: number;
    bounds: number;
}

type Watched = Window & { __prv68?: Runs };

/** Counts every run of the row chain and every measuring of the model from now on. Run inside the preview. */
async function watch(): Promise<void> {
    await browser.execute(() => {
        const viewport = (
            window as unknown as {
                aetDebugViewport: {
                    rows: { resolveRows: () => void };
                    cameraRig: { boundingSphere: (includeEffects?: boolean) => unknown };
                };
            }
        ).aetDebugViewport;
        const seen: Runs = { rows: 0, bounds: 0 };
        (window as Watched).__prv68 = seen;

        const resolve = viewport.rows.resolveRows.bind(viewport.rows);
        viewport.rows.resolveRows = () => {
            seen.rows++;
            resolve();
        };

        const measure = viewport.cameraRig.boundingSphere.bind(viewport.cameraRig);
        viewport.cameraRig.boundingSphere = (includeEffects?: boolean) => {
            seen.bounds++;
            return measure(includeEffects);
        };
    });
}

/** What has run so far. Run inside the preview. */
async function runs(): Promise<Runs> {
    return browser.execute(() => ({ ...(window as Watched).__prv68! }));
}

/** The explosions in the scene. Run inside the preview. */
async function explosions(): Promise<number> {
    return browser.execute(() => {
        const rig = (
            window as unknown as {
                aetDebugViewport: { particles: { particleSystems: Map<string, unknown> } };
            }
        ).aetDebugViewport.particles;
        return [...rig.particleSystems.keys()].filter((id) => id.startsWith("explosion:")).length;
    });
}

/** The wrecks in the scene. Run inside the preview. */
async function wrecks(): Promise<number> {
    return browser.execute(
        () =>
            (window as unknown as { aetDebugViewport: { destruction: { breakoffs: unknown[] } } }).aetDebugViewport
                .destruction.breakoffs.length,
    );
}

describe("PRV-68", () => {
    before(async () => {
        await serverReady();
    });

    it("runs the row chain and measures the model per event of the press, not per explosion and per effect", async () => {
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

            // What an idle second costs, so the press is not charged for what runs every frame anyway.
            await watch();
            await pause(1_000);
            const idle = await runs();

            await press('button[title="Destroy all hardpoints"]');
            await pause(1_000);
            const after = await runs();
            const started = await explosions();

            // The second with the press in it, less what the idle second before it ran.
            const pressed = {
                rows: after.rows - idle.rows - idle.rows,
                bounds: after.bounds - idle.bounds - idle.bounds,
            };
            const dropped = await wrecks();
            console.log("PRV-68: " + JSON.stringify({ started, dropped, idle, pressed }));

            expect(started).toBeGreaterThan(4);

            // What is left is counted in events, not in explosions or effects: the press, the rules pass
            // after it and the unit's own death, and then each wreck, which arrives in a message of its
            // own - its model, then what it trails.
            expect(pressed.rows).toBeLessThanOrEqual(6 + 2 * dropped);
            expect(pressed.bounds).toBeLessThanOrEqual(4 + dropped);
        });
    });
});

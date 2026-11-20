// PRV-52 (#289): a particle file gets the transport the animation player has. Pause freezes the frame on
// screen; Stop holds every emitter and lets the particles already in the air finish their lives; Play after
// Stop and Restart play the system again from nothing, which is what replays a burst. Before, a particle file
// had no controls at all: a burst played once and the only way to see it again was to reopen the file.
// The file's Skeleton control, which has no bones to draw, is disabled and says why.
import { browser, expect } from "@wdio/globals";
import { serverReady } from "../protocol.js";
import { model, openInPreview, press } from "./preview.js";
import { type EmitterProbe, emitterProbes, emittersWhen, positionSet } from "./particles.js";
import { closeAll, inNewestWebview } from "./storyGraph.js";

/** A control of the particle transport, by the title it carries while it can be pressed. */
const control = (title: string): string => `.particle-transport button[title="${title}"]`;

const live = (emitters: EmitterProbe[]): number => emitters.reduce((n, e) => n + e.live, 0);

/** What a frame holds: every emitter's drawn positions and the summed age of its particles. */
const frame = (emitters: EmitterProbe[]): string =>
    JSON.stringify(emitters.map((e) => ({ at: positionSet(e), age: e.ageSum.toFixed(4) })));

/** Samples the live count until it reaches zero, and returns every sample. */
async function drainSamples(timeout: number): Promise<number[]> {
    const samples: number[] = [];
    await browser.waitUntil(
        async () => {
            samples.push(live(await emitterProbes()));
            return samples[samples.length - 1] === 0;
        },
        { timeout, interval: 100, timeoutMsg: "The particles in the air never finished" },
    );
    return samples;
}

describe("PRV-52", () => {
    before(async () => {
        await serverReady();
        await closeAll();
    });

    it("pauses P_fire_med00 on one frame, and Play runs it on", async () => {
        await openInPreview(model("P_fire_med00.alo"));
        await inNewestWebview(async () => {
            await emittersWhen((e) => live(e) > 0, 120_000, "The fire never drew");
            await press(control("Pause"));
            await browser.pause(300);

            const held = frame(await emitterProbes());
            await browser.pause(1_000);
            const later = frame(await emitterProbes());
            expect(later).toBe(held);

            await press(control("Play"));
            await browser.pause(500);
            expect(frame(await emitterProbes())).not.toBe(held);

            // A particle file has no bones, so the Skeleton control stays on show, disabled, and says why.
            const skeleton = await browser.execute(() =>
                Array.from(document.querySelectorAll<HTMLButtonElement>(".skeleton-field button")).map((b) => ({
                    disabled: b.disabled,
                    title: b.title,
                })),
            );
            expect(skeleton).toHaveLength(3);
            expect(skeleton.every((b) => b.disabled && b.title === "No skeleton loaded")).toBe(true);
        });
    });

    it("stops P_fire_med00: nothing new spawns, the fire dies out, and Play lights it again", async () => {
        await inNewestWebview(async () => {
            const before = live(await emitterProbes());
            expect(before).toBeGreaterThan(0);

            await press(control("Stop emitting"));
            const samples = await drainSamples(60_000);
            console.log("PRV-52 stop: " + JSON.stringify({ before, samples: samples.length, first: samples[0] }));

            // The particles in the air were not cut: the first sample still holds them.
            expect(samples[0]).toBeGreaterThan(0);
            // And nothing was born after the press: the count only ever went down.
            for (let i = 1; i < samples.length; i++) {
                expect(samples[i]).toBeLessThanOrEqual(samples[i - 1]);
            }
            // Still a system on stage, held rather than removed.
            expect((await emitterProbes()).length).toBeGreaterThan(0);

            await press(control("Play from the start"));
            await emittersWhen((e) => live(e) > 0, 30_000, "Play after Stop did not light the fire again");
        });
    });

    it("replays the burst of P_explosion_small00 once it is over", async () => {
        await closeAll();
        await openInPreview(model("P_explosion_small00.alo"));
        await inNewestWebview(async () => {
            await emittersWhen((e) => live(e) > 0, 120_000, "The explosion never drew");
            await emittersWhen((e) => e.length > 0 && live(e) === 0, 60_000, "The burst never finished");
            await browser.pause(1_000);
            expect(live(await emitterProbes())).toBe(0);

            await press(control("Restart"));
            await emittersWhen((e) => live(e) > 0, 30_000, "Restart did not replay the burst");
        });
    });
});

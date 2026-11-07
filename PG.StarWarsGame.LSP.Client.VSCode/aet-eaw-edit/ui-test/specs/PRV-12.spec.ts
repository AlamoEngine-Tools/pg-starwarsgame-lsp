// PRV-12: a cube-shaped volume is used at its full size - the stored value is the half-extent and each axis is
// drawn between minus and plus it. Read off newborn particles: their spawn spread, their starting speeds.
import { browser, expect } from "@wdio/globals";
import { serverReady } from "../protocol.js";
import { model, openInPreview, openSceneSettings, setSlider } from "./preview.js";
import { type EmitterProbe, emitterProbes, emittersWhen, pause } from "./particles.js";
import { inNewestWebview } from "./storyGraph.js";

/**
 * Newborn particles' positions - before their speed has carried them off - and every particle's speed: across the
 * ground a spark's speed is its birth speed for life, as gravity pulls along y alone.
 */
async function newborn(file: string, emitter: string): Promise<{ declared: EmitterProbe; positions: number[][]; speeds: number[][] }> {
    await openInPreview(model(file));
    return inNewestWebview(async () => {
        await emittersWhen((e) => e.some((x) => x.name === emitter && x.live > 0), 120_000, `No '${emitter}' particles`);
        await openSceneSettings();
        await setSlider("Wind speed", 0);
        const positions: number[][] = [];
        const speeds: number[][] = [];
        let declared: EmitterProbe | undefined;
        for (let i = 0; i < 300 && (positions.length < 40 || speeds.length < 40); i++) {
            // A damage effect throws one burst and stops: replayed every second, so enough of it is seen.
            if (i % 10 === 9) {
                await browser.execute(() =>
                    (window as unknown as { aetDebugViewport: { restartParticles(): void } }).aetDebugViewport.restartParticles(),
                );
            }
            for (const e of (await emitterProbes()).filter((x) => x.name === emitter)) {
                declared = e;
                for (const p of e.particles.filter((x) => x.age < 0.06)) {
                    positions.push(p.drawn);
                }
                // The cube's own part: the outward push along the birth direction taken off.
                speeds.push(
                    ...e.particles
                        .filter((x) => x.age < 0.06)
                        .map((p) => p.velocity.map((v, axis) => v - p.inward[axis] * e.inwardSpeed)),
                );
            }
            await pause(100);
        }
        return { declared: declared!, positions, speeds };
    });
}

const spread = (values: number[][], axis: number): number => Math.max(...values.map((v) => Math.abs(v[axis])));

describe("PRV-12", () => {
    before(async () => {
        await serverReady();
    });

    it("spawns the fire across the whole cube its position volume declares", async () => {
        const { declared, positions } = await newborn("P_fire_empire_med00.alo", "fire");
        expect(declared.position.shape).toBe("Cube");
        const half = declared.position.sideLength;
        expect(positions.length).toBeGreaterThan(20);
        // Half the cube would reach 0.5 of the stored value at most; the full one reaches it.
        expect(Math.max(spread(positions, 0), spread(positions, 2))).toBeGreaterThan(half * 0.75);
    });

    // Judged on the speed's MAGNITUDE: the volume is in the emitter's own frame, which its bone turns, and
    // a length does not care. A full cube reaches up to the square root of three times the half-extent;
    // half of one never passes 0.87 of it.
    it("throws the sparks at the full speed of their cube-shaped speed volume", async () => {
        const { declared, speeds } = await newborn("P_damage_med00.alo", "sparks");
        expect(declared.speed.shape).toBe("Cube");
        const half = declared.speed.sideLength;
        expect(speeds.length).toBeGreaterThan(10);
        const fastest = Math.max(...speeds.map((v) => Math.hypot(v[0], v[1], v[2])));
        expect(fastest).toBeGreaterThan(half);
        expect(fastest).toBeLessThan(half * 1.8);
    });
});

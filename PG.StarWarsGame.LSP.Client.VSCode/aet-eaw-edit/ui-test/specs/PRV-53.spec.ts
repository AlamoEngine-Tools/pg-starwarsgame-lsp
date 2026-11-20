// PRV-53 (#190): an effect keeps pace with the bone it is anchored on while a clip moves it. The Slave 1's
// takeoff, Ev_slave1_takeoff_00 on Ev_slave1.alo, moves the whole ship by its Root bone. For 240 display
// frames, sampled right after each frame's particle step, every emitter's live particles keep the same
// distance from their anchor however fast the anchor moves: a lag behind the bone would grow with its speed.
// Reported on 0.4.0; measured clean on 2026-11-09, and pinned here.
import { browser, expect } from "@wdio/globals";
import { serverReady } from "../protocol.js";
import { model, openInPreview } from "./preview.js";
import { closeAll, inNewestWebview } from "./storyGraph.js";

/** One emitter on one display frame. */
interface Sample {
    emitter: string;
    /** How far the anchor moved since the frame before, in simulation units. */
    speed: number;
    /** The distance from the anchor to the centroid of the emitter's live particles. */
    centroid: number;
    /** The distance from the anchor to the youngest live particle. */
    newest: number;
}

const FRAMES = 240;

/** The least-squares slope of `ys` over `xs`. */
function slope(xs: number[], ys: number[]): number {
    const mean = (v: number[]): number => v.reduce((a, b) => a + b, 0) / v.length;
    const mx = mean(xs);
    const my = mean(ys);
    let num = 0;
    let den = 0;
    for (let i = 0; i < xs.length; i++) {
        num += (xs[i] - mx) * (ys[i] - my);
        den += (xs[i] - mx) ** 2;
    }
    return den === 0 ? 0 : num / den;
}

describe("PRV-53", () => {
    before(async () => {
        await serverReady();
        await closeAll();
    });

    it("keeps the Slave 1's effects on their bones through its takeoff", async () => {
        await openInPreview(model("Ev_slave1.alo"));
        await inNewestWebview(async () => {
            await browser.waitUntil(
                async () =>
                    browser.execute(() => {
                        // eslint-disable-next-line @typescript-eslint/no-explicit-any
                        const v = (window as unknown as { aetDebugViewport?: any }).aetDebugViewport;
                        return (
                            v !== undefined &&
                            v.parts.size > 0 &&
                            (v.animationNames as string[]).some((n) => /takeoff/i.test(n)) &&
                            v.particles.particleSystems.size >= 2
                        );
                    }),
                { timeout: 180_000, interval: 500, timeoutMsg: "The Slave 1, its clips or its effects did not load" },
            );
            await browser.pause(2_000);

            // Wraps the particle step, so each sample is taken after the frame's bones AND particles moved -
            // the state the frame is drawn from.
            await browser.execute((frames: number) => {
                // eslint-disable-next-line @typescript-eslint/no-explicit-any
                const v = (window as unknown as { aetDebugViewport: any }).aetDebugViewport;
                const rig = v.particles;
                const space = v.modelRoot;
                const previous = new Map<string, { x: number; y: number; z: number }>();
                const samples: unknown[][] = [];
                const step = rig.step.bind(rig);
                (window as unknown as { aetLagSamples: unknown[][] }).aetLagSamples = samples;

                rig.step = (dt: number, view: unknown): void => {
                    step(dt, view);
                    if (samples.length >= frames) {
                        return;
                    }
                    const frame: unknown[] = [];
                    for (const [id, entry] of rig.particleSystems) {
                        const anchor = entry.instance.anchorNode;
                        if (anchor === null) {
                            continue;
                        }
                        anchor.updateWorldMatrix(true, false);
                        space.updateWorldMatrix(true, false);
                        const local = space.matrixWorld.clone().invert().multiply(anchor.matrixWorld);
                        const at = anchor.position.clone().setFromMatrixPosition(local);
                        const before = previous.get(id) ?? at;
                        const speed = Math.hypot(at.x - before.x, at.y - before.y, at.z - before.z);
                        previous.set(id, { x: at.x, y: at.y, z: at.z });

                        const names: string[] = entry.instance.emitterNames();
                        names.forEach((name, index) => {
                            const live = entry.instance.emitterParticles(index) as {
                                position: { x: number; y: number; z: number };
                                age: number;
                            }[];
                            if (live.length === 0) {
                                return;
                            }
                            let x = 0;
                            let y = 0;
                            let z = 0;
                            let newest = live[0];
                            for (const p of live) {
                                x += p.position.x;
                                y += p.position.y;
                                z += p.position.z;
                                if (p.age < newest.age) {
                                    newest = p;
                                }
                            }
                            const n = live.length;
                            frame.push({
                                emitter: `${id}/${name}#${index}`,
                                speed,
                                centroid: Math.hypot(x / n - at.x, y / n - at.y, z / n - at.z),
                                newest: Math.hypot(
                                    newest.position.x - at.x,
                                    newest.position.y - at.y,
                                    newest.position.z - at.z,
                                ),
                            });
                        });
                    }
                    samples.push(frame);
                };

                const name = (v.animationNames as string[]).find((n) => /takeoff/i.test(n));
                v.setAnimationLoop(false);
                v.setPaused(false);
                v.play(name);
            }, FRAMES);

            await browser.waitUntil(
                async () =>
                    browser.execute(
                        (frames: number) =>
                            ((window as unknown as { aetLagSamples?: unknown[] }).aetLagSamples?.length ?? 0) >= frames,
                        FRAMES,
                    ),
                { timeout: 60_000, interval: 250, timeoutMsg: "Fewer than 240 frames were drawn" },
            );
            const samples = (
                await browser.execute(() => (window as unknown as { aetLagSamples: Sample[][] }).aetLagSamples)
            ).flat();

            const byEmitter = new Map<string, Sample[]>();
            for (const sample of samples) {
                byEmitter.set(sample.emitter, [...(byEmitter.get(sample.emitter) ?? []), sample]);
            }
            const report = [...byEmitter].map(([emitter, rows]) => {
                const moving = rows.filter((r) => r.speed > 0.05);
                const range = (key: "centroid" | "newest"): [number, number] => [
                    Math.min(...moving.map((r) => r[key])),
                    Math.max(...moving.map((r) => r[key])),
                ];
                return {
                    emitter,
                    frames: rows.length,
                    moving: moving.length,
                    maxSpeed: Math.max(...rows.map((r) => r.speed)),
                    centroid: range("centroid"),
                    newest: range("newest"),
                    centroidSlope: slope(
                        moving.map((r) => r.speed),
                        moving.map((r) => r.centroid),
                    ),
                    newestSlope: slope(
                        moving.map((r) => r.speed),
                        moving.map((r) => r.newest),
                    ),
                };
            });
            console.log("PRV-53 lag: " + JSON.stringify(report));

            // Measured 2026-11-20 over six runs. A lag of one display frame puts every particle `speed`
            // units behind its anchor, up to 2 units at the end of this window; with the anchor read a
            // frame late, Orange's youngest particle spread over 1.46 units and the Lambda Engines
            // centroid reached 2.26 with a slope of +0.70. Each bound below sits between the two.
            const orange = report.find((r) => /\/Orange#/.test(r.emitter));
            const engines = report.find((r) => /\/Lambda Engines#/.test(r.emitter));
            expect(orange).toBeDefined();
            expect(engines).toBeDefined();

            // The clip moved both, and fast: about 2.0 units per display frame at the end.
            for (const r of [orange!, engines!]) {
                expect(r.moving).toBeGreaterThan(200);
                expect(r.maxSpeed).toBeGreaterThan(1.5);
                // The youngest particle is never further out than the emitter puts it (measured 5.04 at most).
                expect(r.newest[1]).toBeLessThan(5.5);
            }

            // Orange spawns on a point: its youngest particle sits 5.044 units from the anchor on every
            // moving frame, to within 0.000002. A lag would add the anchor's speed to it.
            expect(orange!.newest[1] - orange!.newest[0]).toBeLessThan(0.05);

            // Lambda Engines spawns in a volume, so its youngest particle lands anywhere from 0.35 to 4.17
            // out; its centroid holds 0.33 to 0.82 from the anchor and does not climb with the speed
            // (slope -0.16 to -0.03 per unit of speed). One-sided: a lag only ever adds distance.
            expect(engines!.centroid[0]).toBeGreaterThan(0.2);
            expect(engines!.centroid[1]).toBeLessThan(1.2);
            expect(engines!.centroidSlope).toBeLessThan(0.35);
        });
    });
});

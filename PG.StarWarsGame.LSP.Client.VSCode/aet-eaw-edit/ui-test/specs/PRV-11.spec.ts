// PRV-11: a spark's streak follows its speed - longest when fastest, never one constant length, never absurd.
// That a spark flying at the camera reads as a dot is tailGeometry's, covered by the webview unit tests.
import { expect } from "@wdio/globals";
import { serverReady } from "../protocol.js";
import { model, openInPreview } from "./preview.js";
import { correlation, emitterProbes, emittersWhen, pause } from "./particles.js";
import { inNewestWebview } from "./storyGraph.js";

describe("PRV-11", () => {
    before(async () => {
        await serverReady();
    });

    it("draws a spark's streak as long as it is fast", async () => {
        await openInPreview(model("P_explosion_empire_atat00.alo"));
        await inNewestWebview(async () => {
            await emittersWhen((e) => e.some((x) => x.name === "sparks" && x.live > 5), 120_000, "No sparks were thrown");
            const speeds: number[] = [];
            const stretches: number[] = [];
            for (let i = 0; i < 30 && speeds.length < 300; i++) {
                for (const e of (await emitterProbes()).filter((x) => x.name === "sparks")) {
                    for (const p of e.particles) {
                        speeds.push(Math.hypot(...p.velocity));
                        stretches.push(p.stretch);
                    }
                }
                await pause(150);
            }
            expect(speeds.length).toBeGreaterThan(30);
            expect(correlation(speeds, stretches)).toBeGreaterThan(0.5);
            expect(Math.max(...stretches) - Math.min(...stretches)).toBeGreaterThan(0.2);
            expect(Math.max(...stretches)).toBeLessThan(30);
        });
    });
});
